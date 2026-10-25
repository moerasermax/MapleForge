using Maple.Core.Inventory;

namespace Maple.Core.World;

public sealed partial class Player
{
    /// <summary>怪物死亡 EXP 入口。升級由 stats 系統後續接管；此處只安全累加目前 EXP。</summary>
    public int GainExp(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var before = Character.Exp;
        var after = Math.Min(int.MaxValue, (long)Character.Exp + amount);
        Character.Exp = (int)after;
        return Character.Exp - before;
    }

    /// <summary>
    /// P181：Java <c>addFromDrop</c>：合併到同擁有者、同期限的堆疊，其餘開新格（複製掉落物屬性）。
    /// 有任何變動就 flush；背包滿時回報剩餘數量（呼叫端把掉落物數量改成剩餘、留在地上）。
    /// </summary>
    public InventoryStackGain GainDropItemStacking(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var copy = item.Copy();
        copy.Slot = 0;
        var result = Inventory.By(InventoryTypeOf(copy.ItemId)).GainStacking(
            copy,
            GetSlotMax(copy.ItemId),
            existing => existing.Owner == copy.Owner && existing.Expiration == copy.Expiration);
        if (result.AnyChange)
        {
            FlushInventory();
        }

        return result;
    }

    public Item? GainDropItem(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var copy = item.Copy();
        copy.Slot = 0;
        var type = InventoryTypeOf(copy.ItemId);
        var gained = Inventory.By(type).Gain(copy);
        if (gained is not null)
        {
            FlushInventory();
        }

        return gained;
    }
}
