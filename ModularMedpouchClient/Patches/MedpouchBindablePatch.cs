using System.Linq;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // IsAtBindablePlace has a hard type whitelist (weapon, meds, food, throwable, etc).
    // a container cant pass that check so the medpouch (or any vanilla medkit weve
    // converted into a container) never shows the bind UI. postfix flips the result to true
    // for any ID in Plugin.ContainerTpls while still requiring the same location/examined
    // gates the original enforced.
    internal class MedpouchBindablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.IsAtBindablePlace));
        }

        [PatchPostfix]
        static void Postfix(InventoryController __instance, Item item, ref bool __result)
        {
            if (__result) return;
            if (item == null || !Plugin.ContainerTpls.Contains(item.TemplateId)) return;

            // mirror the original early exit conditions
            if (item.CurrentAddress == null) return;
            if (item.Parent is GClass3390) return;

            var parentItemAddress = item.Parent.Container.ParentItem.CurrentAddress;
            var parentSlot = (parentItemAddress != null ? parentItemAddress.Container : null) as Slot;
            if (parentSlot == null) return;

            // must sit inside one of the rig/pocket fast-access slots
            bool inFastAccessSlot = Inventory.FastAccessSlots
                .Select(es => __instance.Inventory.Equipment.GetSlot(es))
                .Any(s => s == parentSlot);
            if (!inFastAccessSlot) return;

            if (!__instance.Examined(item)) return;

            __result = true;
        }
    }
}
