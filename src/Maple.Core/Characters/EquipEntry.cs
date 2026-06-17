using Maple.Core.Inventory;

namespace Maple.Core.Characters;

/// <summary>一件穿戴中的裝備。負 position 為穿戴欄，並保留可被卷軸/維修等流程改動的裝備數值。</summary>
public sealed class EquipEntry
{
    public short Position { get; set; }
    public int ItemId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public long Expiration { get; set; } = -1;
    public short Flag { get; set; }
    public long UniqueId { get; set; }

    public byte UpgradeSlots { get; set; }
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

    public Equip ToEquip() => new()
    {
        ItemId = ItemId,
        Slot = Position,
        Quantity = 1,
        Owner = Owner,
        Expiration = Expiration,
        Flag = Flag,
        UniqueId = UniqueId,
        UpgradeSlots = UpgradeSlots,
        Level = Level,
        ItemLevel = ItemLevel,
        ItemExp = ItemExp,
        Str = Str,
        Dex = Dex,
        Int = Int,
        Luk = Luk,
        Hp = Hp,
        Mp = Mp,
        Watk = Watk,
        Matk = Matk,
        Wdef = Wdef,
        Mdef = Mdef,
        Acc = Acc,
        Avoid = Avoid,
        Hands = Hands,
        Speed = Speed,
        Jump = Jump,
    };

    public void CopyFrom(Equip equip)
    {
        ItemId = equip.ItemId;
        Owner = equip.Owner;
        Expiration = equip.Expiration;
        Flag = equip.Flag;
        UniqueId = equip.UniqueId;
        UpgradeSlots = equip.UpgradeSlots;
        Level = equip.Level;
        ItemLevel = equip.ItemLevel;
        ItemExp = equip.ItemExp;
        Str = equip.Str;
        Dex = equip.Dex;
        Int = equip.Int;
        Luk = equip.Luk;
        Hp = equip.Hp;
        Mp = equip.Mp;
        Watk = equip.Watk;
        Matk = equip.Matk;
        Wdef = equip.Wdef;
        Mdef = equip.Mdef;
        Acc = equip.Acc;
        Avoid = equip.Avoid;
        Hands = equip.Hands;
        Speed = equip.Speed;
        Jump = equip.Jump;
    }
}
