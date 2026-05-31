namespace Maple.Net;

/// <summary>
/// 處理一條新接受的連線（由版本 adapter 實作：握手、cipher、封包路由）。
/// M1 由 V113 adapter 實作；版本抽象接縫延到 M3（見設計書 §0.5）。
/// </summary>
public interface IConnectionHandler
{
    Task HandleConnectionAsync(MapleSession session, CancellationToken ct);
}
