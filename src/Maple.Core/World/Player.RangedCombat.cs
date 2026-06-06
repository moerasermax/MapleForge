using Maple.Core.Inventory;

namespace Maple.Core.World;

public sealed record RangedProjectile(int ItemId, int VisualItemId);

public sealed partial class Player
{
    public bool TryResolveRangedProjectile(short useSlot, short cashSlot, out RangedProjectile projectile)
    {
        projectile = new RangedProjectile(0, 0);
        var useItem = Inventory.By(InventoryType.Use).Get(useSlot);
        if (useItem is null)
        {
            return false;
        }

        var visualItemId = useItem.ItemId;
        if (cashSlot > 0)
        {
            var cashItem = Inventory.By(InventoryType.Cash).Get(cashSlot);
            if (cashItem is null)
            {
                return false;
            }

            visualItemId = cashItem.ItemId;
        }

        projectile = new RangedProjectile(useItem.ItemId, visualItemId);
        return true;
    }

    public bool TryConsumeUseItemById(
        int itemId,
        int quantity,
        out IReadOnlyList<InventoryQuantityMutation> mutations)
    {
        mutations = Array.Empty<InventoryQuantityMutation>();
        if (itemId <= 0 || quantity <= 0)
        {
            return false;
        }

        var bag = Inventory.By(InventoryType.Use);
        if (bag.CountById(itemId) < quantity)
        {
            return false;
        }

        var remaining = quantity;
        var changed = new List<InventoryQuantityMutation>();
        foreach (var item in bag.Items.Where(i => i.ItemId == itemId).OrderBy(i => i.Slot).ToList())
        {
            if (remaining <= 0)
            {
                break;
            }

            var oldQuantity = item.Quantity;
            var consumed = Math.Min(remaining, oldQuantity);
            var newQuantity = (short)(oldQuantity - consumed);
            if (newQuantity <= 0)
            {
                bag.TryTake(item.Slot, out _);
            }
            else
            {
                item.Quantity = newQuantity;
            }

            changed.Add(new InventoryQuantityMutation(
                InventoryType.Use,
                item.Slot,
                itemId,
                oldQuantity,
                newQuantity));
            remaining -= consumed;
        }

        FlushInventory();
        mutations = changed;
        return true;
    }
}

