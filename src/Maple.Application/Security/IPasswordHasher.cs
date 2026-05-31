namespace Maple.Application.Security;

/// <summary>
/// 密碼雜湊介面（Application 層）。
/// 抽成介面方便單元測試替換為 fake 實作，避免測試直接跑 BCrypt 的高 work factor。
/// </summary>
public interface IPasswordHasher
{
    /// <summary>將明文密碼轉為雜湊字串（含 salt）。</summary>
    string Hash(string password);

    /// <summary>驗證明文密碼與已儲存的雜湊是否相符。</summary>
    bool Verify(string password, string hash);
}
