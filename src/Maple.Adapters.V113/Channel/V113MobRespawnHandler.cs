using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P067（M4-2 世界 tick 第四步）：世界 tick 排程器對單一 field 該做的怪物重生處理——找出該生的
/// 怪、生出來、廣播 <c>SpawnMonster</c>（對照 Java <c>map.spawnMonster(monster, -2)</c>，
/// spawnType -2 = 一般重生非特殊效果）給場上所有玩家。控制權指派不在這裡處理，沿用既有
/// AutoAggro 機制（玩家端主動請求控制權），跟初始進場 replay 走的路徑不同（那個是對單一
/// session 補送控制封包，這裡是對整個地圖廣播新怪物出現）。跟 <see cref="V113DropExpiryHandler"/>
/// 一樣是薄封裝角色，排程節奏不歸它管。
/// </summary>
public sealed class V113MobRespawnHandler
{
    private readonly CombatService _combat;
    private readonly IMapSessionRegistry _mapRegistry;

    public V113MobRespawnHandler(CombatService combat, IMapSessionRegistry mapRegistry)
    {
        _combat = combat;
        _mapRegistry = mapRegistry;
    }

    public async Task RespawnMonstersAsync(FieldInstance field, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);

        // 對照既有戰鬥 handler 慣例：field 的領域變更（生怪）要在 lock(field) 內完成，
        // 異步廣播留到鎖外（lock 不能橫跨 await，見 V113DropExpiryHandler 同款寫法）。
        IReadOnlyList<Mob> spawned;
        lock (field)
        {
            spawned = _combat.RespawnMonsters(field, now);
        }

        if (spawned.Count == 0)
        {
            return;
        }

        var recipients = _mapRegistry.GetAll(field.MapId);
        if (recipients.Count == 0)
        {
            return;
        }

        foreach (var mob in spawned)
        {
            var packet = V113CombatPackets.SpawnMonster(mob);
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
