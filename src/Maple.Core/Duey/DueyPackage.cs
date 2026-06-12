using Maple.Core.Inventory;

namespace Maple.Core.Duey;

/// <summary>
/// Duey 宅配包裹文件。這是版本無關的持久化模型；v113 的 opcode 與 byte layout 留在 adapter。
/// </summary>
public sealed class DueyPackage
{
    /// <summary>LiteDB/Mongo sequence id；對應 Java dueypackages.PackageId。</summary>
    public int Id { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public int RecipientCharacterId { get; set; }

    public int Meso { get; set; }

    public ItemRecord? Item { get; set; }

    public string Message { get; set; } = string.Empty;

    public long CreatedAtUnixMillis { get; set; }

    /// <summary>包裹到期時間；Java TimeStamp 欄位實際寫 now + 20 days。</summary>
    public long ExpiresAtUnixMillis { get; set; }

    /// <summary>對照 Java dueypackages.Checked；MVP 只持久化，不做上線通知。</summary>
    public bool Checked { get; set; } = true;

    public bool IsExpired(long nowUnixMillis) => ExpiresAtUnixMillis <= nowUnixMillis;
}
