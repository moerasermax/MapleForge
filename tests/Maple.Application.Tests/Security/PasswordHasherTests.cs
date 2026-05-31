using Maple.Application.Security;

namespace Maple.Application.Tests.Security;

/// <summary>BcryptPasswordHasher 單元測試。</summary>
public sealed class PasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_產生非明文字串()
    {
        var hash = _hasher.Hash("myPassword");
        Assert.NotEqual("myPassword", hash);
        Assert.False(string.IsNullOrEmpty(hash));
    }

    [Fact]
    public void Hash_相同密碼每次產生不同雜湊_因隨機salt()
    {
        var hash1 = _hasher.Hash("samePassword");
        var hash2 = _hasher.Hash("samePassword");
        // BCrypt 每次雜湊包含不同隨機 salt，結果應不同
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Verify_正確密碼回傳true()
    {
        var hash = _hasher.Hash("correctPassword");
        Assert.True(_hasher.Verify("correctPassword", hash));
    }

    [Fact]
    public void Verify_錯誤密碼回傳false()
    {
        var hash = _hasher.Hash("correctPassword");
        Assert.False(_hasher.Verify("wrongPassword", hash));
    }

    [Fact]
    public void Verify_空密碼與非空密碼不相符()
    {
        var hash = _hasher.Hash("somePassword");
        Assert.False(_hasher.Verify("", hash));
    }
}
