using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Host.Shared;

/// <summary>
/// P063（M4-2 世界 tick 第三步）：第一個真正的週期性世界 tick 排程器。對照 Java
/// <c>World.Respawn</c>（<c>WorldTimer.register(new Respawn(), 3000)</c>，每 3 秒巡一次所有地圖）——
/// 這裡先只做掉落物過期這一件事，排程節奏忠實對照（3 秒），巡邏對象是這個 channel process 自己
/// 持有的 field（MapleForge 每頻道一個 process，不像 Java 是單一 process 內巡全部頻道）。
/// 之後 M4-2 其餘項目（怪物重生等）若要接上，可以重用這個排程器骨架，不需要另開一個
/// BackgroundService。
/// </summary>
internal sealed class DropExpiryHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);

    private readonly IFieldInstanceRegistry _fields;
    private readonly V113DropExpiryHandler _handler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DropExpiryHostedService> _log;

    public DropExpiryHostedService(
        IFieldInstanceRegistry fields,
        V113DropExpiryHandler handler,
        ILogger<DropExpiryHostedService> log,
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
                    await _handler.ExpireDropsAsync(field, now, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[WorldTick] 地圖 {MapId} 掉落物過期處理失敗，跳過本次繼續巡下一個 field", field.MapId);
                }
            }
        }
    }
}
