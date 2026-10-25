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
    string? PopupMessage = null)
{
    public static StorageResult None { get; } = new(StorageResultKind.None);
}

/// <summary>帳號倉庫用例；不持 session/封包，結果由 adapter 編碼。</summary>
public sealed class StorageService
{
    public StorageResult Open(Player player)
    {
        player.Storage.SortByInventoryType();
        player.FlushStorage();
        return new StorageResult(StorageResultKind.Opened);
    }

    public StorageResult Store(Player player, InventoryType type, short inventorySlot, short quantity, int expectedItemId = 0)
    {
        if (player.Storage.IsFull)
            return new StorageResult(StorageResultKind.Full);

        return player.TryStoreItemToStorage(type, inventorySlot, quantity, expectedItemId)
            ? new StorageResult(StorageResultKind.Stored, type)
            : StorageResult.None;
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
