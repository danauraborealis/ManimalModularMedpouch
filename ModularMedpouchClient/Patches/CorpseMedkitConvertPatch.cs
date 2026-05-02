using System.Reflection;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // fallback for corpse creation paths where Player.OnDead didnt catch the inventory.
    // only bot-only med clone IDs are touched.
    internal class CorpseMedkitConvertPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(Corpse),
                "method_17",
                new[]
                {
                    typeof(string),
                    typeof(InventoryEquipment),
                    typeof(GClass2197),
                    typeof(bool),
                    typeof(GameWorld),
                    typeof(EPlayerSide),
                    typeof(UnityEngine.Vector3),
                    typeof(UnityEngine.Transform),
                    typeof(bool),
                    typeof(BindableStateClass<Item>),
                    typeof(bool),
                    typeof(GClass768),
                    typeof(MongoID)
                });
        }

        [PatchPrefix]
        static void Prefix(InventoryEquipment equipment)
        {
            BotMedkitDeathConverter.Convert(equipment);
        }
    }
}
