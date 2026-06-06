using Maple.Core.Storage;

namespace Maple.Core.Accounts;

/// <summary>
/// 帳號文件模型（LiteDB 集合的根文件）。
/// 採文件模型設計：一份文件代表一個帳號的完整狀態，Load/Save 為原子單元。
/// </summary>
public sealed class Account
{
    /// <summary>LiteDB 自動遞增主鍵。</summary>
    public int Id { get; set; }

    /// <summary>帳號名稱（唯一索引，用於登入查詢）。</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>密碼雜湊（BCrypt 格式，不儲存明文）。</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>帳號建立時間（UTC）。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最後成功登入時間（UTC）；首次登入前為 null。</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>是否已封鎖。</summary>
    public bool IsBanned { get; set; }

    /// <summary>封鎖原因；未封鎖時為空字串。</summary>
    public string BanReason { get; set; } = string.Empty;

    /// <summary>帳號性別（10=未設定；0=男；1=女）。對照 Java：gender==10 代表新帳號尚未完成性別選擇流程。</summary>
    public byte Gender { get; set; } = 10;

    /// <summary>第二密碼（PIN）。null=尚未設定；新帳號需在 CHOOSE_GENDER 流程中設定。</summary>
    public string? SecondPassword { get; set; }

    /// <summary>帳號層級倉庫快照（所有角色共用）；執行期由 <see cref="World.Player"/> hydrate 成 StorageBox。</summary>
    public AccountStorage Storage { get; set; } = new();
}
