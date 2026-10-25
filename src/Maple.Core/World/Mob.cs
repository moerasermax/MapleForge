using Maple.Core.Maps;

namespace Maple.Core.World;

/// <summary>怪物模板數值（從 Mob.wz info 載入；Core 不知道 WZ 格式）。</summary>
public sealed record MobStats(
    int MonsterId,
    long MaxHp,
    int MaxMp,
    short Level,
    int Exp,
    bool Boss = false,
    bool Mobile = false,
    bool Friendly = false,
    // P107：對照 Java MapleLifeFactory——tagColor>0→0（王血條）、friendly→1、武陵 9300184~9300215→2、
    // 非王或 9410066→3（攻擊者小血條），其餘（王但無 hpTagColor）→-1。
    sbyte HpDisplayType = 3,
    sbyte SelfDestructAnimation = -1,
    bool Fly = false,
    // P096：對照 Java MapleMonsterStats.isExplosiveReward（WZ info/explosiveReward）與 isFfaLoot（WZ info/publicReward）。
    bool ExplosiveReward = false,
    bool FfaLoot = false,
    // P107：王血條顏色（WZ info/hpTagColor、hpTagBgcolor；僅王且未隱藏血條時有值）。
    byte TagColor = 0,
    byte TagBgColor = 0,
    // P110：WZ info/noDoom（Java MapleMonsterStats.isNoDoom）。
    bool NoDoom = false,
    // P114：WZ info/elemAttr 解析後的屬性抗性（null = 全 Normal）。
    IReadOnlyDictionary<Element, ElementalEffectiveness>? Resistances = null,
    // P122：WZ info/skill（Java MapleMonsterStats.getSkills）。
    IReadOnlyList<MobSkillRef>? Skills = null,
    // P130：WZ info/ban（Java BanishInfo：banMsg、banMap/0/field、banMap/0/portal）。
    MobBanishInfo? Banish = null,
    // P135：WZ info/fixedDamage（Java getIntConvert 預設 -1：無固定傷害）、info/onlyNormalAttack（&gt; 0）。
    int FixedDamage = -1,
    bool OnlyNormalAttack = false,
    // P137：WZ info/eva、info/MDDamage（Java getEva、getMagicDefense；魔法傷害上限用）。
    int Eva = 0,
    int MagicDefense = 0)
{
    /// <summary>對照 Java <c>MapleMonsterStats.getEffectiveness</c>：查無即 Normal。</summary>
    public ElementalEffectiveness GetEffectiveness(Element element)
        => Resistances is not null && Resistances.TryGetValue(element, out var value) ? value : ElementalEffectiveness.Normal;
}

/// <summary>P130：怪物放逐目的地（Java <c>BanishInfo</c>）。</summary>
public sealed record MobBanishInfo(string Message, int MapId, string PortalName);

/// <summary>單次怪物傷害套用結果。</summary>
public sealed record MobDamageResult(
    int ObjectId,
    int MonsterId,
    long RequestedDamage,
    long AppliedDamage,
    long RemainingHp,
    bool Killed);

/// <summary>
/// 執行期怪物（地圖物件）：持有靜態出生點、模板數值、場上 objectId 與可變 HP/MP。
/// 對照舊 OdinMS <c>MapleMonster</c>，但只保留戰鬥 MVP 所需生命週期。
/// </summary>
public sealed class Mob : IFieldObject
{
    public const byte DefaultStance = 5;

    /// <summary>對照 Java <c>MapleMonster.damage</c> 的 HPDisplayType 3：最近 4 秒內攻擊過的玩家才收到血條。</summary>
    public static readonly TimeSpan RecentAttackerWindow = TimeSpan.FromSeconds(4);

    private readonly Dictionary<int, DateTimeOffset> _lastAttackTimes = new();
    private readonly Dictionary<MonsterStatus, MonsterStatusEffect> _statuses = new();
    private readonly Dictionary<int, DateTimeOffset> _skillUsedAt = new();

    public MapMonster Definition { get; }

    public MobStats Stats { get; }

    public int ObjectId { get; }

    public Position Position { get; private set; }

    public FieldObjectType Type => FieldObjectType.Monster;

