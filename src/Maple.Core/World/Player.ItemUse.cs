using Maple.Core.Inventory;

namespace Maple.Core.World;

public sealed partial class Player
{
    /// <summary>
    /// Consumes quantity from a bag slot after validating the expected item id.
    /// The caller decides when to flush/persist the inventory snapshot.
    /// </summary>
    public bool TryConsumeInventoryItem(
        InventoryType type,
        short slot,
        int itemId,
        short quantity,
        out InventoryQuantityMutation? mutation)
    {
        mutation = null;
        if (slot <= 0 || quantity <= 0)
        {
            return false;
        }

        var bag = Inventory.By(type);
        var current = bag.Get(slot);
        if (current is null || current.ItemId != itemId || current.Quantity < quantity)
        {
            return false;
        }

        var oldQuantity = current.Quantity;
        if (!bag.TryTake(slot, quantity, out _))
        {
            return false;
        }

        mutation = new InventoryQuantityMutation(
            type,
            slot,
            itemId,
            oldQuantity,
            (short)(oldQuantity - quantity));
        return true;
    }
}
