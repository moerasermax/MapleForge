using Maple.Application.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Host.Shared;

/// <summary>P173：交通船班排程（Java EventScriptManager 載入 Boats／Trains／Geenie／Flight／AirPlane／Subway 並 init）；每秒 tick。</summary>
internal sealed class TransportHostedService : BackgroundService
{
    private readonly TransportService _transport;
    private readonly TimeProvider _time;
    private readonly ILogger<TransportHostedService> _log;

    public TransportHostedService(TransportService transport, ILogger<TransportHostedService> log, TimeProvider? time = null)
    {
        _transport = transport;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _transport.StartAsync(_time.GetUtcNow(), stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _transport.TickAsync(_time.GetUtcNow(), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[Transport] 船班排程處理失敗，下次 tick 繼續");
            }
        }
    }
}
