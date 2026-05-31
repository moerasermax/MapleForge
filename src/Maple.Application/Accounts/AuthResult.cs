using Maple.Core.Accounts;

namespace Maple.Application.Accounts;

/// <summary>帳密驗證結果（不可變 record）。</summary>
/// <param name="Status">驗證狀態碼。</param>
/// <param name="Account">成功時為帳號文件；失敗時為 null（帳號不存在）或帳號文件（帳號存在但被封鎖）。</param>
public sealed record AuthResult(AuthStatus Status, Account? Account)
{
    /// <summary>是否驗證成功的捷徑屬性。</summary>
    public bool IsSuccess => Status == AuthStatus.Success;
}
