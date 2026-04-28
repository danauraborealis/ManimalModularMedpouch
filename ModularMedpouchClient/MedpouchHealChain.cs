using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;

namespace Manimal.ModularMedpouch
{
    // single-press auto heal chain. when a medkit hotkey fires we pick the highest
    // priority effect the player has thats serviceable from this pouch, fire it via
    // Player.TryProceed (game auto picks affected limb via BodyPartsPriority), then on each
    // hands-controller-out-of-meds transition we look at whether any tracked health metric
    // changed:
    //   - changed -> heal made progress, reevaluate priority and fire the next applicable item
    //   - unchanged -> player canceled -> stop chain
    // chain also stops when no priority bucket has a matching item left in this pouch.
    internal static class MedpouchHealChain
    {
        // limb-affecting effects we recognize. matched by Type.Name against
        // HealthControllerClass nested protected effect classes. 
        private const string EffectHeavyBleeding = "HeavyBleeding";
        private const string EffectLightBleeding = "LightBleeding";
        private const string EffectFracture      = "Fracture";

        private static readonly EBodyPart[] LimbParts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach,
            EBodyPart.LeftArm, EBodyPart.RightArm,
            EBodyPart.LeftLeg, EBodyPart.RightLeg,
        };

        // bucket name -> ordered list of items IDs that handle that effect
        private static Dictionary<string, List<string>> _effectItems = new Dictionary<string, List<string>>();
        private static List<string> _priority = new List<string>();

        // chain state
        private static Player _player;
        private static CompoundItem _pouch;
        private static bool _running;
        private static Player.MedsController _hookedMeds; // currently subscribed-to controller, for clean unsubscribe
        // set to true whenever EffectAddedEvent/EffectRemovedEvent fires during the chain.
        // a successful heal always raises one of these 
        // a canceled mid-animation heal never raises them. 
        // checked at each chain step and reset at the start of the next.
        private static bool _progressSinceLastStep;

        public static void Configure(List<string> priority, Dictionary<string, List<string>> effectItems)
        {
            _priority = priority ?? new List<string>();
            _effectItems = effectItems ?? new Dictionary<string, List<string>>();
        }

        // preview-only: would TryStart find a med to fire right now? used by the unzip
        // flourish to decide whether to bother playing the animation.
        public static bool WouldStart(Player player, CompoundItem pouch)
        {
            return SelectNext(player, pouch) != null;
        }

        // pre-pick the first med to fire so the unzip flourish can hand it back to us
        // verbatim after the post-cancel transient where CanApplyItem temporarily rejects
        // everything. returns null if nothing applies right now.
        public static MedsItemClass PreSelect(Player player, CompoundItem pouch)
        {
            return SelectNext(player, pouch)?.Med;
        }

        // skip-the-gates entry used by the post-flourish path. fires `med` directly,
        // wires up the chain, no checks. caller is responsible for ensuring `med` is
        // a valid MedsItemClass in the pouch.
        public static void StartWithMed(Player player, CompoundItem pouch, MedsItemClass med, Callback<IHandsController> firstCallback)
        {
            End();
            _player = player;
            _pouch = pouch;
            _running = true;
            _progressSinceLastStep = false;
            player.HealthController.EffectAddedEvent   += OnEffectChanged;
            player.HealthController.EffectRemovedEvent += OnEffectChanged;
            player.OnHandsControllerChanged += OnHandsChanged;
            // intentionally NOT hooking the existing MedsController here. it's the unzip
            // flourish phantom. its OnOutUseEvent will fire mid-animation and trigger
            // AdvanceChain with no progress, ending the chain prematurely. let OnHandsChanged
            // pick up the next real MedsController when EFT swaps to it.

            Plugin.LogSource?.LogInfo($"[Medpouch] chain start (post-flourish): firing tpl={med.TemplateId}");
            player.TryProceed(med, firstCallback, true);
        }

