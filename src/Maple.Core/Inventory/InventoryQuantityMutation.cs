namespace Maple.Core.Inventory;

/// <summary>Single inventory-slot quantity mutation for version adapters.</summary>
public sealed record InventoryQuantityMutation(
    InventoryType Type,
    short Slot,
    int ItemId,
    short OldQuantity,
    short NewQuantity)
{
    public bool Removed => NewQuantity <= 0;
}
