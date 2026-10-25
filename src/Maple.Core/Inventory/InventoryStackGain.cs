namespace Maple.Core.Inventory;

/// <summary>
/// P181：疊加取得的結果：補到既有堆疊的格（呼叫端送數量更新）、新放入的道具（送新增），與背包滿而沒放進去的剩餘數量。
/// </summary>
public sealed record InventoryStackGain(
    IReadOnlyList<InventoryQuantityMutation> Merged,
    IReadOnlyList<Item> Added,
    short Remaining)
{
    public bool Complete => Remaining == 0;

    public bool AnyChange => Merged.Count > 0 || Added.Count > 0;
}
