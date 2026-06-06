using LiteDB;
using Maple.Core.Accounts;
using System;

namespace Maple.Persistence.Accounts;

/// <summary>
/// 以 LiteDB 實作的帳號 repository。
/// collection 名稱為 "accounts"，帳號名稱建唯一索引加速登入查詢。
/// LiteDB 的操作本身為同步，以 Task.FromResult / Task.CompletedTask 包裝，符合介面 async 契約。
/// </summary>
public sealed class LiteDbAccountRepository : IAccountRepository
{
    private readonly ILiteCollection<Account> _collection;

    public LiteDbAccountRepository(LiteDatabase db)
    {
        _collection = db.GetCollection<Account>("accounts");
        // 帳號名稱唯一索引；unique=true 在 DB 層強制不重複
        _collection.EnsureIndex(a => a.AccountName, unique: true);
    }

    /// <inheritdoc/>
    public Task<Account?> FindByIdAsync(int accountId, CancellationToken cancellationToken = default)
    {
        var account = _collection.FindById(accountId);
        return Task.FromResult<Account?>(account);
    }

    /// <inheritdoc/>
    public Task<Account?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default)
    {
        var account = _collection.FindOne(a => a.AccountName == accountName);
        return Task.FromResult<Account?>(account);
    }

    /// <inheritdoc/>
    public Task AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        _collection.Insert(account);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> TryAddAsync(Account account, CancellationToken cancellationToken = default)
    {
        try
        {
            _collection.Insert(account);
            return Task.FromResult(true);
        }
        catch (LiteException ex) when (ex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        _collection.Update(account);
        return Task.CompletedTask;
    }
}
