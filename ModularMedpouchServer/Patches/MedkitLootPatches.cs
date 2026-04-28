using HarmonyLib;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ModularMedpouchMod.Patches;

// three Harmony postfixes that catch the three places SPT spawns a single medkit
// item and inject our randomized children into it. all three delegate to the same
// MedkitLootFiller helper.

// bot inventory generation
// vanilla: BotLootGenerator.AddRequiredChildItemsToParent runs after a bot picks an
// item from its loot pool. for items with required children (mags need ammo, rifles
// need mags) it adds them to the same item-list. converted medkits have no required
// children so vanilla appends nothing. our postfix adds the medkit fillings.
[HarmonyPatch(typeof(BotLootGenerator), "AddRequiredChildItemsToParent")]
internal static class BotMedkitFillPatch
{
    [HarmonyPostfix]
    static void Postfix(TemplateItem itemToAddTemplate, List<Item> itemToAddChildrenTo, bool isPmc, string botRole)
    {
        if (!MedkitLootFiller.ShouldFill(itemToAddTemplate.Id)) return;

        // the parent is the root of itemToAddChildrenTo (no ParentId set, or matches the
        // medkit ID). use the first item in the list as the parent reference.
        if (itemToAddChildrenTo == null || itemToAddChildrenTo.Count == 0) return;
        var parent = itemToAddChildrenTo[0];

        var category = MedkitLootFiller.CategoryForBotRole(botRole);
        var children = MedkitLootFiller.BuildChildren(itemToAddTemplate.Id, parent.Id, category);
        if (children.Count == 0) return;
        itemToAddChildrenTo.AddRange(children);
    }
}

// static loot containers (medbags, drawers, jackets, etc)
// vanilla: LocationLootGenerator.CreateStaticLootItem returns a ContainerItem whose
// Items collection is the parent + any default children. we append our fillings.
[HarmonyPatch(typeof(LocationLootGenerator), "CreateStaticLootItem")]
internal static class StaticLootMedkitFillPatch
{
    [HarmonyPostfix]
    static void Postfix(ref ContainerItem __result)
    {
        InjectIfMedkit(__result);
    }

    internal static void InjectIfMedkit(ContainerItem result)
    {
        if (result?.Items == null) return;
        var items = result.Items as List<Item> ?? result.Items.ToList();
        if (items.Count == 0) return;

        // root parent is the item with no/empty parent ref, or the first item in the list.
        var parent = items.FirstOrDefault(i => string.IsNullOrEmpty(i.ParentId)) ?? items[0];
        if (!MedkitLootFiller.ShouldFill(parent.Template)) return;

        var children = MedkitLootFiller.BuildChildren(parent.Template, parent.Id, MedkitLootFiller.Category.Loot);
        if (children.Count == 0) return;
        items.AddRange(children);
        result.Items = items;
    }
}

// loose / dynamic world loot
// vanilla: LocationLootGenerator.CreateDynamicLootItem returns a ContainerItem for a
// loose-spawned item (medkit on a table, on the floor, etc).
[HarmonyPatch(typeof(LocationLootGenerator), "CreateDynamicLootItem")]
internal static class DynamicLootMedkitFillPatch
{
    [HarmonyPostfix]
    static void Postfix(ref ContainerItem __result)
    {
        StaticLootMedkitFillPatch.InjectIfMedkit(__result);
    }
}
