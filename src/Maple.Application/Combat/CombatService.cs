using Maple.Application.Maps;
using Maple.Application.Drops;
using Maple.Core.World;

namespace Maple.Application.Combat;

public sealed record CombatAttack(IReadOnlyList<CombatAttackTarget> Targets);

public sealed record CombatAttackTarget(int ObjectId, IReadOnlyList<int> DamageLines)
{
    public long TotalDamage => DamageLines.Sum(static d => Math.Max(0, (long)d));
}

public sealed record CombatMobHit(
    int ObjectId,
    int MonsterId,
    long RequestedDamage,
    long AppliedDamage,
    long RemainingHp,
    bool Killed,
    MobKillRewards? Rewards = null,
    // 死亡當下的怪物控制者（死亡前捕捉，field.Remove 後就查不到了）。0=無控制者。
    // 對照 Java MapleMonster 死亡流程對控制者送 stopControllingMonster。
    int ControllerId = 0);

public sealed record CombatAttackResult(IReadOnlyList<CombatMobHit> Hits)
{
    public bool AnyKilled => Hits.Any(static h => h.Killed);
}

public sealed record CombatMobKillResult(int ObjectId, int MonsterId, byte Animation, bool Killed);

/// <summary>戰鬥用例：建立場上怪物、套用攻擊傷害、處理怪物死亡生命週期。</summary>
public sealed class CombatService
{
    public const int DefaultMobObjectIdBase = 100_000;

    private readonly MapService _maps;
    private readonly IMobKillHandler? _mobKillHandler;
    private readonly TimeProvider _timeProvider;

    public CombatService(MapService maps, IMobKillHandler? mobKillHandler = null, TimeProvider? timeProvider = null)
    {
        _maps = maps;
        _mobKillHandler = mobKillHandler;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 依地圖 WZ life 建立初始怪物並加入 field，同時建立對應的 <see cref="MobSpawnPoint"/>
    /// 供後續 <see cref="RespawnMonsters"/> 使用。objectId 由呼叫端提供起點，預設對齊 Java
    /// MapleMap runningOid。
    /// </summary>
    public IReadOnlyList<Mob> SpawnMapMonsters(FieldInstance field, int mapId, int firstObjectId = DefaultMobObjectIdBase + 1)
    {
        ArgumentNullException.ThrowIfNull(field);

        var map = _maps.LoadMap(mapId);
        var objectId = firstObjectId;
        var spawned = new List<Mob>();
        var now = _timeProvider.GetUtcNow();

        foreach (var def in map.Monsters)
        {
            if (def.Hide)
            {
                continue;
            }

            var stats = _maps.LoadMobStats(def.MonsterId);
            if (stats is null)
            {
                continue;
            }

            var mob = new Mob(def, stats, objectId++);
            field.Add(mob);
            spawned.Add(mob);

            var point = new MobSpawnPoint(def, stats.Mobile, now);
            point.OnSpawned();
            field.SpawnPoints.Add(point);
        }

        return spawned;
    }

    /// <summary>
    /// P065（M4-2 世界 tick，怪物重生第二步）：對照 Java <c>MapleMap.respawn(force: false)</c>——
    /// 巡覽這個 field 的重生點，找出該生的怪並生出來，直到達到地圖級生怪上限（重生點數量*3 -
    /// 場上目前怪物數，對照 Java <c>monsterSpawn.size()*3 - spawnedMonstersOnMap.get()</c>）。
    /// 刻意簡化：Java 用 <c>Collections.shuffle</c> 打亂重生點候選順序增加公平性，這裡維持固定
    /// 順序（field.SpawnPoints 的既有順序）——只影響「上限提前打滿時哪些點被跳過」的隨機性，
    /// 不影響核心規則本身，已記錄這個刻意偏離。仍不接任何排程器，也不處理怪物死亡時通知
    /// 對應重生點（見任務歷程，留給下一個 P-phase）。
    /// </summary>
    public IReadOnlyList<Mob> RespawnMonsters(FieldInstance field, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.SpawnPoints.Count == 0)
        {
            return Array.Empty<Mob>();
        }

        var currentMobCount = field.Objects.OfType<Mob>().Count();
        var shouldSpawnCount = (field.SpawnPoints.Count * 3) - currentMobCount;
        if (shouldSpawnCount <= 0)
        {
            return Array.Empty<Mob>();
        }

        var spawned = new List<Mob>();
        var nextObjectId = AllocateNextMobObjectId(field);

        foreach (var point in field.SpawnPoints)
        {
            if (spawned.Count >= shouldSpawnCount)
            {
                break;
            }

            if (!point.ShouldSpawn(now))
            {
                continue;
            }

            var stats = _maps.LoadMobStats(point.Definition.MonsterId);
            if (stats is null)
            {
                continue;
            }

            var mob = new Mob(point.Definition, stats, nextObjectId++);
            field.Add(mob);
            point.OnSpawned();
            spawned.Add(mob);
        }

        return spawned;
    }

    private static int AllocateNextMobObjectId(FieldInstance field)
        => Math.Max(DefaultMobObjectIdBase, field.Objects.Select(static o => o.ObjectId).DefaultIfEmpty(0).Max()) + 1;

    /// <summary>套用一次攻擊。死亡怪物會從 field 移除，供上層廣播 KILL_MONSTER。</summary>
    public CombatAttackResult ApplyAttack(FieldInstance field, Player attacker, CombatAttack attack)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(attack);

        if (!attacker.IsAlive)
        {
            return new CombatAttackResult(Array.Empty<CombatMobHit>());
        }

        var hits = new List<CombatMobHit>();
        foreach (var target in attack.Targets)
        {
            if (field.Get(target.ObjectId) is not Mob mob || !mob.IsAlive)
            {
                continue;
            }

            var result = mob.TakeDamage(target.TotalDamage);
            var rewards = result.Killed
                ? _mobKillHandler?.OnMobKilled(field, attacker, mob)
                : null;

            hits.Add(new CombatMobHit(
                result.ObjectId,
                result.MonsterId,
                result.RequestedDamage,
                result.AppliedDamage,
                result.RemainingHp,
                result.Killed,
                rewards,
                mob.ControllerId));

            if (result.Killed)
            {
                field.Remove(mob.ObjectId);
            }
        }

        return new CombatAttackResult(hits);
    }

    /// <summary>Kill a mob without EXP/drop rewards. Used by Java MonsterBomb self-destruction path.</summary>
    public CombatMobKillResult KillMobWithoutRewards(FieldInstance field, int mobObjectId, byte animation)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.Get(mobObjectId) is not Mob mob || !mob.IsAlive)
        {
            return new CombatMobKillResult(mobObjectId, 0, animation, Killed: false);
        }

        field.Remove(mob.ObjectId);
        return new CombatMobKillResult(mob.ObjectId, mob.Definition.MonsterId, animation, Killed: true);
    }
}
