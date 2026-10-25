using Maple.Application.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Host.Shared;

/// <summary>P177：事件腳本主機啟動（init）與每秒 tick（到期的 em.schedule）。</summary>
internal sealed class EventScriptHostedService : BackgroundService
{
    private readonly IEventScriptHost _host;
    private readonly TimeProvider _time;
    private readonly ILogger<EventScriptHostedService> _log;

    public EventScriptHostedService(IEventScriptHost host, ILogger<EventScriptHostedService> log, TimeProvider? time = null)
    {
        _host = host;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 讓 Channel listener 等其他 hosted service 先起來（init 會呼叫世界操作）。
        await Task.Yield();
        _host.Start(_time.GetUtcNow());
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                _host.Tick(_time.GetUtcNow());
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[EventScript] 事件排程處理失敗，下次 tick 繼續");
            }
        }
    }
}
