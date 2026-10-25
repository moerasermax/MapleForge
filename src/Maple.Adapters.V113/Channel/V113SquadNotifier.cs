using Maple.Application.Maps;
using Maple.Application.OnlinePlayers;
using Maple.Application.Squads;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P190：把遠征隊通知轉成 v113 封包：開始地圖 getClock／stopClock／getItemNotice（serverNotice 6），
/// 個人 dropMessage（serverNotice type）。送包 best-effort。
/// </summary>
public sealed class V113SquadNotifier
{
    private readonly IMapSessionRegistry _maps;
    private readonly IOnlinePlayerRegistry _online;

    public V113SquadNotifier(IMapSessionRegistry maps, IOnlinePlayerRegistry online)
    {
        _maps = maps;
        _online = online;
    }

    public async Task SendAsync(SquadNotice notice, CancellationToken ct)
    {
        switch (notice)
        {
            case SquadMapClock clock:
                await BroadcastAsync(clock.MapId, V113TransportEffects.Clock(clock.Seconds), ct);
                break;
            case SquadMapStopClock stop:
                await BroadcastAsync(stop.MapId, V113TransportEffects.StopClock(), ct);
                break;
            case SquadMapNotice mapNotice:
                await BroadcastAsync(mapNotice.MapId, V113BroadcastPackets.ServerNotice(6, mapNotice.Message), ct);
                break;
            case SquadPlayerNotice playerNotice when _online.FindByName(playerNotice.CharacterName) is { } online:
                await SendBestEffortAsync(online.SendPacket, V113BroadcastPackets.ServerNotice(playerNotice.Type, playerNotice.Message), ct);
                break;
        }
    }

    private async Task BroadcastAsync(int mapId, byte[] packet, CancellationToken ct)
    {
        foreach (var entry in _maps.GetAll(mapId).ToArray())
        {
            await SendBestEffortAsync(entry.SendPacket, packet, ct);
        }
    }

    private static async Task SendBestEffortAsync(Func<byte[], CancellationToken, Task> send, byte[] packet, CancellationToken ct)
    {
        try
        {
            await send(packet, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 對方連線關閉中：略過。
        }
    }
}
