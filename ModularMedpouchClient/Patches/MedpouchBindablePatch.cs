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
        // template id of Trenchfoot-BeltSlot's hidden belt holder item. checked
        // by walking the item's parent chain so medkit containers sitting inside
        // a belt (via that mod's BeltHolder.mod_belt slot) count as a valid
        // bindable place. soft compat - no runtime ref to BeltSlot. if BeltSlot
        // isn't installed nothing in any user's inventory matches this id so the
        // walk is a no-op extra branch.
        private const string BeltHolderTpl = "6815465859b8c6ff13f94100";

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

            // must sit inside one of the rig/pocket fast-access slots OR inside
            // the Trenchfoot-BeltSlot belt holder hierarchy. without this extra
            // branch, medpouches in the belt fail the spatial check even though
            // BeltSlot's own patch makes vanilla treat the belt as fast-access -
            // because we re-do our OWN FastAccessSlots check here that doesn't
            // see BeltSlot's relaxation.
            bool inFastAccessSlot = Inventory.FastAccessSlots
                .Select(es => __instance.Inventory.Equipment.GetSlot(es))
                .Any(s => s == parentSlot);
            if (!inFastAccessSlot && !IsInBeltHolder(item)) return;

            if (!__instance.Examined(item)) return;

            __result = true;
        }

        // walks an item's address chain looking for the BeltSlot mod's holder
        // template. depth cap matches BeltSlot's own helper - the real chain is
        // typically 3 levels (item -> belt -> holder) so 8 is generous.
        private static bool IsInBeltHolder(Item item)
        {
            var current = item;
            for (int i = 0; i < 8 && current != null; i++)
            {
                if (current.StringTemplateId == BeltHolderTpl) return true;
                var parent = current.CurrentAddress?.Container?.ParentItem;
                if (parent == null || parent == current) break;
                current = parent;
            }
            return false;
        }
    }
}
