namespace Maple.Core.Inventory;

/// <summary>
/// 背包分類（協定一致：客戶端 ITEM_MOVE 送的 type byte 即此值 1–5）。
/// 已穿戴裝備走負 slot、暫存於 <c>Character.Equips</c>（驅動 char look），MVP-0 不納入此枚舉；
/// equip/unequip 統一收攏留 MVP-1（見 docs/design/背包道具-領域分層設計.md）。
/// </summary>
public enum InventoryType : byte
{
    Equip = 1,
    Use = 2,
    Setup = 3,
    Etc = 4,
    Cash = 5,
}

public static class InventoryTypes
{
    /// <summary>各背包預設格數上限（對照 SetField AddInventoryInfo 宣告值）。</summary>
    public static byte DefaultSlotLimit(this InventoryType type) => type == InventoryType.Cash ? (byte)48 : (byte)24;

    public static bool IsValid(byte raw) => raw is >= 1 and <= 5;
}
