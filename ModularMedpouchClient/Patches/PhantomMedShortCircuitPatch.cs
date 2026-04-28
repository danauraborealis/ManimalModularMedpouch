using System.Reflection;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // when the unzip flourish fires Player.Proceed on a phantom MedsItemClass, EFT's
    // MedsController eventually calls ActiveHealthController.DoMedEffect, which tries
    // to read Item.Parent on the phantom. phantoms aren't in any inventory so the
    // parent walk NPEs and crashes the animation. this prefix short-circuits DoMedEffect
    // for phantoms, returning null so the meds controller treats the step as a no-op.
    // the animation keeps playing until the coroutine fires CancelApplyingItem.
    internal class PhantomMedShortCircuitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoMedEffect));
        }

        [PatchPrefix]
        static bool Prefix(Item item, ref IEffect __result)
        {
            if (item == null) return true;
            if (!MedpouchUnzipFlourish.IsPhantomTpl(item.TemplateId)) return true;
            __result = null;
            return false;
        }
    }
}
