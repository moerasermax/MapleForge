namespace Maple.Net;

/// <summary>Channel 監聽器設定（最小化，避免 Maple.Net 依賴 Host.Shared）。</summary>
public sealed record ChannelListenerSettings(
    string InstanceName,
    string ListenIp,
    int ChannelPort);
