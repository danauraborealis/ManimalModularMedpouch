using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // catches bot med clones when the corpse pocket grid draws them.
    internal class GridItemViewMedkitConvertPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(GridItemView),
                nameof(GridItemView.NewGridItemView),
                new[]
                {
                    typeof(Item),
                    typeof(ItemContextAbstractClass),
                    typeof(ItemRotation),
                    typeof(TraderControllerClass),
                    typeof(IItemOwner),
                    typeof(FilterPanel),
                    typeof(global::IContainer),
                    typeof(ItemUiContext),
                    typeof(InsuranceCompanyClass),
                    typeof(GClass2067)
                });
        }

        [PatchPrefix]
        static void Prefix(ref Item item)
        {
            BotMedkitDeathConverter.TryConvertItem(ref item);
        }
    }
}
