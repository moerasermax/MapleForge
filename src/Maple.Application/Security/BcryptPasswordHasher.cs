// BCrypt.Net.BCrypt 與命名空間同名，用別名避免 CS0234
using BcryptLib = BCrypt.Net.BCrypt;

namespace Maple.Application.Security;

/// <summary>
/// 以 BCrypt（BCrypt.Net-Next 套件）實作的密碼雜湊器。
/// 選用 BCrypt 理由：自動含 salt、可調 work factor、業界標準密碼儲存方案。
/// work factor = 12（一般硬體約 200-500 ms），對登入頻率（非批次）足夠安全。
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    // BCrypt work factor：每增加 1 計算量翻倍；12 是 2026 年的合理基線
    private const int WorkFactor = 12;

    /// <inheritdoc/>
    public string Hash(string password) =>
        BcryptLib.HashPassword(password, WorkFactor);

    /// <inheritdoc/>
    public bool Verify(string password, string hash) =>
        BcryptLib.Verify(password, hash);
}
