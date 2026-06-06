using Maple.Core.Inventory;

namespace Maple.Core.Storage;

/// <summary>
/// 帳號倉庫富領域模型。倉庫 slot 與背包 slot 不同：client 對取出操作送的是「該類型清單中的 0-based slot」。
/// </summary>
public sealed class StorageBox
{
    public const byte DefaultSlots = 4;

    private readonly List<Item> _items = new();

    private StorageBox(byte slots, int meso)
    {
        Slots = slots <= 0 ? DefaultSlots : slots;
        Meso = Math.Max(0, meso);
    }

    public byte Slots { get; private set; }
    public int Meso { get; private set; }
    public IReadOnlyList<Item> Items => _items;
    public bool IsFull => _items.Count >= Slots;

    public static StorageBox Hydrate(AccountStorage? snapshot)
    {
        snapshot ??= new AccountStorage();
        var storage = new StorageBox(snapshot.Slots, snapshot.Meso);

        foreach (var record in snapshot.Items.OrderBy(i => i.Slot))
        {
            if (!InventoryTypes.IsValid(record.Type)) continue;
            storage._items.Add(record.ToItem());
        }

        storage.Reindex();
        return storage;
    }

    public AccountStorage Flush()
    {
        var snapshot = new AccountStorage { Slots = Slots, Meso = Meso };
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var record = ItemRecord.From(InventoryTypeOf(item), item);
            record.Slot = (short)i;
            snapshot.Items.Add(record);
        }

        return snapshot;
    }

    public IReadOnlyList<Item> ItemsByType(InventoryType type)
    {
        var items = new List<Item>();
        foreach (var item in _items)
        {
            if (InventoryTypeOf(item) == type)
                items.Add(item);
        }

        return items;
    }

    public bool TryStore(Item item)
    {
        if (IsFull) return false;

        var copy = item.Copy();
        copy.Slot = (short)_items.Count;
        _items.Add(copy);
        return true;
    }

    public bool TryTakeOut(InventoryType type, byte typeSlot, out Item? item)
    {
        item = null;
        var index = IndexOfTypeSlot(type, typeSlot);
        if (index < 0) return false;

        item = _items[index].Copy();
        _items.RemoveAt(index);
        Reindex();
        return true;
    }

    public void SortByInventoryType()
    {
        _items.Sort((a, b) => InventoryTypeOf(a).CompareTo(InventoryTypeOf(b)));
        Reindex();
    }

    public void Arrange()
    {
        _items.Sort((a, b) => a.ItemId.CompareTo(b.ItemId));
        Reindex();
    }

    public bool TryDepositMeso(int amount)
    {
        if (amount <= 0) return false;
        if (Meso > int.MaxValue - amount) return false;
        Meso += amount;
        return true;
    }

    public bool TryWithdrawMeso(int amount)
    {
        if (amount <= 0 || Meso < amount) return false;
        Meso -= amount;
        return true;
    }

    public static InventoryType InventoryTypeOf(Item item)
    {
        if (item.IsEquip) return InventoryType.Equip;

        var raw = item.ItemId / 1_000_000;
        return raw is >= 1 and <= 5 ? (InventoryType)raw : InventoryType.Etc;
    }

    private int IndexOfTypeSlot(InventoryType type, byte typeSlot)
    {
        var seen = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            if (InventoryTypeOf(_items[i]) != type) continue;
            if (seen == typeSlot) return i;
            seen++;
        }

        return -1;
    }

    private void Reindex()
    {
        for (var i = 0; i < _items.Count; i++)
            _items[i].Slot = (short)i;
    }
}
