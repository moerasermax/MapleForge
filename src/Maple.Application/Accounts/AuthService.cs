using Maple.Application.Security;
using Maple.Core.Accounts;

namespace Maple.Application.Accounts;

/// <summary>
/// 帳密驗證服務（Application 層 use-case）。
/// autoRegister=true 時帳號不存在會自動建立，適合私服「玩家自行設帳」模式。
/// 此類別無 static 可變狀態；所有狀態均由 DI 注入。
/// </summary>
public sealed class AuthService
{
    private readonly IAccountRepository _accounts;
    private readonly IPasswordHasher _hasher;

    public AuthService(IAccountRepository accounts, IPasswordHasher hasher)
    {
        _accounts = accounts;
        _hasher = hasher;
    }

    /// <summary>
    /// 驗證帳密，回傳驗證結果。
    /// </summary>
    /// <param name="accountName">帳號名稱。</param>
    /// <param name="password">明文密碼。</param>
    /// <param name="autoRegister">
    ///   true：帳號不存在時自動以此密碼建立並回傳 Success。
    ///   false：帳號不存在時回傳 InvalidPassword（不洩漏帳號是否存在）。
    /// </param>
    /// <param name="cancellationToken">取消符記。</param>
    public async Task<AuthResult> AuthenticateAsync(
        string accountName,
        string password,
        bool autoRegister = false,
        CancellationToken cancellationToken = default)
    {
        var account = await _accounts.FindByNameAsync(accountName, cancellationToken);

        if (account is null)
        {
            if (!autoRegister)
                // 帳號不存在且不自動建立：統一回 InvalidPassword（不洩漏帳號是否存在）
                return new AuthResult(AuthStatus.InvalidPassword, null);

            account = new Account
            {
                AccountName = accountName,
                PasswordHash = _hasher.Hash(password),
                CreatedAt = DateTime.UtcNow,
            };
            await _accounts.AddAsync(account, cancellationToken);
            return new AuthResult(AuthStatus.Success, account);
        }

        if (account.IsBanned)
            return new AuthResult(AuthStatus.AccountBanned, account);

        if (!_hasher.Verify(password, account.PasswordHash))
            return new AuthResult(AuthStatus.InvalidPassword, null);

        // 更新最後登入時間後存回 DB
        account.LastLoginAt = DateTime.UtcNow;
        await _accounts.UpdateAsync(account, cancellationToken);

        return new AuthResult(AuthStatus.Success, account);
    }
}
