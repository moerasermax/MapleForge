namespace Maple.Core.Inventory;

/// <summary>
/// 道具的**持久扁平快照**（單一具型、無行為、無繼承）：LiteDB 友善，嵌入 <see cref="Characters.Character"/>，整份文件原子寫。
/// 執行期由 <see cref="Inventories.Hydrate"/> 還原成富 <see cref="Item"/>/<see cref="Equip"/>；checkpoint 時 <see cref="Inventories.Flush"/> 折回。
/// （見設計 doc 決策二：持久快照 ≠ 執行期富領域。）
/// </summary>
public sealed class ItemRecord
{
    public byte Type { get; set; }          // InventoryType 1–5
    public bool IsEquip { get; set; }
    public int ItemId { get; set; }
    public short Slot { get; set; }
    public short Quantity { get; set; } = 1;
    public string Owner { get; set; } = string.Empty;
    public long Expiration { get; set; } = -1;
    public short Flag { get; set; }
    public long UniqueId { get; set; }

    // equip 專屬（IsEquip=true 時有效）
    public byte UpgradeSlots { get; set; }
    public byte ViciousHammer { get; set; }
    public byte Level { get; set; }
    public byte ItemLevel { get; set; }
    public int ItemExp { get; set; }
    public short Str { get; set; }
    public short Dex { get; set; }
    public short Int { get; set; }
    public short Luk { get; set; }
    public short Hp { get; set; }
    public short Mp { get; set; }
    public short Watk { get; set; }
    public short Matk { get; set; }
    public short Wdef { get; set; }
    public short Mdef { get; set; }
    public short Acc { get; set; }
    public short Avoid { get; set; }
    public short Hands { get; set; }
    public short Speed { get; set; }
    public short Jump { get; set; }

    public Item ToItem()
    {
        if (!IsEquip)
            return new Item { ItemId = ItemId, Slot = Slot, Quantity = Quantity, Owner = Owner, Expiration = Expiration, Flag = Flag, UniqueId = UniqueId };

        return new Equip
        {
            ItemId = ItemId, Slot = Slot, Quantity = 1, Owner = Owner, Expiration = Expiration, Flag = Flag, UniqueId = UniqueId,
            UpgradeSlots = UpgradeSlots, ViciousHammer = ViciousHammer, Level = Level, ItemLevel = ItemLevel, ItemExp = ItemExp,
            Str = Str, Dex = Dex, Int = Int, Luk = Luk, Hp = Hp, Mp = Mp, Watk = Watk, Matk = Matk,
            Wdef = Wdef, Mdef = Mdef, Acc = Acc, Avoid = Avoid, Hands = Hands, Speed = Speed, Jump = Jump,
        };
    }

    public static ItemRecord From(InventoryType type, Item it)
    {
        var r = new ItemRecord
        {
            Type = (byte)type, IsEquip = it.IsEquip, ItemId = it.ItemId, Slot = it.Slot,
            Quantity = it.Quantity, Owner = it.Owner, Expiration = it.Expiration, Flag = it.Flag, UniqueId = it.UniqueId,
        };
        if (it is Equip e)
        {
            r.UpgradeSlots = e.UpgradeSlots; r.ViciousHammer = e.ViciousHammer; r.Level = e.Level; r.ItemLevel = e.ItemLevel; r.ItemExp = e.ItemExp;
            r.Str = e.Str; r.Dex = e.Dex; r.Int = e.Int; r.Luk = e.Luk; r.Hp = e.Hp; r.Mp = e.Mp;
            r.Watk = e.Watk; r.Matk = e.Matk; r.Wdef = e.Wdef; r.Mdef = e.Mdef; r.Acc = e.Acc; r.Avoid = e.Avoid;
            r.Hands = e.Hands; r.Speed = e.Speed; r.Jump = e.Jump;
        }
        return r;
    }
}