        // returns true if a med was selected and TryProceed-d. caller should treat that as
        // "we handled input, dont fall through to vanilla". returns false if no applicable
        // med was found, caller should invoke its callback with null and bail.
        // bypassTransientGates skips IsChangingWeapon and CheckAction, both of which
        // transiently fail during the post-flourish weapon-switch. the chain steps
        // themselves dont check either, so its safe to skip here too.
        public static bool TryStart(Player player, CompoundItem pouch, Callback<IHandsController> firstCallback, bool bypassTransientGates = false)
        {
            var pick = SelectNext(player, pouch);
            if (pick == null) return false;

            var med = pick.Med;
            if (!bypassTransientGates)
            {
                if (!med.CheckAction(null).Succeeded) return false;
                if (player.InventoryController.IsChangingWeapon) return false;
            }
            if (player.IsInBufferZone &&
                !player.CanManipulateWithHandsInBufferZone &&
                !player.HealthController.IsItemForHealing(med))
            {
                return false;
            }

            End(); // in case a previous chain didnt clean up
            _player = player;
            _pouch = pouch;
            _running = true;
            _progressSinceLastStep = false;
            player.HealthController.EffectAddedEvent   += OnEffectChanged;
            player.HealthController.EffectRemovedEvent += OnEffectChanged;

            // OnHandsControllerChanged fires when entering a new MedsController. each meds
            // application creates a fresh controller (Process<MedsController, ...>), so we
            // hook the IN-transition to grab the new instance and subscribe to its
            // OnOutUseEvent THAT is what fires when the animation ends. EFT does NOT
            // transition out of MedsController between consecutive meds, so relying on the
            // OUT-transition alone (as we did initially) misses every chain step.
            player.OnHandsControllerChanged += OnHandsChanged;

            // if the player is already in a meds animation when we start (rare but possible),
            // hook that controller directly so we still chain.
            if (player.HandsController is Player.MedsController existing) HookMeds(existing);

            Plugin.LogSource?.LogInfo($"[Medpouch] chain start: firing first med tpl={med.TemplateId}");
            player.TryProceed(med, firstCallback, true);
            return true;
        }

        // event fires as (prev, next), see Player.HandsController setter at Player.cs:5785.
        // primary chain trigger: transition OUT of MedsController (the heal animation finished
        // and the player went back to whatever they had in hands before).
        // we also hook OnOutUseEvent on the IN transition as a backup signal, but in some
        // setups (e.g. animation transpiles by other mods) that event may not fire.
        private static void OnHandsChanged(Player.AbstractHandsController prev, Player.AbstractHandsController next)
        {
            Plugin.LogSource?.LogInfo($"[Medpouch][trace] HandsController: {TypeName(prev)} -> {TypeName(next)} (running={_running})");
            if (!_running || _player == null) return;

            // IN transition: hook OnOutUseEvent as backup and remember the controller
            if (next is Player.MedsController newMeds && newMeds != _hookedMeds)
            {
                HookMeds(newMeds);
                return;
            }

            // OUT-of-meds transition. fires next med immediately on natural completion.
            // TryProceed interrupts the weapon-switch and chains into the next meds without
            // the firearm ever appearing. SKIP if the controller leaving is our phantom
            // unzip controller. StartWithMed is in the middle of swapping it out for a
            // real med and we'd otherwise advance with progress=false and end the chain.
            if (prev is Player.MedsController prevMeds && !(next is Player.MedsController))
            {
                if (prevMeds.Item != null && MedpouchUnzipFlourish.IsPhantomTpl(prevMeds.Item.TemplateId))
                {
                    Plugin.LogSource?.LogInfo("[Medpouch][trace] skipping out-of-meds advance (phantom controller swap)");
                    return;
                }
                AdvanceChain("out-of-meds", prevMeds);
            }
        }

        private static string TypeName(object o) => o == null ? "null" : o.GetType().Name;

        // any effect add/remove during a meds animation = the heal did something. cancel
        // mid-animation never raises these. IEffect is in the global namespace.
        private static void OnEffectChanged(IEffect effect)
        {
            _progressSinceLastStep = true;
        }

        private static void HookMeds(Player.MedsController meds)
        {
            if (_hookedMeds != null) _hookedMeds.OnOutUseEvent -= OnMedsCompleted;
            _hookedMeds = meds;
            meds.OnOutUseEvent += OnMedsCompleted;
            Plugin.LogSource?.LogInfo($"[Medpouch] hooked OnOutUseEvent on MedsController#{meds.GetHashCode():X}");
        }

        // backup signal: animation event "OutUse" calls MedsController.OnOutUse() which raises
        // OnOutUseEvent. fires before the hands transition. delegates into AdvanceChain
        // using the still-current _hookedMeds as the just-finished controller.
        private static void OnMedsCompleted()
        {
            Plugin.LogSource?.LogInfo($"[Medpouch][trace] OnMedsCompleted fired (running={_running})");
            AdvanceChain("OnOutUseEvent", _hookedMeds);
        }

