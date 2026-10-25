using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// buff 結束的地圖副作用（P089 從連線 handler 抽出，讓世界 tick 也能共用）：
/// P082 SUMMON/PUPPET → 移除召喚獸並全圖廣播 <c>removeSummon(summon, true)</c>（Java <c>deregisterBuffStats</c>）；
/// P088 時空門 buff → 關門並送 destroy data（Java <c>cancelEffect</c> 的 <c>isMagicDoor()</c> → <c>removeDoor</c>）。
/// 廣播一律走 <see cref="IMapSessionRegistry"/>（含主人本人），best-effort。
/// </summary>
public sealed class V113BuffCancellationEffects
{
    private readonly SummonService _summons;
    private readonly DoorService _doors;
    private readonly IMapSessionRegistry _mapRegistry;

    public V113BuffCancellationEffects(SummonService summons, DoorService doors, IMapSessionRegistry mapRegistry)
    {
        _summons = summons;
        _doors = doors;
        _mapRegistry = mapRegistry;
    }

    public async Task ApplyAsync(
        Player player,
        FieldInstance? field,
        IReadOnlyList<PlayerBuffCancellation> cancellations,
        CancellationToken ct,
        Func<byte[], CancellationToken, Task>? sendSelf = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(cancellations);

        if (cancellations.Count == 0)
        {
            return;
        }

        // P139：Java cancelPlayerBuffs → map.broadcastMessage(this, cancelForeignBuff(id, buffstats), false)（每個被取消的效果一包）。
        var others = _mapRegistry.GetOthers(player.Character.MapId, player.Character.Id);
        foreach (var cancellation in cancellations)
        {
            await SendAllAsync(others, V113SkillPackets.CancelForeignBuff(player.Character.Id, cancellation.Stats), ct).ConfigureAwait(false);
        }

        // P116：Java cancelEffect → recalcLocalStats → enforceMaxHpMp（神聖之火等 MAXHP/MAXMP buff 結束時壓回上限）。
        if (sendSelf is not null)
        {
            var enforced = player.EnforceMaxHpMp();
            if (enforced.Count > 0)
            {
                try
                {
                    await sendSelf(V113StatsPackets.UpdateStats(enforced), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // best-effort
                }
            }
        }

        if (cancellations.Any(static c => DoorService.IsMagicDoorSkill(c.SourceId)))
        {
            await CloseMagicDoorAsync(player.Character.Id, ct).ConfigureAwait(false);
        }

        if (field is null)
        {
            return;
        }

        IReadOnlyList<Summon> removed;
        lock (field)
        {
            removed = _summons.RemoveForCancelledBuffs(field, player.Character.Id, cancellations);
        }

        foreach (var summon in removed)
        {
            await SendAllAsync(_mapRegistry.GetAll(field.MapId), V113SummonPackets.RemoveSummon(summon, animated: true), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// P088：對照 Java <c>MapleCharacter.removeDoor</c> + <c>MapleDoor.sendDestroyData</c>：目標地圖所有人、以及人在村莊的
    /// 主人本人，收到 <c>removeDoor(owner, false)</c> + <c>spawnPortal(999999999, 999999999)</c>。隊伍 partyPortal 重設未移植。
    /// </summary>
    public async Task CloseMagicDoorAsync(int ownerId, CancellationToken ct)
    {
        if (_doors.CloseDoor(ownerId) is not { } door)
        {
            return;
        }

        var recipients = _mapRegistry.GetAll(door.TargetMapId)
            .Concat(_mapRegistry.GetAll(door.TownMapId).Where(e => e.CharId == ownerId))
            .ToArray();
        await SendAllAsync(recipients, V113DoorPackets.RemoveDoor(ownerId), ct).ConfigureAwait(false);
        await SendAllAsync(recipients, V113DoorPackets.RemoveTownPortal(), ct).ConfigureAwait(false);
    }

    private static async Task SendAllAsync(IEnumerable<MapPlayerEntry> recipients, byte[] packet, CancellationToken ct)
    {
        foreach (var entry in recipients)
        {
            try
            {
                await entry.SendPacket(packet, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // best-effort；斷線 session 由中央連線處理生命週期負責清理。
            }
        }
    }
}
