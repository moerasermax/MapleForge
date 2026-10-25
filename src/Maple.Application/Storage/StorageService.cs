using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Storage;

public enum StorageResultKind
{
    None,
    Opened,
    Full,
    Stored,
    TakenOut,
    MesoChanged,
    Arranged,
    Closed,
}

public sealed record StorageResult(
    StorageResultKind Kind,
    InventoryType? ChangedType = null,
    InventoryStackGain? Gained = null,
    bool MesoChanged = false,
    string? PopupMessage = null,
    InventoryQuantityMutation? Removed = null,
    bool EnableActions = false)
{
    public static StorageResult None { get; } = new(StorageResultKind.None);
}

/// <summary>帳號倉庫用例；不持 session/封包，結果由 adapter 編碼。</summary>
public sealed class StorageService
{
    private readonly Maple.Core.Items.IItemMakeCatalog? _items;

    /// <summary>P212：道具目錄供 isDropRestricted（不可交易）判斷；未提供時不檢查。</summary>
    public StorageService(Maple.Core.Items.IItemMakeCatalog? items = null) => _items = items;

    public StorageResult Open(Player player)
    {
        player.Storage.SortByInventoryType();
        player.FlushStorage();
        return new StorageResult(StorageResultKind.Opened);
    }

    /// <summary>Java：存入手續費一般 100、自由市場 500。</summary>
    public const int StoreFee = 100;
    public const int FreeMarketStoreFee = 500;

    /// <summary>P211：Java handleStorage mode 5（手續費、寵物／ID／數量檢查、removeFromSlot 背包封包）。</summary>
    public StorageResult Store(Player player, InventoryType type, short inventorySlot, short quantity, int expectedItemId = 0)
    {
        var fee = player.Character.MapId == FreeMarketMapId ? FreeMarketStoreFee : StoreFee;
        Func<int, bool>? restricted = _items is null ? null : _items.IsDropRestricted;
        Func<int, bool>? unique = _items is null ? null : _items.IsPickupRestricted;
        return player.TryStoreItemToStorage(type, inventorySlot, quantity, expectedItemId, fee, restricted, unique, out var removed) switch
        {
            StorageStoreStatus.Success => new StorageResult(StorageResultKind.Stored, type, MesoChanged: true, Removed: removed),
            StorageStoreStatus.Full => new StorageResult(StorageResultKind.Full),
            StorageStoreStatus.NotEnoughMeso => new StorageResult(StorageResultKind.None, PopupMessage: NotEnoughMesoMessage),
            StorageStoreStatus.NotFound or StorageStoreStatus.Rejected when quantity >= 1 => new StorageResult(StorageResultKind.None, EnableActions: true),
            _ => StorageResult.None,
        };
    }

    /// <summary>Java：自由市場入口（910000000）取出收 1000 楓幣。</summary>
    public const int FreeMarketMapId = 910000000;
    public const int FreeMarketTakeOutFee = 1000;
    public const string NoSpaceMessage = "你的物品攔已經滿了..";
    public const string NotEnoughMesoMessage = "你沒有足夠的楓幣放倉庫道具。";

    /// <summary>P210：Java handleStorage mode 4（checkSpace、自由市場手續費、addFromDrop 疊加；失敗時 dropMessage(1)）。</summary>
    public StorageResult TakeOut(Player player, InventoryType type, byte storageTypeSlot)
    {
        var fee = player.Character.MapId == FreeMarketMapId ? FreeMarketTakeOutFee : 0;
        return player.TryTakeItemFromStorage(type, storageTypeSlot, fee, out var gain) switch
        {
            StorageTakeOutStatus.Success => new StorageResult(StorageResultKind.TakenOut, type, gain, MesoChanged: fee > 0),
            StorageTakeOutStatus.NoSpace => new StorageResult(StorageResultKind.None, PopupMessage: NoSpaceMessage),
            StorageTakeOutStatus.NotEnoughMeso => new StorageResult(StorageResultKind.None, PopupMessage: NotEnoughMesoMessage),
            _ => new StorageResult(StorageResultKind.None),
        };
    }

    public StorageResult MoveMeso(Player player, int clientMesoDelta) =>
        player.TryApplyStorageMesoClientDelta(clientMesoDelta)
            ? new StorageResult(StorageResultKind.MesoChanged)
            : StorageResult.None;

    public StorageResult Arrange(Player player)
    {
        player.Storage.Arrange();
        player.FlushStorage();
        return new StorageResult(StorageResultKind.Arranged);
    }

    public StorageResult Close(Player player)
    {
        player.FlushStorage();
        return new StorageResult(StorageResultKind.Closed);
    }
}
