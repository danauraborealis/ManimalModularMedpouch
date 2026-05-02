using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // runs on the exact container the loot UI is about to show.
    // this catches corpse inventories even when death/corpse creation used a copied owner.
    internal class SearchPanelMedkitConvertPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(SimpleStashPanel),
                nameof(SimpleStashPanel.Show),
                new[]
                {
                    typeof(CompoundItem),
                    typeof(InventoryController),
                    typeof(ItemContextAbstractClass),
                    typeof(bool),
                    typeof(SortingTableItemClass),
                    typeof(SimpleStashPanel.EStashSearchAvailability),
                    typeof(InventoryController),
                    typeof(ItemsPanel.EItemsTab)
                });
        }

        [PatchPrefix]
        static void Prefix(CompoundItem item)
        {
            BotMedkitDeathConverter.Convert(item);
        }
    }
}
