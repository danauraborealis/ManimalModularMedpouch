using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Manimal.ModularMedpouch.Patches
{
    // Player.OnDead creates the corpse from the live equipment a little later.
    // swap bot-only med clones before that so the corpse exposes searchable containers.
    internal class BotMedkitDeathConvertPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.OnDead));
        }

        [PatchPrefix]
        static void Prefix(Player __instance)
        {
            BotMedkitDeathConverter.Convert(__instance);
        }
    }
}
