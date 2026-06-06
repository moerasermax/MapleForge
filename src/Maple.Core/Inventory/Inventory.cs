namespace Maple.Core.Inventory;

/// <summary>
/// 單一背包富聚合（＝OdinMS MapleInventory 乾淨版）：slot(1..SlotLimit)→Item，含 Add/Move/Merge 不變式。
/// **零傳輸、零 session**（架構北極星）。slot 正數＝背包格。
/// </summary>
public sealed class Inventory
{
    private readonly Dictionary<short, Item> _slots = new();

    public InventoryType Type { get; }
    public byte SlotLimit { get; set; }

    public Inventory(InventoryType type, byte slotLimit)
    {
        Type = type;
        SlotLimit = slotLimit;
    }

    public IReadOnlyCollection<Item> Items => _slots.Values;

    public Item? Get(short slot) => _slots.GetValueOrDefault(slot);

    public bool Contains(short slot) => _slots.ContainsKey(slot);

    /// <summary>第一個空格（1..SlotLimit），滿了回 null。</summary>
    public short? FirstFreeSlot()
    {
        for (short s = 1; s <= SlotLimit; s++)
            if (!_slots.ContainsKey(s)) return s;
        return null;
    }

    /// <summary>直接放到指定格（hydrate/測試用，覆寫同格）。</summary>
    public void Put(Item item)
    {
        item.Slot = item.Slot <= 0 ? (FirstFreeSlot() ?? item.Slot) : item.Slot;
        _slots[item.Slot] = item;
    }

    /// <summary>移除指定正格並取出道具；供跨聚合原子操作使用。</summary>
    public bool TryTake(short slot, out Item? item)
    {
        item = null;
        if (slot <= 0 || !_slots.TryGetValue(slot, out var found)) return false;

        _slots.Remove(slot);
        item = found;
        return true;
    }

    /// <summary>把道具放進指定正格；目標已占用時不覆寫。</summary>
    public bool TryPut(short slot, Item item)
    {
        if (slot <= 0 || slot > SlotLimit || _slots.ContainsKey(slot)) return false;

        item.Slot = slot;
        _slots[slot] = item;
        return true;
    }

    /// <summary>取得道具到背包：找空格放入，滿了回 null。可堆疊道具的合併留呼叫端（MVP-0 簡化：新格）。</summary>
    public Item? Gain(Item item)
    {
        var slot = FirstFreeSlot();
        if (slot is null) return null;
        item.Slot = slot.Value;
        _slots[slot.Value] = item;
        return item;
    }

    public int CountById(int itemId)
    {
        var n = 0;
        foreach (var it in _slots.Values)
            if (it.ItemId == itemId) n += it.Quantity;
        return n;
    }

    /// <summary>從指定背包格取出指定數量；裝備只能整件取出。</summary>
    public bool TryTake(short slot, short quantity, out Item? item)
    {
        item = null;
        if (slot <= 0 || quantity <= 0) return false;
        if (!_slots.TryGetValue(slot, out var source)) return false;

        if (source.IsEquip)
        {
            if (quantity != 1) return false;
            _slots.Remove(slot);
            item = source;
            return true;
        }

        if (source.Quantity < quantity) return false;

        item = source.Copy();
        item.Quantity = quantity;
        if (source.Quantity == quantity)
            _slots.Remove(slot);
        else
            source.Quantity = (short)(source.Quantity - quantity);

        return true;
    }

    /// <summary>
    /// 格內移動：src→dst。dst 空＝搬移；dst 同 id 可堆疊＝合併（MVP-0 全量併，不處理上限分裂）；否則＝交換。
    /// 回傳是否成功（src 無物或越界＝false）。
    /// </summary>
    public bool Move(short src, short dst)
    {
        if (src <= 0 || dst <= 0 || dst > SlotLimit) return false;
        if (!_slots.TryGetValue(src, out var s)) return false;

        if (_slots.TryGetValue(dst, out var d))
        {
            if (d.ItemId == s.ItemId && !s.IsEquip && !d.IsEquip)
            {
                // 合併（MVP-0：全量併入 dst，清空 src）
                d.Quantity = (short)(d.Quantity + s.Quantity);
                _slots.Remove(src);
            }
            else
            {
                // 交換
                _slots[dst] = s; _slots[src] = d;
                s.Slot = dst; d.Slot = src;
            }
        }
        else
        {
            _slots.Remove(src);
            _slots[dst] = s;
            s.Slot = dst;
        }
        return true;
    }
}

/// <summary>玩家全背包聚合（5 個 type）。執行期掛 <see cref="World.Player"/>，由 ItemRecord hydrate / flush。</summary>
public sealed class Inventories
{
    private readonly Dictionary<InventoryType, Inventory> _bags = new();

    public Inventories()
    {
        foreach (InventoryType t in Enum.GetValues<InventoryType>())
            _bags[t] = new Inventory(t, t.DefaultSlotLimit());
    }

    public Inventory By(InventoryType type) => _bags[type];

    /// <summary>從持久扁平記錄 hydrate（正 slot 才入袋；負 slot=已穿戴由 Character.Equips 另管，MVP-0 略過）。</summary>
    public static Inventories Hydrate(IEnumerable<ItemRecord> records)
    {
        var inv = new Inventories();
        foreach (var r in records)
        {
            if (r.Slot <= 0 || !InventoryTypes.IsValid(r.Type)) continue;
            inv.By((InventoryType)r.Type).Put(r.ToItem());
        }
        return inv;
    }

    /// <summary>flush 回持久扁平記錄（checkpoint/存檔用）。</summary>
    public List<ItemRecord> Flush()
    {
        var list = new List<ItemRecord>();
        foreach (var (type, bag) in _bags)
            foreach (var it in bag.Items)
                list.Add(ItemRecord.From(type, it));
        return list;
    }
}
