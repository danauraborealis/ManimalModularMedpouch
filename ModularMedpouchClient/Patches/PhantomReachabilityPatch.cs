using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // TryProceed gates on InventoryController.IsAtReachablePlace which rejects our
    // phantom MedsItemClass since the phantom isnt in any inventory and has no
    // CurrentAddress. force-true for phantom IDs so TryProceed can do its full
    // setup including the hands-out transition that actually plays the unzip.
    internal class PhantomReachabilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAtReachablePlace));
        }

        [PatchPostfix]
        static void Postfix(Item item, ref bool __result)
        {
            if (__result) return;
            if (item == null) return;
            if (MedpouchUnzipFlourish.IsPhantomTpl(item.TemplateId)) __result = true;
        }
    }
}
