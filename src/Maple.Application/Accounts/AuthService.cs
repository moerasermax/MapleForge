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
        // ④ 正規化：去頭尾空白 + 小寫（帳號不分大小寫）
        var normalizedName = accountName.Trim().ToLowerInvariant();

        var account = await _accounts.FindByNameAsync(normalizedName, cancellationToken);

        if (account is null)
        {
            if (!autoRegister)
                return new AuthResult(AuthStatus.InvalidPassword, null);

            var newAccount = new Account
            {
                AccountName = normalizedName,
                PasswordHash = _hasher.Hash(password),
                CreatedAt = DateTime.UtcNow,
            };

            // ① race-safe：TryAddAsync 在並發撞名時回 false
            bool created = await _accounts.TryAddAsync(newAccount, cancellationToken);
            if (created)
                return new AuthResult(AuthStatus.Success, newAccount);

            // 並發建帳失敗：另一請求搶先，重新查詢並驗密
            account = await _accounts.FindByNameAsync(normalizedName, cancellationToken);
            if (account is null)
                return new AuthResult(AuthStatus.InvalidPassword, null);
        }

        if (account.IsBanned)
            return new AuthResult(AuthStatus.AccountBanned, account);

        if (!_hasher.Verify(password, account.PasswordHash))
            return new AuthResult(AuthStatus.InvalidPassword, null);

        account.LastLoginAt = DateTime.UtcNow;
        await _accounts.UpdateAsync(account, cancellationToken);

        return new AuthResult(AuthStatus.Success, account);
    }

    /// <summary>
    /// 性別選擇流程完成後，將帳號的性別與第二密碼（PIN）寫入持久層。
    /// 由 v113 SET_GENDER handler 在驗證完 c2s 封包後呼叫。
    /// </summary>
    public async Task SetGenderAndPinAsync(
        Account account,
        byte gender,
        string secondPassword,
        CancellationToken cancellationToken = default)
    {
        account.Gender = gender;
        account.SecondPassword = secondPassword;
        await _accounts.UpdateAsync(account, cancellationToken);
    }
}
