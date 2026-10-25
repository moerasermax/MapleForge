using Maple.Adapters.V113.Channel;
using Maple.Application.Squads;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Host.Shared;

/// <summary>P190：遠征隊到期移除（Java MapleSquad.scheduleRemoval）與排隊遞補；每秒 tick 並送出通知。</summary>
internal sealed class SquadTickHostedService : BackgroundService
{
    private readonly SquadRegistry _squads;
    private readonly V113SquadNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<SquadTickHostedService> _log;

    public SquadTickHostedService(SquadRegistry squads, V113SquadNotifier notifier, ILogger<SquadTickHostedService> log, TimeProvider? time = null)
    {
        _squads = squads;
        _notifier = notifier;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                foreach (var notice in _squads.Tick(channelIndex: 0))
                {
                    await _notifier.SendAsync(notice, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[Squad] 遠征隊 tick 失敗，下次繼續");
            }
        }
    }
}
