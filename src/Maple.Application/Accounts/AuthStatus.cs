namespace Maple.Application.Accounts;

/// <summary>帳密驗證結果狀態碼。</summary>
public enum AuthStatus
{
    /// <summary>驗證成功（含 autoRegister 自動建帳後成功）。</summary>
    Success,

    /// <summary>密碼錯誤，或帳號不存在且 autoRegister=false。</summary>
    InvalidPassword,

    /// <summary>帳號已封鎖（IsBanned=true）。</summary>
    AccountBanned,
}
