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
