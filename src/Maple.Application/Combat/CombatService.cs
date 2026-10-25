using Maple.Application.Maps;
using Maple.Application.Drops;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Combat;

/// <param name="SkillEffect">P111：攻擊技能效果（Java <c>getAttackEffect</c>）；命中存活怪物時依其 MonsterStatuses 附帶異常狀態。</param>
/// <param name="SkillId">P124：客戶端宣稱的攻擊技能 id（Java <c>attack.skill</c>，用於鬼神之擊等特例）。</param>
/// <param name="IsMagic">P124：魔法攻擊（Java <c>applyAttackMagic</c>）——反射判斷改看 MAGIC_DAMAGE_REFLECT。</param>
/// <param name="FromSummon">P138：召喚獸攻擊（Java SummonAttack 另一條路徑，不做 applyAttack 的射程檢查）。</param>
public sealed record CombatAttack(
    IReadOnlyList<CombatAttackTarget> Targets,
    MapleStatEffect? SkillEffect = null,
    int SkillId = 0,
    bool IsMagic = false,
    bool FromSummon = false);

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
    int ControllerId = 0,
    // P107：命中後的血條顯示快照（傷害 > 0 時才有；對照 Java MapleMonster.damage 的 HPDisplayType 分派）。
    CombatMobHpDisplay? HpDisplay = null,
    // P108：此次命中後攻擊者的武陵能量（Java HPDisplayType -1/2 存活命中時 mulungEnergyModify(true)）；null = 未變動。
    short? MulungEnergyAfter = null,
    // P111：此次命中成功套用的怪物異常狀態（呼叫端逐一廣播 APPLY_MONSTER_STATUS）。
    IReadOnlyList<MonsterStatusEffect>? AppliedStatuses = null,
    // P124：怪物身上有物/魔反射時，攻擊者因此被扣的 HP（Java player.addHP(-(7000 + nextInt(8000)))，實際扣血量）。
    int ReflectedDamage = 0,
    // P143：吸魔成功時的被動技能 ID（呼叫端送 showOwnBuffEffect／showBuffeffect）；0 = 未觸發。
    int MpEaterSkillId = 0,
    // P144：偷錢掉出的楓幣（呼叫端廣播 DROP_ITEM_FROM_MAPOBJECT）。
    IReadOnlyList<MapDrop>? PickPocketDrops = null,
    // P148：死亡後生成的 revive 怪物（呼叫端廣播 SPAWN_MONSTER(link = 本怪 oid) 並把控制權交給攻擊者）。
    IReadOnlyList<Mob>? Revived = null);

/// <summary>
/// 怪物血條顯示所需快照（在戰鬥鎖內擷取）：顯示類型、血量、王血條顏色、最近攻擊者。
/// 封包版型由 Adapters 決定。
/// </summary>
public sealed record CombatMobHpDisplay(
    sbyte DisplayType,
    long Hp,
    long MaxHp,
    byte TagColor,
    byte TagBgColor,
    IReadOnlyList<int> RecentAttackerIds)
{
    /// <summary>對照 Java <c>(int) Math.ceil((hp * 100.0) / getMobMaxHp())</c>。</summary>
    public byte HpPercent => (byte)Math.Clamp((int)Math.Ceiling(Hp * 100.0 / Math.Max(1, MaxHp)), 0, 100);
}

/// <param name="Aborted">
/// P137/P138：Java applyAttack／applyAttackMagic 中途 <c>return</c>（魔法傷害封鎖門檻、超出射程）——後續怪物未處理，
/// 呼叫端不做尾段 applyTo。
/// </param>
public sealed record CombatAttackResult(IReadOnlyList<CombatMobHit> Hits, bool Aborted = false)
{
    public bool AnyKilled => Hits.Any(static h => h.Killed);
}

/// <param name="Revived">P148：死亡後生成的 revive 怪物（呼叫端廣播 SPAWN_MONSTER，link = 死亡怪物 oid）。</param>
public sealed record CombatMobKillResult(int ObjectId, int MonsterId, byte Animation, bool Killed, IReadOnlyList<Mob>? Revived = null);

