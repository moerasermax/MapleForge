namespace Maple.Net;

/// <summary>Channel 連線處理介面（DI 注入，允許 v113 具體實作解耦）。</summary>
public interface IChannelConnectionHandler
{
    Task HandleChannelConnectionAsync(MapleSession session, CancellationToken ct);
}
