using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // hotkey press -> Player.SetQuickSlotItem fetches the bound item and dispatches it.
    // for any converted-medkit container, we hand off to MedpouchHealChain
    // which picks the highest priority needed item from the pouch and auto-chains follow-up
    // applications until either nothing is applicable or the player cancels mid animation.
    internal class MedpouchHotkeyUsePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.SetQuickSlotItem));
        }

        [PatchPrefix]
        static bool Prefix(Player __instance, EBoundItem quickSlot, Callback<IHandsController> callback)
        {
            var bound = __instance.InventoryController.Inventory.FastAccess.GetBoundItem(quickSlot);
            if (bound == null) return true;
            if (!Plugin.ContainerTpls.Contains(bound.TemplateId)) return true;

            var compound = bound as CompoundItem;
            if (compound == null)
            {
                callback?.Invoke(null);
                return false;
            }

            if (MedpouchUnzipFlourish.Trigger(__instance, compound, callback)) return false;

            // nothing applicable in the pouch right now (no matching status, or no matching item)
            Plugin.LogSource?.LogInfo($"[Medpouch] hotkey {quickSlot}: no applicable med in pouch");
            return false;
        }
    }
}
