using HarmonyLib;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.Trade;

namespace ModularMedpouchMod.Patches;

// fills medkits the player BUYS from a trader, the same way world-spawned medkits get
// filled. a plain medkit in a trader assort has no children, so it normally arrives empty.
//
// the buy flow is TradeHelper.BuyItem -> builds root+children list -> InventoryHelper
// .AddItemsToStash. AddItemsToStash is generic (quest rewards, hideout, flea, etc all use
// it) so we cant just patch it blindly. instead BuyItem sets a thread-scoped flag for the
// duration of a TRADER purchase, and the AddItemsToStash prefix only injects fillings while
// that flag is set. the lock in BuyItem + ThreadStatic keep it safe across sessions.

internal static class TraderPurchaseScope
{
    // thread-scoped so a trader buy on one session never bleeds into an unrelated
    // AddItemsToStash (quest reward, hideout output) on another thread.
    [ThreadStatic] public static bool Active;
    // player loyalty level with the trader being bought from; gates which IDs may roll
    // as random extras (e.g. Super AI-2 only at LL2+).
    [ThreadStatic] public static int LoyaltyLevel;
}

// flag the window where a trader purchase is being fulfilled. excludes flea (ragfair pmc)
// purchases -- those are player listings and should arrive exactly as listed.
[HarmonyPatch(typeof(TradeHelper), nameof(TradeHelper.BuyItem))]
internal static class TraderBuyScopePatch
{
    [HarmonyPrefix]
    static void Prefix(PmcData pmcData, ProcessBuyTradeRequestData buyRequestData)
    {
        TraderPurchaseScope.Active =
            !string.Equals(buyRequestData?.Type, "buy_from_ragfair_pmc", StringComparison.OrdinalIgnoreCase);

        // look up the players loyalty level with this trader; default 1 if unknown.
        int loyalty = 1;
        if (buyRequestData != null
            && pmcData?.TradersInfo != null
            && pmcData.TradersInfo.TryGetValue(buyRequestData.TransactionId, out var info)
            && info?.LoyaltyLevel != null)
        {
            loyalty = info.LoyaltyLevel.Value;
        }
        TraderPurchaseScope.LoyaltyLevel = loyalty;
    }

    // finalizer so the flag clears even if BuyItem throws (out-of-stock, limit hit, etc).
    [HarmonyFinalizer]
    static void Finalizer()
    {
        TraderPurchaseScope.Active = false;
    }
}

// inject medkit fillings into any converted-medkit root being added during a trader buy.
// runs before AddItemsToStash places the items, so the children get parented + placed by
// the same code path that handles everything else (space check, grid filter, id remap).
[HarmonyPatch(typeof(InventoryHelper), nameof(InventoryHelper.AddItemsToStash))]
internal static class TraderPurchaseMedkitFillPatch
{
    [HarmonyPrefix]
    static void Prefix(AddItemsDirectRequest request)
    {
        if (!TraderPurchaseScope.Active) return;
        if (request?.ItemsWithModsToAdd == null) return;

        foreach (var group in request.ItemsWithModsToAdd)
        {
            if (group == null || group.Count == 0) continue;

            var root = group[0];
            if (!MedkitLootFiller.ShouldFillTraderPurchase(root.Template)) continue;

            // dont double-fill if the medkit somehow already carries grid children
            bool alreadyHasChildren = false;
            for (int i = 1; i < group.Count; i++)
            {
                if (group[i].ParentId == root.Id) { alreadyHasChildren = true; break; }
            }
            if (alreadyHasChildren) continue;

            // trader fill: guaranteed AI-2 + random extras, pool filtered by exclusions and
            // loyalty gates (e.g. MEGA never, Super only at LL2+). no substitution.
            var children = MedkitLootFiller.BuildTraderChildren(root.Template, root.Id, TraderPurchaseScope.LoyaltyLevel);
            if (children.Count > 0) group.AddRange(children);
        }
    }
}