/// <summary>戰鬥用例：建立場上怪物、套用攻擊傷害、處理怪物死亡生命週期。</summary>
public sealed class CombatService
{
    public const int DefaultMobObjectIdBase = 100_000;
    public const int HeavensHammerSkillId = 1221011;

    private readonly MapService _maps;
    private readonly IMobKillHandler? _mobKillHandler;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly PlayerLocalStatsCalculator? _localStats;
    private readonly OnHitStatusApplier? _onHitStatuses;
    private readonly ISkillCatalog? _skills;

    public CombatService(
        MapService maps,
        IMobKillHandler? mobKillHandler = null,
        TimeProvider? timeProvider = null,
        Random? random = null,
        PlayerLocalStatsCalculator? localStats = null,
        ISkillCatalog? skills = null)
    {
        _localStats = localStats;
        _skills = skills;
        _onHitStatuses = skills is null ? null : new OnHitStatusApplier(skills, random ?? Random.Shared);
        _maps = maps;
        _mobKillHandler = mobKillHandler;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _random = random ?? Random.Shared;
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
        // P137：魔法攻擊的伺服器端傷害上限（需玩家總屬性；未注入計算器時沿用客戶端值）。
        var localStats = _localStats?.Calculate(attacker);
        PlayerLocalStats? magicStats = attack.IsMagic ? localStats : null;
        var magicBaseMax = magicStats is null
            ? 0
            : MagicDamageCap.BaseMaxDamagePerHit(attack.SkillId, attack.SkillEffect, magicStats, attacker.Character.Stats.Int);
        // P138：射程檢查（Java 在逐行傷害之後、monster.damage 之前；王圖豁免，物理攻擊另豁免傳送門 2 秒內）。
        var rangeExempt = localStats is null
            || attack.FromSummon
            || AttackRange.IsBossMap(attacker.Character.MapId)
            || (!attack.IsMagic && attacker.IsUsingPortal(_timeProvider.GetUtcNow()));
        var maxRangeSq = rangeExempt
            ? double.MaxValue
            : AttackRange.MaxDistanceSq(attacker, attack.SkillEffect, attack.SkillId, localStats!.DefRange);
        long cumulativeDamage = 0;
        foreach (var target in attack.Targets)
        {
            if (field.Get(target.ObjectId) is not Mob mob || !mob.IsAlive)
            {
                continue;
            }

            var now = _timeProvider.GetUtcNow();
            long totalDamage;
            if (magicStats is not null)
            {
                if (AdjustMagicDamage(attacker, mob, attack, target, magicStats, magicBaseMax) is not { } magicTotal)
                {
                    return new CombatAttackResult(hits, Aborted: true);
                }

                totalDamage = magicTotal;
            }
            else
            {
                totalDamage = AdjustedTotalDamage(mob.Stats, attack, target);
            }

            // Java totDamage += totDamageToOneMonster 在射程檢查之前。
            cumulativeDamage += totalDamage;
            if (AttackRange.DistanceSq(attacker.Position, mob.Position) > maxRangeSq)
            {
                return new CombatAttackResult(hits, Aborted: true);
            }

            // P146：Java applyAttackMagic——群體治癒打到非不死系怪物 → return（整次攻擊中止）。
            if (attack.IsMagic && attack.SkillId == HealSkillId && !mob.Stats.Undead)
            {
                return new CombatAttackResult(hits, Aborted: true);
            }

            if (totalDamage > 0)
            {
                mob.RecordAttacker(attacker.Character.Id, now);
            }

            // P144：Java applyAttack 射程檢查之後、扣血之前——偷錢 buff 下普攻與指定技能逐行掉楓幣。
            var pickPocketDrops = !attack.IsMagic && !attack.FromSummon ? PickPocket(field, attacker, mob, attack, target) : null;

            // P124：Java DamageParse.applyAttack——鬼神之擊（1221011）打王固定 199999，其餘留 1 HP。
            var requested = attack.SkillId == HeavensHammerSkillId && totalDamage > 0
                ? (mob.Stats.Boss ? 199_999 : Math.Max(0, mob.Hp - 1))
                : totalDamage;
            mob.AddAttackerDamage(attacker.Character.Id, requested);
            var result = mob.TakeDamage(requested);
            // P108：對照 Java MapleMonster.damage case -1/2（存活時）→ from.mulungEnergyModify(true)。
            short? mulungEnergyAfter = result.AppliedDamage > 0 && !result.Killed && mob.Stats.HpDisplayType is -1 or 2
                ? attacker.ModifyMulungEnergy(increase: true)
                : null;

            var appliedStatuses = result.AppliedDamage > 0 && !result.Killed
                ? CombineStatuses(
                    !attack.IsMagic && !attack.FromSummon ? _onHitStatuses?.Apply(mob, attacker, attack.SkillId, now) : null,
                    ApplySkillStatuses(mob, attack.SkillEffect, attacker, now))
                : null;

            // P143：Java applyAttackMagic——totDamageToOneMonster > 0 且有吸魔被動 → applyPassive（不看怪物是否存活）。
            var mpEaterSkillId = attack.IsMagic && totalDamage > 0 ? ApplyMpEater(attacker, mob, now) : 0;

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
                mob.ControllerId,
                result.AppliedDamage > 0
                    ? new CombatMobHpDisplay(
                        mob.Stats.HpDisplayType,
                        result.RemainingHp,
                        mob.Stats.MaxHp,
                        mob.Stats.TagColor,
                        mob.Stats.TagBgColor,
                        mob.GetRecentAttackers(now))
                    : null,
                mulungEnergyAfter,
                appliedStatuses,
                ApplyReflect(mob, attacker, attack.IsMagic, totalDamage),
                mpEaterSkillId,
                pickPocketDrops,
                result.Killed ? SpawnRevives(field, mob, attacker.Character.Id) : null));

