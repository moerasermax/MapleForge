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

public sealed record StorageResult(StorageResultKind Kind, InventoryType? ChangedType = null)
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

    public StorageResult TakeOut(Player player, InventoryType type, byte storageTypeSlot) =>
        player.TryTakeItemFromStorage(type, storageTypeSlot)
            ? new StorageResult(StorageResultKind.TakenOut, type)
            : StorageResult.None;

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
