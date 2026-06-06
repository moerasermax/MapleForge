using Maple.Core.Inventory;

namespace Maple.Core.Storage;

/// <summary>
/// 帳號倉庫的持久快照，嵌入 <see cref="Accounts.Account"/> 文件。
/// 道具以清單順序表示倉庫內部順序；<see cref="ItemRecord.Slot"/> 在 flush 時寫入 0-based 順序供 roundtrip。
/// </summary>
public sealed class AccountStorage
{
    public byte Slots { get; set; } = StorageBox.DefaultSlots;
    public int Meso { get; set; }
    public List<ItemRecord> Items { get; set; } = new();
}
