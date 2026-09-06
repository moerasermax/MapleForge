using Maple.Application.Drops;
using Maple.Application.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P063（M4-2 世界 tick 第三步）：世界 tick 排程器對單一 field 該做的事——找出過期掉落物並廣播
/// <c>REMOVE_ITEM_FROM_MAP</c>（animation=0/Expire，對照 Java <c>MapleMapItem.expire</c>）給場上
/// 所有玩家。P069：同一次 tick 也處理限定拾取權轉開放（<c>MapDrop.PromoteFfaDrops</c>，對照
/// Java <c>item.shouldFFA()</c>），這個轉換 Java 本身不廣播任何封包，純粹是伺服器內部狀態變更。
/// 排程本身（多久跑一次、跑哪些 field）刻意留給呼叫端（<c>Maple.Host.Shared</c> 的
/// <c>BackgroundService</c>），這裡只做「單次 tick 對單一 field 的處理」，維持跟其餘 V113*Handler
/// （<see cref="V113GuildOperationHandler"/>/<see cref="V113SummonHandler"/>）一致的薄封裝角色。
/// </summary>
public sealed class V113DropExpiryHandler
{
    private readonly DropService _drops;
    private readonly IMapSessionRegistry _mapRegistry;

    public V113DropExpiryHandler(DropService drops, IMapSessionRegistry mapRegistry)
    {
        _drops = drops;
        _mapRegistry = mapRegistry;
    }

    public async Task ExpireDropsAsync(FieldInstance field, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);

        // 對照既有戰鬥/拾取 handler 慣例：field 的領域變更（限定拾取權轉開放、移除過期掉落物）
        // 要在 lock(field) 內完成，異步廣播留到鎖外——lock 區塊不能橫跨 await，且世界 tick 排程器
        // 是背景執行緒，更需要跟其他連線 handler 的 lock(field) 互斥，避免併發修改同一個 Dictionary。
        IReadOnlyList<MapDrop> expired;
        lock (field)
        {
            _drops.PromoteFfaDrops(field, now);
            expired = _drops.ExpireDrops(field, now);
        }

        if (expired.Count == 0)
        {
            return;
        }

        var recipients = _mapRegistry.GetAll(field.MapId);
        if (recipients.Count == 0)
        {
            return;
        }

        foreach (var drop in expired)
        {
            var packet = V113DropPackets.RemoveItemFromMap(drop.ObjectId, animation: 0);
            foreach (var recipient in recipients)
            {
                try
                {
                    await recipient.SendPacket(packet, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // 世界 tick 廣播是 best-effort；斷線 session 由中央連線處理生命週期負責清理。
                }
            }
        }
    }
}
