using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // method_5 is the meds queue step that EFT runs at animation events to apply heal
    // effects body-part by body-part. for our phantom MedsItemClass it would call
    // DoMedEffect (already short-circuited to null by PhantomMedShortCircuitPatch),
    // see null, then mark FailedToApply=true and tear the controller down within
    // a frame, nuking the unzip animation before its visible. this prefix skips
    // method_5 entirely for phantom items so the animation plays out until the
    // flourish coroutine cancels it at the configured delay.
    internal class PhantomMedsMethod5Patch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player.MedsController.ObservedMedsControllerClass), "method_5");
        }

        [PatchPrefix]
        static bool Prefix(Player.MedsController.ObservedMedsControllerClass __instance)
        {
            var item = __instance?.MedsController_0?.Item;
            if (item == null) return true;
            if (!MedpouchUnzipFlourish.IsPhantomTpl(item.TemplateId)) return true;
            return false;
        }
    }
}
