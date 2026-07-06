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
    MobKillRewards? Rewards = null);

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

    public CombatService(MapService maps, IMobKillHandler? mobKillHandler = null)
    {
        _maps = maps;
        _mobKillHandler = mobKillHandler;
    }

    /// <summary>
    /// 依地圖 WZ life 建立初始怪物並加入 field。objectId 由呼叫端提供起點，預設對齊 Java MapleMap runningOid。
    /// </summary>
    public IReadOnlyList<Mob> SpawnMapMonsters(FieldInstance field, int mapId, int firstObjectId = DefaultMobObjectIdBase + 1)
    {
        ArgumentNullException.ThrowIfNull(field);

        var map = _maps.LoadMap(mapId);
        var objectId = firstObjectId;
        var spawned = new List<Mob>();

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
        }

        return spawned;
    }

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
                rewards));

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
