using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Manimal.ModularMedpouch
{
    // plays the start of a medkit's vanilla unzip animation by spinning up a phantom
    // MedsItemClass via EFT's item factory using a clone ID registered server-side.
    // after AnimationDelaySeconds we cancel the heal and hand off to MedpouchHealChain.
    //
    // if anything goes wrong (no map entry, factory fails, no real meds applicable),
    // we fall back to firing the heal chain directly so the hotkey never feels broken.
    internal static class MedpouchUnzipFlourish
    {
        // originalTpl -> phantomAnimTpl, populated at startup from medkitAnimationMap.json
        private static Dictionary<string, string> _animMap = new Dictionary<string, string>();
        // reverse lookup of phantom IDs used by PhantomMedShortCircuitPatch to skip the
        // real heal effect on phantoms.
        private static HashSet<string> _phantomTpls = new HashSet<string>();
        // per-ID unzip delay overrides. IDs absent here use _defaultDelaySeconds.
        private static Dictionary<string, float> _perTplDelay = new Dictionary<string, float>();
        private static float _defaultDelaySeconds = 0.5f;

        public static void Configure(Dictionary<string, string> animMap, float defaultDelaySeconds, Dictionary<string, float> perTplDelay)
        {
            _animMap = animMap ?? new Dictionary<string, string>();
            _defaultDelaySeconds = defaultDelaySeconds;
            _perTplDelay = perTplDelay ?? new Dictionary<string, float>();
            _phantomTpls = new HashSet<string>(_animMap.Values);
        }

        public static bool IsPhantomTpl(string tpl) => tpl != null && _phantomTpls.Contains(tpl);

        private static float DelayFor(string tpl)
        {
            return _perTplDelay.TryGetValue(tpl, out var d) ? d : _defaultDelaySeconds;
        }

        // entry point. returns true if we either fired the unzip flourish or the heal chain;
        // false if nothing was applicable and the caller should signal "no-op" to vanilla.
        public static bool Trigger(Player player, CompoundItem pouch, Callback<IHandsController> firstCallback)
        {
            // skip the flourish if disabled, no map entry, or no real heal would fire anyway
            float delay = DelayFor(pouch.TemplateId);
            string animTpl = null;
            if (delay > 0f) _animMap.TryGetValue(pouch.TemplateId, out animTpl);
            bool canFireUnzip = animTpl != null && Plugin.Instance != null;

            if (!canFireUnzip)
            {
                return MedpouchHealChain.TryStart(player, pouch, firstCallback);
            }

            // snapshot the med we're going to fire BEFORE the unzip animation runs.
            // CanApplyItem flips to false for the ~half second of post-cancel cleanup,
            // so re-running SelectNext after the delay would reject everything. firing
            // the snapshotted med directly via StartWithMed sidesteps that.
            var presel = MedpouchHealChain.PreSelect(player, pouch);
            if (presel == null)
            {
                firstCallback?.Invoke(null);
                return false;
            }

            var phantom = TryCreatePhantom(animTpl);
            if (phantom == null)
            {
                Plugin.LogSource?.LogWarning($"[Medpouch] phantom creation failed for animTpl={animTpl}, falling back to immediate heal");
                return MedpouchHealChain.TryStart(player, pouch, firstCallback);
            }

            Plugin.LogSource?.LogInfo($"[Medpouch] unzip flourish: tpl={pouch.TemplateId} animTpl={animTpl} delay={delay}s presel={presel.TemplateId} hands={player.HandsController?.GetType().Name} reachable={player.InventoryController.IsAtReachablePlace(phantom)}");

            // listen for transitions during the 0.5s window so we can see if TryProceed
            // actually drove the controller into MedsController
            void Diag(Player.AbstractHandsController prev, Player.AbstractHandsController next)
            {
                Plugin.LogSource?.LogInfo($"[Medpouch][diag] phantom transition: {prev?.GetType().Name ?? "null"} -> {next?.GetType().Name ?? "null"}");
            }
            player.OnHandsControllerChanged += Diag;
            Plugin.Instance.StartCoroutine(UnsubscribeDiagAfter(player, Diag, delay + 1f));

            // use Player.Proceed (not TryProceed). TryProceed bails silently for our phantom
            // somewhere in its setup even with reachability patched. Proceed goes straight
            // to spawning the MedsController, which is what actually plays the unzip.
            Plugin.LogSource?.LogInfo($"[Medpouch][diag] before Proceed: ProcessStatus={player.ProcessStatus} handsItem={player.HandsController?.Item?.TemplateId ?? "null"}");
            try
            {
                player.Proceed(phantom, new GStruct382<EBodyPart>(EBodyPart.Common), new Callback<GInterface203>(_ => { }), 0, false);
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[Medpouch] Player.Proceed threw: {ex}, falling back");
                return MedpouchHealChain.TryStart(player, pouch, firstCallback);
            }
            Plugin.LogSource?.LogInfo($"[Medpouch][diag] after Proceed: ProcessStatus={player.ProcessStatus} handsItem={player.HandsController?.Item?.TemplateId ?? "null"}");

            // stop vanilla from invoking firstCallback now; we'll call it ourselves once the
            // chain actually starts after the delay
            Plugin.Instance.StartCoroutine(WaitThenStartChain(player, pouch, presel, firstCallback, delay));
            return true;
        }

        private static IEnumerator UnsubscribeDiagAfter(Player player, System.Action<Player.AbstractHandsController, Player.AbstractHandsController> diag, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (player != null) player.OnHandsControllerChanged -= diag;
        }

        private static IEnumerator WaitThenStartChain(Player player, CompoundItem pouch, MedsItemClass med, Callback<IHandsController> firstCallback, float delay)
        {
            yield return new WaitForSeconds(delay);
            // do NOT call CancelApplyingItem. that swaps back to firearm, which is the
            // weapon-popping-out artifact. instead, fire the real med via TryProceed while
            // we're still in the phantom MedsController. EFT detects the existing meds
            // controller and does Meds(phantom) -> Meds(real) directly without firearm.
            MedpouchHealChain.StartWithMed(player, pouch, med, firstCallback);
        }

        private static MedsItemClass TryCreatePhantom(string animTpl)
        {
            try
            {
                var factory = Singleton<ItemFactoryClass>.Instance;
                if (factory == null) return null;
                var item = factory.CreateItem(MongoID.Generate(true), animTpl, null);
                return item as MedsItemClass;
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[Medpouch] factory CreateItem threw: {ex.Message}");
                return null;
            }
        }
    }
}