    public long Hp { get; private set; }

    public int Mp { get; private set; }

    public bool IsAlive => Hp > 0;

    /// <summary>控制此怪物 AI/移動的玩家角色 Id（0 = 無人控制）。</summary>
    public int ControllerId { get; set; }

    public short OriginFoothold { get; }

    public sbyte CarnivalTeam => Definition.Team;

    public Mob(MapMonster definition, MobStats stats, int objectId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.MonsterId != definition.MonsterId)
        {
            throw new ArgumentException("MobStats.MonsterId must match MapMonster.MonsterId.", nameof(stats));
        }

        Definition = definition;
        Stats = stats;
        ObjectId = objectId;
        OriginFoothold = (short)definition.Fh;
        Position = new Position((short)definition.X, (short)definition.Y, DefaultStance, (short)definition.Fh);
        Hp = Math.Max(1, stats.MaxHp);
        Mp = Math.Max(0, stats.MaxMp);
    }

    /// <summary>套用客戶端攻擊傷害；扣血量會夾在 [0, CurrentHp]。</summary>
    public MobDamageResult TakeDamage(long damage)
    {
        if (damage <= 0 || !IsAlive)
        {
            return new MobDamageResult(ObjectId, Definition.MonsterId, damage, 0, Hp, false);
        }

        var applied = Math.Min(damage, Hp);
        Hp -= applied;
        return new MobDamageResult(ObjectId, Definition.MonsterId, damage, applied, Hp, Hp == 0);
    }

    /// <summary>
    /// 記錄攻擊者最後攻擊時間（對照 Java <c>MapleMonster.damage</c> 開頭的 <c>attackers</c> 登記，
    /// 只在怪物存活且傷害 &gt; 0 時登記）。
    /// </summary>
    public void RecordAttacker(int characterId, DateTimeOffset at)
    {
        _lastAttackTimes[characterId] = at;
    }

    /// <summary>最近 <see cref="RecentAttackerWindow"/> 內攻擊過的角色 Id（含邊界）。</summary>
    public IReadOnlyList<int> GetRecentAttackers(DateTimeOffset now)
        => _lastAttackTimes
            .Where(e => e.Value >= now - RecentAttackerWindow)
            .Select(static e => e.Key)
            .ToArray();

    /// <summary>生效中的異常狀態（P110）。</summary>
    public IReadOnlyDictionary<MonsterStatus, MonsterStatusEffect> Statuses => _statuses;

    /// <summary>
    /// 套用異常狀態（P110，對照 Java <c>MapleMonster.applyStatus</c> 的免疫判斷，依原順序）：
    /// 已死 → MobDead；noDoom 擋 Doom；9400253/9400254 擋 Freeze；王只接受 Speed/NinjaAmbush/Watk（Stun 明確擋）；
    /// 友方怪擋 Stun/Speed/Poison/VenomousWeapon；毒類需有來源技能效果；同狀態已在 → AlreadyActive。
    /// 屬性抗性（Java <c>getEffectiveness</c>）由呼叫端先判斷。
    /// </summary>
    /// <param name="sourceElement">P114：來源技能的屬性（Java <c>SkillFactory.getSkill(status.getSkill())</c> 存在時才檢查）；null 不檢查。</param>
    public MonsterStatusApplyResult TryApplyStatus(MonsterStatusEffect effect, bool hasSourceEffect = true, Element? sourceElement = null)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (!IsAlive)
        {
            return MonsterStatusApplyResult.MobDead;
        }

        if (sourceElement is { } element && IsElementallyResistant(element, effect.SkillId))
        {
            return MonsterStatusApplyResult.Immune;
        }

        if (IsImmuneTo(effect.Status, hasSourceEffect))
        {
            return MonsterStatusApplyResult.Immune;
        }

        if (_statuses.ContainsKey(effect.Status))
        {
            return MonsterStatusApplyResult.AlreadyActive;
        }

        _statuses[effect.Status] = effect;
        return MonsterStatusApplyResult.Applied;
    }

    /// <summary>解除異常狀態（對照 Java <c>cancelStatus</c>：Summon 不可解除、已死不處理）。回傳是否真的解除。</summary>
    public bool CancelStatus(MonsterStatus status)
    {
        if (status == MonsterStatus.Summon || !IsAlive)
        {
            return false;
        }

        return _statuses.Remove(status);
    }

    /// <summary>移除到期的異常狀態並回傳（供世界 tick 廣播解除）。</summary>
    public IReadOnlyList<MonsterStatusEffect> ExpireStatuses(DateTimeOffset now)
    {
        if (!IsAlive || _statuses.Count == 0)
        {
            return Array.Empty<MonsterStatusEffect>();
        }

        var expired = _statuses.Values
            .Where(e => e.Status != MonsterStatus.Summon && e.ShouldCancel(now))
            .ToArray();
        foreach (var effect in expired)
        {
            _statuses.Remove(effect.Status);
        }

        return expired;
    }

    /// <summary>
    /// P113：持續傷害一跳（對照 Java <c>applyEffectTask</c>：只有 Poison／NinjaAmbush 有排程，每秒一次；
    /// 傷害夾在 hp - 1，不會打死）。回傳每個造成傷害的狀態與實際扣血（依列舉序）。
    /// </summary>
    public IReadOnlyList<(MonsterStatusEffect Effect, long Damage)> TickDamageOverTime()
    {
        if (!IsAlive || _statuses.Count == 0)
        {
            return Array.Empty<(MonsterStatusEffect, long)>();
        }

        var ticks = new List<(MonsterStatusEffect, long)>();
        foreach (var effect in _statuses.Values.OrderBy(static e => (int)e.Status).ToArray())
        {
            if (effect.Status is not (MonsterStatus.Poison or MonsterStatus.NinjaAmbush) || effect.PoisonDamage == 0)
            {
                continue;
            }

            var damage = Math.Min(effect.PoisonDamage, Hp - 1);
            if (damage <= 0)
            {
                continue;
            }

            Hp -= damage;
            ticks.Add((effect, damage));
        }

        return ticks;
    }

    /// <summary>
    /// 對照 Java <c>applyStatus</c> 開頭：技能屬性對此怪 Immune/Strong → 擋；火毒合擊（2111006）另查毒、
    /// 冰雷合擊（2211006）另查冰（Immune/Strong 擋）；飛毒殺（4120005/4220005/14110004）毒屬性 Weak 反而擋（照 Java）。
    /// 讀模板抗性（Java 用 <c>stats.getEffectiveness</c>，不經 Doom 例外）。
    /// </summary>
    private bool IsElementallyResistant(Element element, int skillId)
    {
        if (Stats.GetEffectiveness(element) is ElementalEffectiveness.Immune or ElementalEffectiveness.Strong)
        {
            return true;
        }

        return skillId switch
        {
            2111006 => Stats.GetEffectiveness(Element.Poison) is ElementalEffectiveness.Immune or ElementalEffectiveness.Strong,
            2211006 => Stats.GetEffectiveness(Element.Ice) is ElementalEffectiveness.Immune or ElementalEffectiveness.Strong,
            4120005 or 4220005 or 14110004 => Stats.GetEffectiveness(Element.Poison) == ElementalEffectiveness.Weak,
            _ => false,
        };
    }

    private bool IsImmuneTo(MonsterStatus status, bool hasSourceEffect)
    {
        if (Stats.NoDoom && status == MonsterStatus.Doom)
        {
            return true;
        }

        if (status == MonsterStatus.Freeze && Definition.MonsterId is 9400253 or 9400254)
        {
            return true;
        }

        if (Stats.Boss && status is not (MonsterStatus.Speed or MonsterStatus.NinjaAmbush or MonsterStatus.Watk))
        {
            return true;
        }

        if (Stats.Friendly && status is MonsterStatus.Stun or MonsterStatus.Speed or MonsterStatus.Poison or MonsterStatus.VenomousWeapon)
        {
            return true;
        }

        return status is MonsterStatus.Poison or MonsterStatus.VenomousWeapon && !hasSourceEffect;
    }

    /// <summary>
    /// P123：怪物技能自身 buff（對照 Java <c>MapleMonster.applyMonsterBuff</c>）：不做免疫判斷；同狀態已存在先解除
    /// （呼叫端送 CANCEL），再寫入帶怪物技能來源的新狀態（skillId 0、到期 <paramref name="expiresAt"/>）。
    /// 回傳被先行解除的狀態與新套用的狀態（依列舉序）。
    /// </summary>
    public (IReadOnlyList<MonsterStatus> Cancelled, IReadOnlyList<MonsterStatusEffect> Applied) ApplyMonsterBuff(
        IReadOnlyDictionary<MonsterStatus, int> stats,
        MobSkillSource source,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(source);

        var cancelled = new List<MonsterStatus>();
        var applied = new List<MonsterStatusEffect>();
        foreach (var (status, value) in stats.OrderBy(static p => (int)p.Key))
        {
            if (_statuses.ContainsKey(status) && CancelStatus(status))
            {
                cancelled.Add(status);
            }

            var effect = new MonsterStatusEffect(status, value, 0, source, expiresAt);
            _statuses[status] = effect;
            applied.Add(effect);
        }

        return (cancelled, applied);
    }

    /// <summary>Java <c>isFacingLeft</c>：<c>stance % 2 != 0</c>。</summary>
    public bool IsFacingLeft => Position.Stance % 2 != 0;

    /// <summary>P122：Java <c>getLastSkillUsed</c>（存的是 Java <c>setLastSkillUsed</c> 寫入的時間，見該方法）。</summary>
    public DateTimeOffset? GetLastSkillUsed(int skillId)
        => _skillUsedAt.TryGetValue(skillId, out var at) ? at : null;

    /// <summary>
    /// P122：逐字對照 Java <c>MapleMonster.setLastSkillUsed</c>：一般技能存 now + cooltime（搭配呼叫端
    /// <c>now - ls &gt; cooltime</c> 判斷，實際冷卻為兩倍）；140/141 互相牽制（存 now + 2×cooltime，另一個存 now 或 now + cooltime）。
    /// </summary>
    public void SetLastSkillUsed(int skillId, DateTimeOffset now, int coolTimeMilliseconds)
    {
        switch (skillId)
        {
            case 140:
                _skillUsedAt[140] = now.AddMilliseconds(coolTimeMilliseconds * 2.0);
                _skillUsedAt[141] = now;
                break;
            case 141:
                _skillUsedAt[141] = now.AddMilliseconds(coolTimeMilliseconds * 2.0);
                _skillUsedAt[140] = now.AddMilliseconds(coolTimeMilliseconds);
                break;
            default:
                _skillUsedAt[skillId] = now.AddMilliseconds(coolTimeMilliseconds);
                break;
        }
    }

    /// <summary>P125：Java <c>MapleMonster.heal(hp, mp)</c>：加 HP/MP，夾在最大值（已死不處理）。</summary>
    public void Heal(long hp, int mp)
    {
        if (!IsAlive)
        {
            return;
        }

        Hp = Math.Min(Stats.MaxHp, Hp + hp);
        Mp = Math.Min(Stats.MaxMp, Mp + mp);
    }

    /// <summary>P122：扣怪物 MP（Java <c>setMp(getMp() - mpCon)</c>，負值夾 0）。</summary>
    public void ConsumeMp(int amount) => Mp = Math.Max(0, Mp - amount);

    /// <summary>P143：Java <c>lastAbsorbMP</c>（吸魔時間）。</summary>
    public DateTimeOffset? LastAbsorbMpAt { get; private set; }

    /// <summary>Java <c>canAbsorbMP</c>：距上次被吸超過 10 秒。</summary>
    public bool CanAbsorbMp(DateTimeOffset now) => LastAbsorbMpAt is not { } at || (now - at).TotalMilliseconds > 10_000;

    /// <summary>Java <c>absorbMP</c>：可吸時扣 MP（不足歸 0）並記錄時間。</summary>
    public void AbsorbMp(int amount, DateTimeOffset now)
    {
        if (!CanAbsorbMp(now))
        {
            return;
        }

        Mp = Math.Max(0, Mp - amount);
        LastAbsorbMpAt = now;
    }

    /// <summary>套用怪物移動最終位置（MONSTER_MOVE 後續接線用）。</summary>
    public void MoveTo(Position position) => Position = position;
}
