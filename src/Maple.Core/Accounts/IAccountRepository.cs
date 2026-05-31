namespace Maple.Core.Accounts;

/// <summary>
/// 帳號 repository 介面。
/// 定義在 Core（領域邊界），實作在 Maple.Persistence（LiteDB）。
/// 未來若切換至 MongoDB / PostgreSQL，只需替換實作，介面不動。
/// </summary>
public interface IAccountRepository
{
    /// <summary>依帳號名稱查詢；找不到時回傳 null。</summary>
    Task<Account?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default);

    /// <summary>新增帳號（LiteDB 會自動填入 Id）。</summary>
    Task AddAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>
    /// 嘗試新增帳號，若唯一索引衝突（帳號名稱已存在）則回傳 false。
    /// 為 autoRegister 並發場景提供原子 GetOrCreate 語意。
    /// </summary>
    Task<bool> TryAddAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>更新帳號資料（以 Id 比對替換）。</summary>
    Task UpdateAsync(Account account, CancellationToken cancellationToken = default);
}
