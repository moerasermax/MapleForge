using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Host.Shared;

/// <summary>
/// P112：怪物異常狀態 tick（1 秒）。Java 每個狀態各自 <c>BuffTimer.schedule(cancelTask, aniTime)</c>
/// 精確到期、毒傷 <c>register(applyEffectTask, 1000)</c> 每秒一次；MapleForge 以單一 1 秒排程器近似
/// （到期最多晚 1 秒），不併入 3 秒的 <see cref="WorldTickHostedService"/>。
/// </summary>
internal sealed class MobStatusTickHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly IFieldInstanceRegistry _fields;
    private readonly V113MobStatusTickHandler _handler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MobStatusTickHostedService> _log;

    public MobStatusTickHostedService(
        IFieldInstanceRegistry fields,
        V113MobStatusTickHandler handler,
        ILogger<MobStatusTickHostedService> log,
        TimeProvider? timeProvider = null)
    {
        _fields = fields;
        _handler = handler;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var field in _fields.All)
            {
                try
                {
                    await _handler.TickAsync(field, now, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[MobStatusTick] 地圖 {MapId} 怪物狀態處理失敗，跳過本次繼續巡下一個 field", field.MapId);
                }
            }
        }
    }
}
