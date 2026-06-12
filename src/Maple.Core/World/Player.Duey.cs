using Maple.Core.Duey;
using Maple.Core.Inventory;

namespace Maple.Core.World;

public sealed partial class Player
{
    public bool CanReceiveDueyItem(InventoryType type) => Inventory.By(type).FirstFreeSlot() is not null;

    public bool TryTakeDueyItem(
        InventoryType type,
        short slot,
        short quantity,
        out Item? item,
        out DueyInventoryMutation? mutation)
    {
        item = null;
        mutation = null;
        if (slot <= 0 || quantity <= 0)
        {
            return false;
        }

        var bag = Inventory.By(type);
        var existing = bag.Get(slot);
        if (existing is null)
        {
            return false;
        }

        var oldQuantity = existing.IsEquip ? (short)1 : existing.Quantity;
        if (quantity > oldQuantity)
        {
            return false;
        }

        if (!bag.TryTake(slot, existing.IsEquip ? (short)1 : quantity, out item) || item is null)
        {
            return false;
        }

        var newQuantity = (short)(oldQuantity - item.Quantity);
        mutation = new DueyInventoryMutation(type, slot, item.ItemId, oldQuantity, newQuantity);
        FlushInventory();
        return true;
    }

    public bool TryTakeDueyItemById(
        InventoryType type,
        int itemId,
        short quantity,
        out DueyInventoryMutation? mutation)
    {
        mutation = null;
        if (quantity <= 0)
        {
            return false;
        }

        var bag = Inventory.By(type);
        var candidate = bag.Items
            .OrderBy(static item => item.Slot)
            .FirstOrDefault(item => item.ItemId == itemId && item.Quantity >= quantity);

        if (candidate is null)
        {
            return false;
        }

        return TryTakeDueyItem(type, candidate.Slot, quantity, out _, out mutation);
    }

    public Item? TryReceiveDueyItem(InventoryType type, Item item)
    {
        var gained = Inventory.By(type).Gain(item);
        if (gained is not null)
        {
            FlushInventory();
        }

        return gained;
    }

    public void RestoreDueyItem(InventoryType type, short originalSlot, Item item)
    {
        var bag = Inventory.By(type);
        var existing = bag.Get(originalSlot);
        if (existing is not null && !existing.IsEquip && !item.IsEquip && existing.ItemId == item.ItemId)
        {
            existing.Quantity = (short)(existing.Quantity + item.Quantity);
            FlushInventory();
            return;
        }

        item.Slot = originalSlot;
        bag.Put(item);
        FlushInventory();
    }
}
