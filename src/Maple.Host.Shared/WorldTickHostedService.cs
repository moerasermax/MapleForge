using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Host.Shared;

/// <summary>
/// M4-2 世界 tick 排程器。對照 Java <c>World.Respawn</c>（<c>WorldTimer.register(new Respawn(),
/// 3000)</c>，每 3 秒巡一次所有地圖，同一次 tick 依序處理掉落物過期跟怪物重生等項目）——這裡
/// 沿用同一個節奏（3 秒），巡邏對象是這個 channel process 自己持有的 field（MapleForge 每頻道
/// 一個 process，不像 Java 是單一 process 內巡全部頻道）。
///
/// P063（第三步）：第一個消費者，掉落物過期。
/// P067（M4-2 第二個切片第四步）：第二個消費者，怪物重生——沿用 P063 建立的排程器骨架，改成
/// 通用命名（原本叫 <c>DropExpiryHostedService</c>），單一 tick 依序處理兩者，貼近 Java
/// <c>handleMap</c> 一次迴圈做多件事的行為，也不需要為每個新消費者另開一個 BackgroundService。
/// </summary>
internal sealed class WorldTickHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);

    private readonly IFieldInstanceRegistry _fields;
    private readonly V113DropExpiryHandler _drops;
    private readonly V113MobRespawnHandler _mobs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorldTickHostedService> _log;

    public WorldTickHostedService(
        IFieldInstanceRegistry fields,
        V113DropExpiryHandler drops,
        V113MobRespawnHandler mobs,
        ILogger<WorldTickHostedService> log,
        TimeProvider? timeProvider = null)
    {
        _fields = fields;
        _drops = drops;
        _mobs = mobs;
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
                    await _drops.ExpireDropsAsync(field, now, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[WorldTick] 地圖 {MapId} 掉落物過期處理失敗，跳過本次繼續巡下一個 field", field.MapId);
                }

                try
                {
                    await _mobs.RespawnMonstersAsync(field, now, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[WorldTick] 地圖 {MapId} 怪物重生處理失敗，跳過本次繼續巡下一個 field", field.MapId);
                }
            }
        }
    }
}
