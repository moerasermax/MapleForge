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
    {
        if (Storage.IsFull) return false;

        var bag = Inventory.By(type);
        if (expectedItemId > 0 && bag.Get(inventorySlot)?.ItemId != expectedItemId)
            return false;

        if (!bag.TryTake(inventorySlot, quantity, out var item) || item is null)
            return false;

        if (!Storage.TryStore(item))
        {
            RestoreTakenItem(bag, inventorySlot, item);
            return false;
        }

        FlushInventory();
        FlushStorage();
        return true;
    }

    public bool TryTakeItemFromStorage(InventoryType type, byte storageTypeSlot)
    {
        var bag = Inventory.By(type);
        if (bag.FirstFreeSlot() is null) return false;
        if (!Storage.TryTakeOut(type, storageTypeSlot, out var item) || item is null)
            return false;

        if (bag.Gain(item) is null)
        {
            Storage.TryStore(item);
            return false;
        }

        FlushInventory();
        FlushStorage();
        return true;
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
