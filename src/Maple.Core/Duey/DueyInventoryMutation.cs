using Maple.Core.Inventory;

namespace Maple.Core.Duey;

/// <summary>Duey 寄送造成的一個背包格數量變動。</summary>
public sealed record DueyInventoryMutation(
    InventoryType Type,
    short Slot,
    int ItemId,
    short OldQuantity,
    short NewQuantity)
{
    public bool Removed => NewQuantity <= 0;
}
