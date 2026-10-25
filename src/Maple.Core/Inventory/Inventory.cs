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

    /// <summary>Java <c>GameConstants.isRechargable</c>：飛鏢（207）與子彈（233）不合併、整批放一格。</summary>
    public static bool IsRechargeable(int itemId) => itemId / 10000 is 207 or 233;

    /// <summary>
    /// P181：對照 Java <c>MapleInventoryManipulator.addById／addFromDrop</c> 的疊加：非裝備非飛鏢子彈時，
    /// 先依格序把 <paramref name="canMerge"/> 允許的同 ID 堆疊補到 <paramref name="slotMax"/>，剩下的每 slotMax 開新格
    /// （新格複製 <paramref name="item"/> 的擁有者／期限／旗標）；背包滿時停止並回報剩餘數量（已合併／已放入的保留，同 Java）。
    /// 裝備與飛鏢子彈：整件放一格。
    /// </summary>
    public InventoryStackGain GainStacking(Item item, short slotMax, Func<Item, bool> canMerge)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(canMerge);

        if (item.IsEquip || IsRechargeable(item.ItemId))
        {
            item.Slot = 0;
            return Gain(item) is { } placed
                ? new InventoryStackGain([], [placed], 0)
                : new InventoryStackGain([], [], item.Quantity);
        }

        var max = slotMax > 0 ? slotMax : (short)100;
        var quantity = (int)item.Quantity;
        var merged = new List<InventoryQuantityMutation>();
        foreach (var existing in _slots.Values.Where(i => i.ItemId == item.ItemId).OrderBy(static i => i.Slot).ToArray())
        {
            if (quantity <= 0)
            {
                break;
            }

            var old = existing.Quantity;
            if (old < max && canMerge(existing))
            {
                var next = (short)Math.Min(old + quantity, max);
                quantity -= next - old;
                existing.Quantity = next;
                merged.Add(new InventoryQuantityMutation(Type, existing.Slot, item.ItemId, old, next));
            }
        }

        var added = new List<Item>();
        while (quantity > 0)
        {
            var batch = (short)Math.Min(quantity, max);
            var fresh = item.Copy();
            fresh.Slot = 0;
            fresh.Quantity = batch;
            if (Gain(fresh) is null)
            {
                return new InventoryStackGain(merged, added, (short)quantity);
            }

            quantity -= batch;
            added.Add(fresh);
        }

        return new InventoryStackGain(merged, added, 0);
    }

    /// <summary>
    /// P181：Java <c>checkSpace(c, itemId, quantity, owner)</c>：先扣掉同擁有者堆疊還能補的量，再看剩下需要的格數
    /// （ceil(quantity / slotMax)；飛鏢子彈 1 格）是否放得下。裝備：有空格即可。
    /// </summary>
    public bool HasSpaceFor(int itemId, int quantity, short slotMax, string owner = "")
    {
        if (Type == InventoryType.Equip)
        {
            return FirstFreeSlot() is not null;
        }

        if (quantity <= 0 && !IsRechargeable(itemId))
        {
            return false;
        }

        var max = slotMax > 0 ? slotMax : (short)100;
        if (!IsRechargeable(itemId))
        {
            foreach (var existing in _slots.Values.Where(i => i.ItemId == itemId).OrderBy(static i => i.Slot))
            {
                if (existing.Quantity < max && owner == existing.Owner)
                {
                    quantity -= Math.Min(existing.Quantity + quantity, max) - existing.Quantity;
                }

                if (quantity <= 0)
                {
                    break;
                }
            }
        }

        var needed = IsRechargeable(itemId) ? 1 : (int)Math.Ceiling((double)Math.Max(0, quantity) / max);
        return _slots.Count + needed <= SlotLimit;
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

    /// <summary>依 item id 由小到大重新排到前方格位。對照舊 Java ItemSort/ItemGather 的排序主幹。</summary>
    public bool SortByItemId()
    {
        if (_slots.Count <= 1)
        {
            return false;
        }

        var ordered = _slots.Values
            .Select(static item => item.Copy())
            .OrderBy(static item => item.ItemId)
            .ThenBy(static item => item.Slot)
            .ToArray();

        return Repack(ordered);
    }

    /// <summary>聚集背包空洞；現階段同 Java 實作一樣也依 item id 排序。</summary>
    public bool GatherByItemId() => SortByItemId();

    private bool Repack(IReadOnlyList<Item> ordered)
    {
        var before = _slots
            .OrderBy(static pair => pair.Key)
            .Select(static pair => (Slot: pair.Key, pair.Value.ItemId, pair.Value.Quantity))
            .ToArray();

        _slots.Clear();
        short slot = 1;
        foreach (var item in ordered)
        {
            item.Slot = slot;
            _slots[slot] = item;
            slot++;
        }

        var after = _slots
            .OrderBy(static pair => pair.Key)
            .Select(static pair => (Slot: pair.Key, pair.Value.ItemId, pair.Value.Quantity))
            .ToArray();

        return !before.SequenceEqual(after);
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
