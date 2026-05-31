namespace Maple.Net;

/// <summary>
/// Login 監聽器的最小設定（由 Host 從 ServerInstanceOptions 投影而來，
/// 讓 Maple.Net 不必依賴 Host 層的設定型別）。
/// </summary>
public sealed record LoginListenerSettings(string InstanceName, string ListenIp, int LoginPort);