            if (totalDamage > 0 && !attack.IsMagic && !attack.FromSummon)
            {
                ApplyAttackerDrain(attacker, attack, mob, cumulativeDamage);
            }

            if (result.Killed)
            {
                field.Remove(mob.ObjectId);
                NotifySpawnPointKilled(field, mob);
            }
        }

        return new CombatAttackResult(hits);
    }

    /// <summary>
    /// P144：Java <c>applyAttack</c>：PICKPOCKET buff 存在且技能為普攻 0、4001334、4201005、4211002、4211004、4221003、
    /// 4221007 → <c>handlePickPocket</c>（maxmeso = buff 值、機率取偷錢 4211003 在目前等級的 prop）。
    /// </summary>
    private IReadOnlyList<MapDrop>? PickPocket(FieldInstance field, Player attacker, Mob mob, CombatAttack attack, CombatAttackTarget target)
    {
        if (_mobKillHandler is null
            || attacker.GetBuffValue(MapleBuffStat.PICKPOCKET) is not { } maxMeso
            || attack.SkillId is not (0 or 4001334 or 4201005 or 4211002 or 4211004 or 4221003 or 4221007))
        {
            return null;
        }

        var prop = _skills?.GetSkill(PickPocketSkillId)?.GetEffect(attacker.GetSkillLevel(PickPocketSkillId))?.Prop ?? 100;
        var drops = _mobKillHandler.OnPickPocket(field, attacker, mob, target.DamageLines, maxMeso, prop);
        return drops.Count > 0 ? drops : null;
    }

    public const int PickPocketSkillId = 4211003;

    public const int HealSkillId = 2301002;

    /// <summary>
    /// P143：Java <c>MapleStatEffect.applyPassive</c>（魔力吸收 2100000／2200000／2300000，依 <c>getMPEaterForJob</c>：
    /// 210~212 火毒、220~222 冰雷、230~232 僧侶，其他職業預設 2100000）：擲機率 → 非王 →
    /// <c>absorb = min((int)(怪物最大 MP * (x / 70.0)), 目前 MP)</c> &gt; 0 且 10 秒內未被吸 → 怪物扣 MP、攻擊者加 MP
    /// （<c>setMp</c> 夾在目前最大 MP、不送封包，MP 隨尾段 applyTo 的屬性更新送出）。回觸發的技能 ID，未觸發回 0。
    /// </summary>
    private int ApplyMpEater(Player attacker, Mob mob, DateTimeOffset now)
    {
        var eaterId = attacker.Character.Job switch
        {
            220 or 221 or 222 => 2200000,
            230 or 231 or 232 => 2300000,
            _ => 2100000,
        };
        var level = attacker.GetSkillLevel(eaterId);
        if (level <= 0 || _skills?.GetSkill(eaterId)?.GetEffect(level) is not { } eater || !MakeChanceResult(eater.Prop))
        {
            return 0;
        }

        if (mob.Stats.Boss)
        {
            return 0;
        }

        var absorb = Math.Min((int)(mob.Stats.MaxMp * (eater.X / 70.0)), mob.Mp);
        if (absorb <= 0 || !mob.CanAbsorbMp(now))
        {
            return 0;
        }

        mob.AbsorbMp(absorb, now);
        attacker.Character.Stats.Mp = (short)Math.Min(attacker.MaxMp, attacker.Character.Stats.Mp + absorb);
        return eaterId;
    }

    /// <summary>P142：buff 衍生異常（先）與技能 monsterStatus（後）合併；皆無回 null。</summary>
    private static IReadOnlyList<MonsterStatusEffect>? CombineStatuses(
        IReadOnlyList<MonsterStatusEffect>? onHit,
        IReadOnlyList<MonsterStatusEffect>? skill)
        => (onHit is { Count: > 0 }, skill is { Count: > 0 }) switch
        {
            (true, true) => [.. onHit!, .. skill!],
            (true, false) => onHit,
            (false, true) => skill,
            _ => null,
        };

    /// <summary>
    /// P141：對照 Java <c>applyAttack</c> 扣血後的攻擊者 HP 效果（<c>setHp(..., silent)</c>，HP 封包隨尾段 applyTo 送出）。
    /// <c>totDamage</c> 是到這隻怪為止的累計總傷（照抄）；上限用基礎最大 HP（Java <c>getMaxHp</c>）/ 2：
    /// 連擊吸血 COMBO_DRAIN（buff 值 = 技能 x）、吸血術 4101005／吸血 14101006／損人利己 5111004（技能 x）回
    /// <c>min(怪物最大 HP, min((int)(totDamage * x / 100), maxHp / 2))</c>；龍之獻祭 1311005 扣
    /// <c>totDamage * x / 100</c>，最低留 1。
    /// </summary>
    private static void ApplyAttackerDrain(Player attacker, CombatAttack attack, Mob mob, long totDamage)
    {
        var stats = attacker.Character.Stats;
        int Drain(int x) => (int)Math.Min(mob.Stats.MaxHp, Math.Min((int)(totDamage * (double)x / 100.0), stats.MaxHp / 2));

        if (attacker.GetBuffValue(MapleBuffStat.COMBO_DRAIN) is { } comboDrain)
        {
            stats.Hp = (short)Math.Clamp(stats.Hp + Drain(comboDrain), 0, attacker.MaxHp);
        }

        switch (attack.SkillId)
        {
            case 4101005 or 14101006 or 5111004:
                stats.Hp = (short)Math.Clamp(stats.Hp + Drain(attack.SkillEffect?.X ?? 0), 0, attacker.MaxHp);
                break;
            case 5211006 or 5220011:
                // P159：Java 指定攻擊／精準砲擊 → setLinkMid(monster.getObjectId())（多目標時留最後一隻）。
                attacker.LinkMobObjectId = mob.ObjectId;
                break;
            case 1311005:
                var remaining = stats.Hp - totDamage * (attack.SkillEffect?.X ?? 0) / 100;
                stats.Hp = (short)Math.Clamp(remaining > 1 ? remaining : 1, 1, attacker.MaxHp);
                break;
        }
    }

    /// <summary>
    /// P137：Java applyAttackMagic 的逐行改寫加總；任何一行要求中止回 null。屬性：ELEMENT_RESET buff 下為無屬性，
    /// 否則取技能屬性。負值行不計（同 <see cref="CombatAttackTarget.TotalDamage"/>）。
    /// </summary>
    private static long? AdjustMagicDamage(
        Player attacker,
        Mob mob,
        CombatAttack attack,
        CombatAttackTarget target,
        PlayerLocalStats stats,
        double baseMax)
    {
        var element = attacker.GetBuffValue(MapleBuffStat.ELEMENT_RESET) is not null
            ? Element.Neutral
            : attack.SkillEffect?.SourceElement ?? Element.Neutral;
        var maxPerHit = MagicDamageCap.MaxDamagePerHit(attacker, attack.SkillId, mob, stats, element, baseMax);
        long total = 0;
        foreach (var line in target.DamageLines)
        {
            if (MagicDamageCap.AdjustLine(line, attacker, attack.SkillId, attack.SkillEffect, mob, baseMax, maxPerHit) is not { } adjusted)
            {
                return null;
            }

            total += Math.Max(0, adjusted);
        }

        return total;
    }

    /// <summary>
    /// P135：對照 Java <c>applyAttack:270</c>／<c>applyAttackMagic:681</c> 的逐行改寫（在 GM 豁免與傷害上限之前）：
    /// 怪物有 fixedDamage → 每一行（含 0 的 miss 行）都改成固定值，但 onlyNormalAttack 時技能攻擊（魔法一律視為技能）為 0；
    /// 無 fixedDamage 且 onlyNormalAttack → 技能攻擊 0、普攻保留客戶端值（Java 再以 maxDamagePerHit 夾住，待玩家總屬性系列）。
    /// </summary>
    public static long AdjustedTotalDamage(MobStats stats, CombatAttack attack, CombatAttackTarget target)
    {
        var skillAttack = attack.IsMagic || attack.SkillId != 0;
        if (stats.FixedDamage != -1)
        {
            return stats.OnlyNormalAttack && skillAttack ? 0 : (long)stats.FixedDamage * target.DamageLines.Count;
        }

        if (stats.OnlyNormalAttack && skillAttack)
        {
            return 0;
        }

        return target.TotalDamage;
    }

    /// <summary>
    /// P111：對照 Java <c>DamageParse.applyAttack</c>／<c>applyAttackMagic</c> 尾段——命中且怪物仍存活時，
    /// 技能有 monsterStatus 且 <c>makeChanceResult</c> 成功（每隻怪各擲一次）就逐一 <c>applyStatus</c>。
    /// 持續時間＝技能 duration（≥ 2e9 改 5000，同 Java）；毒／影網數值見 P113，忍者伏擊暫不套用。
    /// </summary>
    private IReadOnlyList<MonsterStatusEffect>? ApplySkillStatuses(Mob mob, MapleStatEffect? effect, Player attacker, DateTimeOffset now)
    {
        if (effect is null || effect.MonsterStatuses.Count == 0 || !MakeChanceResult(effect.Prop))
        {
            return null;
        }

        var duration = effect.DurationMilliseconds >= 2_000_000_000 ? 5000 : Math.Max(0, effect.DurationMilliseconds);
        var applied = new List<MonsterStatusEffect>();
        foreach (var (status, value) in effect.MonsterStatuses.OrderBy(static p => (int)p.Key))
        {
            // 忍者伏擊需玩家最大基礎傷害（Java getCurrentMaxBaseDamage），待玩家屬性重算系列。
            if (status == MonsterStatus.NinjaAmbush)
            {
                continue;
            }

            var statusValue = value;
            var poisonDamage = 0;
            var poison = effect.IsPoison;
            if (status == MonsterStatus.Poison && poison)
            {
                // P113：Java `poison && getHp() > 1` 計算毒傷；hp <= 1 時整個狀態不套用。
                if (mob.Hp <= 1)
                {
                    continue;
                }

                statusValue = MonsterPoisonCalculator.PoisonDamage(
                    mob.Stats.MaxHp,
                    attacker,
                    attacker.GetSkillLevel(effect.SourceId),
                    _random);
                poisonDamage = statusValue;
            }
            else if (effect.SourceId is 4111003 or 14111001)
            {
                statusValue = MonsterPoisonCalculator.ShadowWebValue(mob.Stats.MaxHp);
                poisonDamage = statusValue;
            }

            var statusEffect = new MonsterStatusEffect(
                status,
                statusValue,
                effect.SourceId,
                null,
                now.AddMilliseconds(duration),
                poisonDamage,
                attacker.Character.Id);
            if (mob.TryApplyStatus(statusEffect, sourceElement: effect.SourceElement) == MonsterStatusApplyResult.Applied)
            {
                applied.Add(statusEffect);
            }
        }

        return applied.Count > 0 ? applied : null;
    }

    /// <summary>
    /// P113：持續傷害一跳（世界 tick 每秒呼叫，對照 Java <c>applyEffectTask</c> → <c>damage(from, dam, false)</c>）。
    /// 不更新攻擊時間。Java 此路徑也會對 HPDisplayType -1/2 累加攻擊者武陵能量，MapleForge 暫不做（武陵怪中毒的邊角情況）。
    /// </summary>
    public IReadOnlyList<CombatMobHit> TickDamageOverTime(FieldInstance field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var now = _timeProvider.GetUtcNow();
        var hits = new List<CombatMobHit>();
        foreach (var mob in field.Objects.OfType<Mob>().ToArray())
        {
            // P150：Java applyEffectTask → damage(from, dam, false)——毒傷也計入經驗分配（在扣血前取 HP 上限）。
            var hpBeforeTick = mob.Hp;
            foreach (var (effect, damage) in mob.TickDamageOverTime())
            {
                if (effect.FromCharacterId != 0)
                {
                    mob.RecordDotDamage(effect.FromCharacterId, Math.Min(damage, hpBeforeTick));
                }

                hits.Add(new CombatMobHit(
                    mob.ObjectId,
                    mob.Definition.MonsterId,
                    damage,
                    damage,
                    mob.Hp,
                    Killed: false,
                    ControllerId: mob.ControllerId,
                    HpDisplay: new CombatMobHpDisplay(
                        mob.Stats.HpDisplayType,
                        mob.Hp,
                        mob.Stats.MaxHp,
                        mob.Stats.TagColor,
                        mob.Stats.TagBgColor,
                        mob.GetRecentAttackers(now))));
            }
        }

        return hits;
    }

    /// <summary>
    /// P124：對照 Java <c>applyAttack:370</c>／<c>applyAttackMagic:743</c>：本隻怪造成傷害（totDamageToOneMonster &gt; 0）後，
    /// 怪物仍帶 WEAPON/MAGIC_DAMAGE_REFLECT（死亡也一樣，Java 不清狀態）→ 攻擊者扣 7000 + nextInt(8000)。
    /// </summary>
    private int ApplyReflect(Mob mob, Player attacker, bool isMagic, long totalDamage)
    {
        if (totalDamage <= 0)
        {
            return 0;
        }

        var reflect = isMagic ? MonsterStatus.MagicDamageReflect : MonsterStatus.WeaponDamageReflect;
        if (!mob.Statuses.ContainsKey(reflect))
        {
            return 0;
        }

        return attacker.TakeDamage(7000 + _random.Next(8000));
    }

    /// <summary>Java <c>MapleStatEffect.makeChanceResult</c>：<c>prop == 100 || nextInt(99) &lt; prop</c>。</summary>
    private bool MakeChanceResult(int prop) => prop == 100 || _random.Next(99) < prop;

    /// <summary>Kill a mob without EXP/drop rewards. Used by Java MonsterBomb self-destruction path.</summary>
    public CombatMobKillResult KillMobWithoutRewards(FieldInstance field, int mobObjectId, byte animation, int controllerId = 0)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.Get(mobObjectId) is not Mob mob || !mob.IsAlive)
        {
            return new CombatMobKillResult(mobObjectId, 0, animation, Killed: false);
        }

        field.Remove(mob.ObjectId);
        NotifySpawnPointKilled(field, mob);
        return new CombatMobKillResult(mob.ObjectId, mob.Definition.MonsterId, animation, Killed: true, SpawnRevives(field, mob, controllerId));
    }

    /// <summary>
    /// P149：對照 Java <c>MapleMap.checkRemoveAfter</c> → 私有 <c>killMonster(monster)</c>：到期怪物 HP 歸 0、spawnRevives、
    /// 廣播 killMonster(oid, 1)、移出地圖；不給經驗／掉寶，也不呼叫重生點 listener（照抄）。revive 控制者給地圖第一位玩家。
    /// 世界 tick 每秒呼叫（第一次看到才起算，最多晚 1 秒）。呼叫端持有 field 鎖。
    /// </summary>
    public IReadOnlyList<CombatMobKillResult> ExpireRemoveAfter(FieldInstance field, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);

        List<CombatMobKillResult>? expired = null;
        foreach (var mob in field.Objects.OfType<Mob>().ToArray())
        {
            if (!mob.IsAlive || !mob.CheckRemoveAfter(now))
            {
                continue;
            }

            var controllerId = field.Players.FirstOrDefault()?.Character.Id ?? 0;
            var revived = SpawnRevives(field, mob, controllerId);
            field.Remove(mob.ObjectId);
            (expired ??= []).Add(new CombatMobKillResult(mob.ObjectId, mob.Definition.MonsterId, Animation: 1, Killed: true, revived));
        }

        return expired ?? [];
    }

    /// <summary>
    /// P148：對照 Java <c>MapleMonster.spawnRevives</c> 的 default 分支 → <c>map.spawnRevives(mob, oid)</c>：在死亡位置依序生成
    /// <c>info/revive</c> 的怪物（link = 死亡怪物 oid），控制者交給 <paramref name="controllerId"/>（Java updateMonsterController）。
    /// 暗黑龍王／皮卡啾的海綿怪分支（8810118~8810121、8810026、8810130、8820008~8820013）尚未移植，不生成。
    /// 無 revive 回 null。呼叫端持有 field 鎖。
    /// </summary>
    public IReadOnlyList<Mob>? SpawnRevives(FieldInstance field, Mob dead, int controllerId)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(dead);

        if (dead.Stats.Revives is not { Count: > 0 } revives
            || dead.Definition.MonsterId is >= 8810118 and <= 8810121 or 8810026 or 8810130 or >= 8820008 and <= 8820013)
        {
            return null;
        }

        var spawned = new List<Mob>(revives.Count);
        foreach (var monsterId in revives)
        {
            if (_maps.LoadMobStats(monsterId) is not { } stats)
            {
                continue;
            }

            var mob = new Mob(
                new MapMonster
                {
                    MonsterId = monsterId,
                    X = dead.Position.X,
                    Y = dead.Position.Y,
                    Cy = dead.Position.Y,
                    Fh = dead.Position.Foothold,
                    Team = -1,
                },
                stats,
                AllocateNextMobObjectId(field))
            {
                ControllerId = controllerId,
                LinkObjectId = dead.ObjectId,
            };
            field.Add(mob);
            spawned.Add(mob);
        }

        return spawned.Count > 0 ? spawned : null;
    }

    /// <summary>
    /// P066（M4-2 世界 tick，怪物重生第三步）：對照 Java <c>MonsterListener.monsterKilled</c>——
    /// 怪物死亡（不論是一般攻擊擊殺還是 <see cref="KillMobWithoutRewards"/> 的自爆路徑，Java
    /// 兩者都會觸發同一個 listener）要通知它出生的重生點重算下次可重生時間、歸還並發額度。
    /// 用 <c>Mob.Definition</c> 跟 <c>MobSpawnPoint.Definition</c> 的參照相等比對找出對應重生點
    /// ——兩者本來就共用同一個 <see cref="Maple.Core.Maps.MapMonster"/> 物件實例（見
    /// <see cref="SpawnMapMonsters"/>），不需要另外幫 <see cref="Mob"/> 加欄位。找不到對應重生點
    /// （例如未來其他生怪路徑，如玩家召喚怪，本來就不該有重生點）靜默略過。
    /// </summary>
    private void NotifySpawnPointKilled(FieldInstance field, Mob mob)
    {
        foreach (var point in field.SpawnPoints)
        {
            if (ReferenceEquals(point.Definition, mob.Definition))
            {
                point.OnMonsterKilled(_timeProvider.GetUtcNow());
                return;
            }
        }
    }
}
