using Maple.Core.Characters;
using Maple.Core.Inventory;
using InventoryEquip = Maple.Core.Inventory.Equip;

namespace Maple.Core.World;

public sealed partial class Player
{
    /// <summary>
    /// 穿裝：Equip 背包正格 -> 已穿戴負格。若目標裝備欄已有裝備，交換回來源背包格。
    /// </summary>
    public bool Equip(short srcSlot, short dstEquipSlot)
    {
        if (srcSlot <= 0 || dstEquipSlot >= 0) return false;

        var equipBag = Inventory.By(InventoryType.Equip);
        if (srcSlot > equipBag.SlotLimit) return false;
        if (equipBag.Get(srcSlot) is not InventoryEquip source) return false;

        var target = FindEquipped(dstEquipSlot);
        var targetBackToBag = target is null ? null : ToBagEquip(target, srcSlot);

        if (!equipBag.TryTake(srcSlot, out var taken) || taken is not InventoryEquip takenEquip)
            return false;

        if (target is not null)
            Character.Equips.Remove(target);

        Character.Equips.Add(ToEquippedEntry(takenEquip, dstEquipSlot));

        if (targetBackToBag is not null && !equipBag.TryPut(srcSlot, targetBackToBag))
        {
            Character.Equips.RemoveAll(e => e.Position == dstEquipSlot && e.ItemId == takenEquip.ItemId);
            Character.Equips.Add(target!);
            equipBag.TryPut(srcSlot, takenEquip);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 脫裝：已穿戴負格 -> Equip 背包正格。目標背包格需為空，與 Java MVP 行為一致。
    /// </summary>
    public bool Unequip(short srcEquipSlot, short dstSlot)
    {
        if (srcEquipSlot >= 0 || dstSlot <= 0) return false;

        var equipBag = Inventory.By(InventoryType.Equip);
        if (dstSlot > equipBag.SlotLimit || equipBag.Contains(dstSlot)) return false;

        var source = FindEquipped(srcEquipSlot);
        if (source is null) return false;

        var item = ToBagEquip(source, dstSlot);
        Character.Equips.Remove(source);

        if (!equipBag.TryPut(dstSlot, item))
        {
            Character.Equips.Add(source);
            return false;
        }

        return true;
    }

    private EquipEntry? FindEquipped(short position) => Character.Equips.FirstOrDefault(e => e.Position == position);

    private static EquipEntry ToEquippedEntry(InventoryEquip equip, short position) => new()
    {
        Position = position,
        ItemId = equip.ItemId,
        Owner = equip.Owner,
        Expiration = equip.Expiration,
        Flag = equip.Flag,
        UniqueId = equip.UniqueId,
        UpgradeSlots = equip.UpgradeSlots,
        ViciousHammer = equip.ViciousHammer,
        Level = equip.Level,
        ItemLevel = equip.ItemLevel,
        ItemExp = equip.ItemExp,
        Str = equip.Str,
        Dex = equip.Dex,
        Int = equip.Int,
        Luk = equip.Luk,
        Hp = equip.Hp,
        Mp = equip.Mp,
        Watk = equip.Watk,
        Matk = equip.Matk,
        Wdef = equip.Wdef,
        Mdef = equip.Mdef,
        Acc = equip.Acc,
        Avoid = equip.Avoid,
        Hands = equip.Hands,
        Speed = equip.Speed,
        Jump = equip.Jump,
    };

    private static InventoryEquip ToBagEquip(EquipEntry entry, short slot) => new()
    {
        ItemId = entry.ItemId,
        Slot = slot,
        Quantity = 1,
        Owner = entry.Owner,
        Expiration = entry.Expiration,
        Flag = entry.Flag,
        UniqueId = entry.UniqueId,
        UpgradeSlots = entry.UpgradeSlots,
        ViciousHammer = entry.ViciousHammer,
        Level = entry.Level,
        ItemLevel = entry.ItemLevel,
        ItemExp = entry.ItemExp,
        Str = entry.Str,
        Dex = entry.Dex,
        Int = entry.Int,
        Luk = entry.Luk,
        Hp = entry.Hp,
        Mp = entry.Mp,
        Watk = entry.Watk,
        Matk = entry.Matk,
        Wdef = entry.Wdef,
        Mdef = entry.Mdef,
        Acc = entry.Acc,
        Avoid = entry.Avoid,
        Hands = entry.Hands,
        Speed = entry.Speed,
        Jump = entry.Jump,
    };
}