        // re-entrancy guard so paired (OUT-of-meds + OnOutUseEvent) signals only step once
        private static int _stepGuard;
        private static void AdvanceChain(string source, Player.MedsController justFinished)
        {
            if (!_running || _player == null) return;
            int myStep = ++_stepGuard;

            // unsubscribe from this specific controller. the next Proceed creates a new one
            if (_hookedMeds != null) _hookedMeds.OnOutUseEvent -= OnMedsCompleted;
            _hookedMeds = null;

            // cancel detection: a successful heal always raises EffectAddedEvent or
            // EffectRemovedEvent on the player's HealthController. 
            // canceling mid-animation never raises them, so an unset flag = cancel.
            bool progress = _progressSinceLastStep;
            Plugin.LogSource?.LogInfo($"[Medpouch] step from {source} (progress={progress})");
            _progressSinceLastStep = false;
            if (!progress)
            {
                Plugin.LogSource?.LogInfo("[Medpouch] cancel detected (no effect change since last step), ending chain");
                End();
                return;
            }
            if (myStep != _stepGuard) return; // a later step already ran, abandon

            var pick = SelectNext(_player, _pouch);
            if (pick == null)
            {
                Plugin.LogSource?.LogInfo("[Medpouch] no more applicable meds, ending chain");
                End();
                return;
            }

            // intentionally NOT calling CheckAction or checking IsChangingWeapon: between meds
            // the player is mid weapon-switch back to firearm and CheckAction can transiently
            // fail. let TryProceed handle validation, and trust SelectNext for applicability.
            Plugin.LogSource?.LogInfo($"[Medpouch] chain step: firing tpl={pick.Med.TemplateId} bucket={pick.Bucket}");
            _player.TryProceed(pick.Med, null, true);
        }

        private static void End()
        {
            if (_hookedMeds != null) _hookedMeds.OnOutUseEvent -= OnMedsCompleted;
            if (_player != null)
            {
                _player.OnHandsControllerChanged -= OnHandsChanged;
                _player.HealthController.EffectAddedEvent   -= OnEffectChanged;
                _player.HealthController.EffectRemovedEvent -= OnEffectChanged;
            }
            _hookedMeds = null;
            _player = null;
            _pouch = null;
            _running = false;
            _progressSinceLastStep = false;
        }

        // selection

        private sealed class SelectedMed
        {
            public string Bucket;
            public MedsItemClass Med;
        }

        private static SelectedMed SelectNext(Player player, CompoundItem pouch)
        {
            foreach (var bucket in _priority)
            {
                if (!PlayerNeeds(player, bucket)) continue;
                if (!_effectItems.TryGetValue(bucket, out var allowedTpls)) continue;

                var med = FindMedByTpls(player, pouch, allowedTpls);
                if (med != null) return new SelectedMed { Bucket = bucket, Med = med };
                // no item for this bucket -> fall through to next priority
            }
            return null;
        }

        private static bool PlayerNeeds(Player player, string bucket)
        {
            switch (bucket)
            {
                case "heavyBleed": return AnyLimbHasEffect(player, EffectHeavyBleeding);
                case "lightBleed": return AnyLimbHasEffect(player, EffectLightBleeding);
                case "fracture":   return AnyLimbHasEffect(player, EffectFracture);
                case "hpHeal":     return AnyLimbBelowMaxHp(player);
                default:           return false;
            }
        }

        private static bool AnyLimbHasEffect(Player player, string effectTypeName)
        {
            foreach (var bp in LimbParts)
            {
                foreach (var e in player.HealthController.GetAllActiveEffects(bp))
                {
                    if (e == null) continue;
                    if (e.GetType().Name == effectTypeName) return true;
                }
            }
            return false;
        }

        private static bool AnyLimbBelowMaxHp(Player player)
        {
            var hc = player.ActiveHealthController;
            if (hc == null) return false;
            foreach (var bp in LimbParts)
            {
                if (hc.IsBodyPartDestroyed(bp)) continue;
                var v = hc.GetBodyPartHealth(bp, false);
                // blacked limbs land at Current=0 but IsBodyPartDestroyed may not catch
                // them. AI-2 cant heal a 0-hp limb so skip it. otherwise the chain
                // re-fires AI-2 forever while blacked limbs keep total HP below max.
                if (v.Current <= 0f) continue;
                if (v.Current < v.Maximum) return true;
            }
            return false;
        }

        private static MedsItemClass FindMedByTpls(Player player, CompoundItem pouch, List<string> allowedTpls)
        {
            // walk the pouch in container order so layout dictates preference within a bucket
            foreach (var container in pouch.Containers)
            {
                foreach (var inner in container.Items)
                {
                    var meds = inner as MedsItemClass;
                    if (meds == null) continue;
                    if (!allowedTpls.Contains(meds.TemplateId)) continue;
                    if (!player.HealthController.CanApplyItem(meds, EBodyPart.Common)) continue;
                    return meds;
                }
            }
            return null;
        }

    }
}
