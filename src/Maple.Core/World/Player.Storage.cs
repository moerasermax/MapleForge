using Maple.Core.Accounts;
using Maple.Core.Inventory;
using Maple.Core.Storage;

namespace Maple.Core.World;

public sealed partial class Player
{
    private Account? _storageAccount;

    public StorageBox Storage { get; private set; } = StorageBox.Hydrate(new AccountStorage());

    public void AttachStorage(Account account)
    {
        if (Character.AccountId > 0 && account.Id > 0 && account.Id != Character.AccountId)
            throw new InvalidOperationException("Cannot attach storage from a different account.");

        _storageAccount = account;
        Storage = StorageBox.Hydrate(account.Storage);
    }

    public void FlushStorage()
    {
        if (_storageAccount is not null)
            FlushStorage(_storageAccount);
    }

    public void FlushStorage(Account account)
    {
        account.Storage = Storage.Flush();
    }

    public bool TryStoreItemToStorage(InventoryType type, short inventorySlot, short quantity, int expectedItemId = 0)
        => TryStoreItemToStorage(type, inventorySlot, quantity, expectedItemId, fee: 0, out _) == StorageStoreStatus.Success;

    public StorageStoreStatus TryStoreItemToStorage(
        InventoryType type,
        short inventorySlot,
        short quantity,
        int expectedItemId,
        int fee,
        out InventoryQuantityMutation? mutation)
        => TryStoreItemToStorage(type, inventorySlot, quantity, expectedItemId, fee, isDropRestricted: null, out mutation);

    /// <summary>
    /// P211：Java NPCHandler.handleStorage mode 5——數量 &lt; 1 不處理；倉庫滿；格子空；楓幣不足手續費（一般 100、自由市場 500）；
    /// 寵物不可存；飛鏢／子彈整組存入；ID 不符或數量不足拒絕。成功時扣手續費、<c>removeFromSlot</c>（回傳數量異動供呼叫端送封包）後存入。
    /// P212：不可交易（drop-restricted）的道具要有 karma 旗標（KARMA_EQ 優先，再 KARMA_USE），存入的那份清掉該旗標；沒有就拒絕。
    /// </summary>
    public StorageStoreStatus TryStoreItemToStorage(
        InventoryType type,
        short inventorySlot,
        short quantity,
        int expectedItemId,
        int fee,
        Func<int, bool>? isDropRestricted,
        out InventoryQuantityMutation? mutation)
    {
        mutation = null;
        if (quantity < 1) return StorageStoreStatus.Rejected;
        if (Storage.IsFull) return StorageStoreStatus.Full;

        var bag = Inventory.By(type);
        var source = bag.Get(inventorySlot);
        if (source is null) return StorageStoreStatus.NotFound;
        if (fee > 0 && Character.Meso < fee) return StorageStoreStatus.NotEnoughMeso;
        if (source.ItemId / 1000 == 5000 || source.Quantity <= 0) return StorageStoreStatus.Rejected;

        var rechargeable = Maple.Core.Inventory.Inventory.IsRechargeable(source.ItemId);
        if ((expectedItemId > 0 && source.ItemId != expectedItemId) || (!rechargeable && source.Quantity < quantity))
            return StorageStoreStatus.Rejected;

        short? karmaToClear = null;
        if (isDropRestricted?.Invoke(source.ItemId) == true)
        {
            if (ItemFlags.Has(source.Flag, ItemFlags.KarmaEquip))
                karmaToClear = ItemFlags.KarmaEquip;
            else if (ItemFlags.Has(source.Flag, ItemFlags.KarmaUse))
                karmaToClear = ItemFlags.KarmaUse;
            else
                return StorageStoreStatus.Rejected;
        }

        if (rechargeable)
            quantity = source.Quantity;

        var oldQuantity = source.Quantity;
        if (!bag.TryTake(inventorySlot, quantity, out var item) || item is null)
            return StorageStoreStatus.Rejected;

        var originalFlag = item.Flag;
        if (karmaToClear is { } karma)
            item.Flag = ItemFlags.Clear(item.Flag, karma);

        if (!Storage.TryStore(item))
        {
            item.Flag = originalFlag;
            RestoreTakenItem(bag, inventorySlot, item);
            return StorageStoreStatus.Full;
        }

        if (fee > 0)
            GainMeso(-fee);

        mutation = new InventoryQuantityMutation(type, inventorySlot, item.ItemId, oldQuantity, (short)(oldQuantity - quantity));
        FlushInventory();
        FlushStorage();
        return StorageStoreStatus.Success;
    }

