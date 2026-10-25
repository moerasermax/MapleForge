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
            // P159：HOMING_BEACON 只送本人 cancelHoming，不廣播。
            if (cancellation.Stats.Contains(MapleBuffStat.HOMING_BEACON))
            {
                continue;
            }

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
    /// P088/P160：對照 Java <c>MapleCharacter.removeDoor</c>：關門後對目標地圖與村莊的人逐一 <c>MapleDoor.sendDestroyData</c>。
    /// </summary>
    public async Task CloseMagicDoorAsync(int ownerId, CancellationToken ct)
    {
        if (_doors.CloseDoor(ownerId) is not { } door)
        {
            return;
        }

        await SendDoorDestroyAsync(door, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// P160：Java <c>sendDestroyData</c>：人在目標地圖、主人或主人隊友 → <c>removeDoor(owner, false)</c> →（主人有隊伍且是主人/隊友）
    /// <c>partyPortal(999999999, 999999999, 0, (-1, -1))</c> → <c>spawnPortal(999999999, 999999999, 0, null)</c>。
    /// </summary>
    public async Task SendDoorDestroyAsync(Door door, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(door);

        foreach (var (entry, mapId) in DoorMapEntries(door))
        {
            if (!_doors.SendsDestroyData(door, mapId, entry.CharId))
            {
                continue;
            }

            await SendAllAsync([entry], V113DoorPackets.RemoveDoor(door.OwnerId), ct).ConfigureAwait(false);
            if (_doors.SendsPartyPortal(door, entry.CharId))
            {
                await SendAllAsync([entry], V113DoorPackets.PartyPortalReset(), ct).ConfigureAwait(false);
            }

            await SendAllAsync([entry], V113DoorPackets.RemoveTownPortal(), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// P160：Java <c>MapleMap.spawnDoor</c>（目標地圖與村莊各一次，canSpawn 過濾）：<c>spawnDoor(owner, targetPosition, false)</c> →
    /// （主人有隊伍且是主人/隊友）<c>partyPortal(town, target, skill, targetPosition)</c> → <c>spawnPortal(town, target, skill, targetPosition)</c>
    /// → <c>enableActions</c>。村莊那扇門 Java 也用目標座標（照抄）；Java 的 800px 視野距離檢查未移植（MapleForge 無視野系統）。
    /// </summary>
    public async Task SendDoorOpenedAsync(Door door, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(door);

        foreach (var (entry, mapId) in DoorMapEntries(door))
        {
            if (!_doors.CanSeeOpening(door, mapId, entry.CharId))
            {
                continue;
            }

            await SendAllAsync([entry], V113DoorPackets.SpawnDoor(door.OwnerId, door.TargetPosition, town: false), ct).ConfigureAwait(false);
            if (_doors.SendsPartyPortal(door, entry.CharId))
            {
                await SendAllAsync([entry], V113DoorPackets.PartyPortal(door.TownMapId, door.TargetMapId, door.TargetPosition), ct).ConfigureAwait(false);
            }

            await SendAllAsync([entry], V113DoorPackets.SpawnPortal(door.TownMapId, door.TargetMapId, door.TargetPosition), ct).ConfigureAwait(false);
            await SendAllAsync([entry], V113StatsPackets.EnableActions(), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// P160：進圖補送（Java <c>MapleDoor.sendSpawnData</c>）：人在目標地圖或（主人本人且無隊伍）→
    /// <c>spawnDoor(owner, 在村莊 ? 村莊傳送點 : 目標座標, true)</c> →（主人有隊伍且是主人/隊友）<c>partyPortal</c> → <c>spawnPortal</c>。
    /// </summary>
    public IReadOnlyList<byte[]> BuildDoorSpawnData(int mapId, int viewerId)
    {
        var packets = new List<byte[]>();
        foreach (var door in _doors.GetDoorsInMap(mapId))
        {
            if (!_doors.SendsSpawnData(door, mapId, viewerId))
            {
                continue;
            }

            var position = door.TownMapId == mapId ? door.TownPortalPosition : door.TargetPosition;
            packets.Add(V113DoorPackets.SpawnDoor(door.OwnerId, position, town: true));
            if (_doors.SendsPartyPortal(door, viewerId))
            {
                packets.Add(V113DoorPackets.PartyPortal(door.TownMapId, door.TargetMapId, door.TargetPosition));
            }

            packets.Add(V113DoorPackets.SpawnPortal(door.TownMapId, door.TargetMapId, door.TargetPosition));
        }

        return packets;
    }

    private IEnumerable<(MapPlayerEntry Entry, int MapId)> DoorMapEntries(Door door)
    {
        var seen = new HashSet<int>();
        foreach (var mapId in door.TargetMapId == door.TownMapId ? [door.TargetMapId] : new[] { door.TargetMapId, door.TownMapId })
        {
            foreach (var entry in _mapRegistry.GetAll(mapId))
            {
                if (seen.Add(entry.CharId))
                {
                    yield return (entry, mapId);
                }
            }
        }
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
