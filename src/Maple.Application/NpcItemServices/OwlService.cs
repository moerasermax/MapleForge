using Maple.Core.Inventory;
using Maple.Core.NpcItemServices;
using Maple.Core.Shops;
using Maple.Core.World;

namespace Maple.Application.NpcItemServices;

public enum OwlSearchStatus
{
    Success,
    NotInFreeMarket,
    MissingOwlItem,
    InvalidMinervaItem,
}

public sealed record OwlSearchResult(
    OwlSearchStatus Status,
    int ItemId,
    IReadOnlyList<OwlSearchEntry> Entries,
    ShopInventoryMutation? ConsumedItem = null)
{
    public bool Success => Status == OwlSearchStatus.Success;
}

public sealed record OwlWarpDecision(bool CanWarp, int MapId);

public sealed class EmptyOwlSearchCatalog : IOwlSearchCatalog
{
    public IReadOnlyList<OwlSearchEntry> Search(int itemId) => Array.Empty<OwlSearchEntry>();
}

/// <summary>
/// Owl of Minerva search flow. The current default catalog is empty because hired merchants/MTS are not ported yet.
/// </summary>
public sealed class OwlService
{
    public const int CashOwlItemId = 5230000;
    public const int MinervaOwlItemId = 2310000;
    public const int FreeMarketFirstMapId = 910000000;
    public const int FreeMarketLastMapId = 910000022;
    public const int FreeMarketWarpFirstMapId = 910000001;

    private readonly IOwlSearchCatalog _catalog;

    public OwlService(IOwlSearchCatalog catalog)
    {
        _catalog = catalog;
    }

    public bool CanOpenOwl(Player player)
        => HasOwlItem(player) && IsFreeMarketMap(player.Character.MapId);

    public OwlSearchStatus GetOpenFailure(Player player)
        => !HasOwlItem(player) ? OwlSearchStatus.MissingOwlItem : OwlSearchStatus.NotInFreeMarket;

    public OwlSearchResult Search(Player player, int itemId)
    {
        if (!IsFreeMarketMap(player.Character.MapId))
        {
            return new OwlSearchResult(OwlSearchStatus.NotInFreeMarket, itemId, Array.Empty<OwlSearchEntry>());
        }

        if (!HasOwlItem(player))
        {
            return new OwlSearchResult(OwlSearchStatus.MissingOwlItem, itemId, Array.Empty<OwlSearchEntry>());
        }

        return new OwlSearchResult(OwlSearchStatus.Success, itemId, _catalog.Search(itemId));
    }

    public OwlSearchResult UseMinerva(Player player, short slot, int itemId, int searchItemId)
    {
        if (itemId != MinervaOwlItemId)
        {
            return new OwlSearchResult(OwlSearchStatus.InvalidMinervaItem, searchItemId, Array.Empty<OwlSearchEntry>());
        }

        var item = player.Inventory.By(InventoryType.Use).Get(slot);
        if (item is null || item.ItemId != itemId || item.Quantity <= 0)
        {
            return new OwlSearchResult(OwlSearchStatus.InvalidMinervaItem, searchItemId, Array.Empty<OwlSearchEntry>());
        }

        var entries = _catalog.Search(searchItemId);
        ShopInventoryMutation? consumed = null;
        if (entries.Count > 0 && player.TryTakeItemFromSlot(InventoryType.Use, slot, itemId, 1, out var mutation))
        {
            consumed = mutation;
            player.FlushInventory();
        }

        return new OwlSearchResult(OwlSearchStatus.Success, searchItemId, entries, consumed);
    }

    public OwlWarpDecision DecideWarp(Player player, int mapId)
    {
        var canWarp = IsFreeMarketMap(player.Character.MapId)
            && mapId is >= FreeMarketWarpFirstMapId and <= FreeMarketLastMapId;
        return new OwlWarpDecision(canWarp, mapId);
    }

    public static bool IsFreeMarketMap(int mapId)
        => mapId is >= FreeMarketFirstMapId and <= FreeMarketLastMapId;

    private static bool HasOwlItem(Player player)
        => player.HasItem(InventoryType.Cash, CashOwlItemId)
            || player.HasItem(InventoryType.Use, MinervaOwlItemId);
}