    public bool TryTakeItemFromStorage(InventoryType type, byte storageTypeSlot)
        => TryTakeItemFromStorage(type, storageTypeSlot, fee: 0, out _) == StorageTakeOutStatus.Success;

    /// <summary>
    /// P210：Java NPCHandler.handleStorage mode 4——takeOut 後 <c>checkSpace(id, qty, owner)</c> 不過或楓幣不足手續費就
    /// <c>store</c> 放回；否則扣手續費（自由市場 1000）並 <c>addFromDrop</c>（疊加）。
    /// </summary>
    public StorageTakeOutStatus TryTakeItemFromStorage(InventoryType type, byte storageTypeSlot, int fee, out InventoryStackGain? gain)
    {
        gain = null;
        if (!Storage.TryTakeOut(type, storageTypeSlot, out var item) || item is null)
            return StorageTakeOutStatus.NotFound;

        if (!HasSpaceFor(item.ItemId, item.Quantity, item.Owner ?? string.Empty))
        {
            Storage.TryStore(item);
            return StorageTakeOutStatus.NoSpace;
        }

        if (fee > 0 && Character.Meso < fee)
        {
            Storage.TryStore(item);
            return StorageTakeOutStatus.NotEnoughMeso;
        }

        if (item.Quantity < 1)
        {
            Storage.TryStore(item);
            return StorageTakeOutStatus.NotFound;
        }

        if (fee > 0)
            GainMeso(-fee);

        gain = GainDropItemStacking(item);
        FlushInventory();
        FlushStorage();
        return StorageTakeOutStatus.Success;
    }

    public bool TryApplyStorageMesoClientDelta(int meso)
    {
        if (meso == 0) return false;

        if (meso > 0)
            return TryWithdrawStorageMeso(meso);

        var deposit = -(long)meso;
        if (deposit > int.MaxValue) return false;
        return TryDepositStorageMeso((int)deposit);
    }

    public bool TryDepositStorageMeso(int amount)
    {
        if (amount <= 0 || Character.Meso < amount) return false;
        if (!Storage.TryDepositMeso(amount)) return false;

        Character.Meso -= amount;
        FlushStorage();
        return true;
    }

    public bool TryWithdrawStorageMeso(int amount)
    {
        if (amount <= 0) return false;
        if (Character.Meso > int.MaxValue - amount) return false;
        if (!Storage.TryWithdrawMeso(amount)) return false;

        Character.Meso += amount;
        FlushStorage();
        return true;
    }

    private static void RestoreTakenItem(Maple.Core.Inventory.Inventory bag, short originalSlot, Item item)
    {
        var existing = bag.Get(originalSlot);
        if (existing is not null && !existing.IsEquip && !item.IsEquip && existing.ItemId == item.ItemId)
        {
            existing.Quantity = (short)(existing.Quantity + item.Quantity);
            return;
        }

        item.Slot = originalSlot;
        bag.Put(item);
    }
}

/// <summary>P211：倉庫存入結果。</summary>
public enum StorageStoreStatus
{
    Success,
    Full,
    NotFound,
    NotEnoughMeso,
    Rejected,
}

/// <summary>P210：倉庫取出結果。</summary>
public enum StorageTakeOutStatus
{
    Success,
    NotFound,
    NoSpace,
    NotEnoughMeso,
}
