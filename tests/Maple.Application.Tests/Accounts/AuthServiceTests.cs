using Maple.Application.Accounts;
using Maple.Application.Security;
using Maple.Core.Accounts;

namespace Maple.Application.Tests.Accounts;

/// <summary>AuthService 單元測試。使用 FakePasswordHasher 和 FakeAccountRepository 避免真實 BCrypt 與 DB。</summary>
public sealed class AuthServiceTests
{
    // ── 假實作（測試用）─────────────────────────────────────────────────

    /// <summary>直接以 "hash:{明文}" 作為雜湊，避免測試跑真實 BCrypt work factor。</summary>
    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hash:{password}";
        public bool Verify(string password, string hash) => hash == $"hash:{password}";
    }

    /// <summary>以記憶體 Dictionary 模擬帳號儲存。</summary>
    private sealed class FakeAccountRepository : IAccountRepository
    {
        private readonly Dictionary<string, Account> _store = new(StringComparer.Ordinal);
        private int _nextId = 1;

        public Task<Account?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(accountName, out var account);
            return Task.FromResult<Account?>(account);
        }

        public Task AddAsync(Account account, CancellationToken cancellationToken = default)
        {
            account.Id = _nextId++;
            _store[account.AccountName] = account;
            return Task.CompletedTask;
        }

        public Task<bool> TryAddAsync(Account account, CancellationToken cancellationToken = default)
        {
            if (_store.ContainsKey(account.AccountName))
                return Task.FromResult(false);
            account.Id = _nextId++;
            _store[account.AccountName] = account;
            return Task.FromResult(true);
        }

        public Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
        {
            _store[account.AccountName] = account;
            return Task.CompletedTask;
        }
    }

    private AuthService BuildService(out FakeAccountRepository repo)
    {
        repo = new FakeAccountRepository();
        return new AuthService(repo, new FakePasswordHasher());
    }

    // ── 測試案例 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Authenticate_帳號不存在且不自動建立_回傳InvalidPassword()
    {
        var svc = BuildService(out _);
        var result = await svc.AuthenticateAsync("noUser", "pass", autoRegister: false);
        Assert.Equal(AuthStatus.InvalidPassword, result.Status);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Authenticate_帳號不存在且autoRegister為true_自動建立並回傳Success()
    {
        var svc = BuildService(out var repo);
        // AuthService 會正規化帳號名（trim+lowercase），"newUser" → "newuser"
        var result = await svc.AuthenticateAsync("newUser", "myPass", autoRegister: true);

        Assert.Equal(AuthStatus.Success, result.Status);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Account);
        Assert.Equal("newuser", result.Account.AccountName);   // normalized

        // 正規化後以小寫查詢
        var saved = await repo.FindByNameAsync("newuser");
        Assert.NotNull(saved);
        Assert.Equal("hash:myPass", saved.PasswordHash);
    }

    [Fact]
    public async Task Authenticate_帳號存在且密碼正確_回傳Success並更新LastLoginAt()
    {
        var svc = BuildService(out var repo);
        // 先建帳
        await repo.AddAsync(new Account
        {
            AccountName = "alice",
            PasswordHash = "hash:secret",
            CreatedAt = DateTime.UtcNow,
        });

        var before = DateTime.UtcNow;
        var result = await svc.AuthenticateAsync("alice", "secret");
        var after = DateTime.UtcNow;

        Assert.Equal(AuthStatus.Success, result.Status);
        Assert.NotNull(result.Account?.LastLoginAt);
        Assert.InRange(result.Account!.LastLoginAt!.Value, before, after);
    }

    [Fact]
    public async Task Authenticate_密碼錯誤_回傳InvalidPassword()
    {
        var svc = BuildService(out var repo);
        await repo.AddAsync(new Account
        {
            AccountName = "bob",
            PasswordHash = "hash:correctPass",
            CreatedAt = DateTime.UtcNow,
        });

        var result = await svc.AuthenticateAsync("bob", "wrongPass");
        Assert.Equal(AuthStatus.InvalidPassword, result.Status);
    }

    [Fact]
    public async Task Authenticate_帳號已封鎖_回傳AccountBanned()
    {
        var svc = BuildService(out var repo);
        await repo.AddAsync(new Account
        {
            AccountName = "banned",
            PasswordHash = "hash:pass",
            IsBanned = true,
            BanReason = "違規",
            CreatedAt = DateTime.UtcNow,
        });

        // 即使密碼正確，封鎖帳號應拒絕
        var result = await svc.AuthenticateAsync("banned", "pass");
        Assert.Equal(AuthStatus.AccountBanned, result.Status);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Authenticate_autoRegister建立的帳號再次登入_成功()
    {
        var svc = BuildService(out _);
        // 第一次：自動建立
        await svc.AuthenticateAsync("charlie", "pw", autoRegister: true);
        // 第二次：正常登入
        var result = await svc.AuthenticateAsync("charlie", "pw");
        Assert.Equal(AuthStatus.Success, result.Status);
    }
}
