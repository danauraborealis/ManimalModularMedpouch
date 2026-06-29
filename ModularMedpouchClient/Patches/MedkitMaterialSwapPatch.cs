using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Manimal.ModularMedpouch.Patches
{
    // recolors the medkit "use" prefab (the case that animates open) per custom AI-2 variant
    // without touching the bundle. Super + MEGA AI-2 both reuse the vanilla item_medkit_container
    // UsePrefab, which ships with the orange "item_medkit" material. at runtime we swap that
    // material for the blue/green ones that already live in the variants in-hand bundles
    // (ai2_blue.bundle -> item_medkit_blue, ai2_green.bundle -> item_medkit_green).
    //
    // EFT POOLS the container object across every AI-2 use, so we cant just swap-and-forget:
    // a pooled blue container would leak onto the next vanilla AI-2. instead we set the material
    // on EVERY spawn based on the item tpl -- blue/green for the customs, orange for everything
    // else -- so the pooled object always matches whoever is using it right now.
    internal class MedkitMaterialSwapPatch : ModulePatch
    {
        // tpls from ServerModFiles/db/CustomItems
        private const string SuperTpl = "7d1a9c5e8f2b4d3a7c0e1fa2"; // blue
        private const string MegaTpl  = "69eff94e0842b5b5bbd1fd4d"; // green

        private const string OrangeMat = "item_medkit";       // vanilla
        private const string BlueMat   = "item_medkit_blue";  // super
        private const string GreenMat  = "item_medkit_green"; // mega

        // cache resolved Material assets by name so we only scan once each.
        private static readonly Dictionary<string, Material> _matCache = new Dictionary<string, Material>();

        // _controllerObject holds the spawned UsePrefab GameObject; private so reflect it.
        private static readonly FieldInfo _controllerObjectField =
            AccessTools.Field(typeof(Player.MedsController), "_controllerObject");

        protected override MethodBase GetTargetMethod()
        {
            // smethod_8<T> is generic over the controller type; patch the closed form for the
            // local-player MedsController, which is where the use-prefab gets wired up.
            var open = AccessTools.Method(typeof(Player.MedsController), "smethod_8");
            return open?.MakeGenericMethod(typeof(Player.MedsController));
        }

        [PatchPostfix]
        static void Postfix(Player.MedsController controller, Player player, Item item)
        {
            try
            {
                if (controller == null || item == null) return;
                if (player != null && !player.IsYourPlayer) return;

                var go = _controllerObjectField?.GetValue(controller) as GameObject;
                if (go == null) return;

                string tpl = item.TemplateId;
                string targetName =
                    tpl == SuperTpl ? BlueMat :
                    tpl == MegaTpl  ? GreenMat :
                    OrangeMat;

                // only the custom variants need a lookup+swap. for vanilla meds we still want
                // to make sure the (possibly pooled) object is back on orange, but skip the
                // work entirely if its already orange to avoid churn on every single med use.
                var target = FindMaterial(targetName);
                if (target == null)
                {
                    if (targetName != OrangeMat)
                        Plugin.LogSource?.LogWarning($"[Medpouch] material {targetName} not loaded; leaving container default for tpl={tpl}");
                    return;
                }

                int swapped = 0;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        // match the medkit albedo material (and its blue/green swaps) by name
                        // prefix so we re-skin correctly no matter which variant the pool last held.
                        if (m != null && m.name.StartsWith(OrangeMat) && m != target)
                        {
                            mats[i] = target;
                            changed = true;
                            swapped++;
                        }
                    }
                    if (changed) r.sharedMaterials = mats;
                }

                if (swapped > 0)
                    Plugin.LogSource?.LogInfo($"[Medpouch] skinned use-prefab -> {targetName} ({swapped} slots) tpl={tpl}");
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource?.LogWarning($"[Medpouch] material swap threw: {ex.Message}");
            }
        }

        // resolve a Material asset by exact name from everything currently loaded (includes
        // bundle assets not attached to an active renderer). cached; re-scans if a cached
        // material got unloaded (Unity null).
        static Material FindMaterial(string name)
        {
            if (_matCache.TryGetValue(name, out var cached) && cached != null)
                return cached;

            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (mat != null && mat.name == name)
                {
                    _matCache[name] = mat;
                    return mat;
                }
            }
            return null;
        }
    }
}
