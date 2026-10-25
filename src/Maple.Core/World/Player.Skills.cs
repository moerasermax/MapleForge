using Maple.Core.Skills;

namespace Maple.Core.World;

public sealed partial class Player
{
    private const int AranComboMaximum = 30_000;
    private static readonly TimeSpan AranComboTimeout = TimeSpan.FromMilliseconds(4_000);

    private readonly object _skillsGate = new();
    private readonly Dictionary<MapleBuffStat, ActiveBuffStat> _activeBuffs = new();
    private readonly Dictionary<int, ActiveSkillCooldown> _skillCooldowns = new();

    public int AranComboCount { get; private set; }

    public DateTimeOffset? LastAranComboAt { get; private set; }

    public IReadOnlyList<CharacterSkillRecord> Skills => Character.Skills;

    public IReadOnlyList<ActiveBuffStat> ActiveBuffs
    {
        get
        {
            lock (_skillsGate)
            {
                return _activeBuffs.Values.ToArray();
            }
        }
    }

    /// <summary>目前生效中的 buff 數值（Java <c>getBuffedValue</c>）；沒有則 null。</summary>
    public int? GetBuffValue(MapleBuffStat stat)
    {
        lock (_skillsGate)
        {
            return _activeBuffs.TryGetValue(stat, out var buff) ? buff.Value : null;
        }
    }

    /// <summary>P158：Java <c>keyDownSkill_Time</c>——蓄力技能開始按住的時間；取消時清為 null（Java 設 0）。</summary>
    public DateTimeOffset? KeyDownSkillStartedAt { get; set; }

    /// <summary>P159：Java <c>linkMid</c>——指定攻擊／精準砲擊最後命中的怪物 objectId（0 = 無；Java 從不清除）。</summary>
    public int LinkMobObjectId { get; set; }

    public int GetSkillLevel(int skillId)
    {
        var skill = Character.Skills.FirstOrDefault(s => s.SkillId == skillId);
        return skill?.Level ?? 0;
    }

    public int GetMasterLevel(int skillId)
    {
        var skill = Character.Skills.FirstOrDefault(s => s.SkillId == skillId);
        return skill?.MasterLevel ?? 0;
    }

    public void ChangeSkillLevel(int skillId, byte level, byte masterLevel, long expiration = -1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skillId);

        var skill = Character.Skills.FirstOrDefault(s => s.SkillId == skillId);
        if (skill is null)
        {
            Character.Skills.Add(new CharacterSkillRecord
            {
                SkillId = skillId,
                Level = level,
                MasterLevel = masterLevel,
                Expiration = expiration,
            });
            return;
        }

        skill.Level = level;
        skill.MasterLevel = masterLevel;
        skill.Expiration = expiration;
    }

    public bool SkillIsCooling(int skillId, DateTimeOffset now)
    {
        lock (_skillsGate)
        {
            if (!_skillCooldowns.TryGetValue(skillId, out var cooldown))
            {
                return false;
            }

            if (cooldown.ExpiresAt > now)
            {
                return true;
            }

            _skillCooldowns.Remove(skillId);
            return false;
        }
    }

    /// <summary>
    /// 移除所有已到期的技能冷卻並回傳其技能 ID（對照 Java <c>World.handleCooldowns</c>：
    /// <c>startTime + length &lt; now</c> 即 <c>removeCooldown</c>）。
    /// </summary>
    public IReadOnlyList<int> RemoveExpiredSkillCooldowns(DateTimeOffset now)
    {
        lock (_skillsGate)
        {
            if (_skillCooldowns.Count == 0)
            {
                return Array.Empty<int>();
            }

            var expired = _skillCooldowns.Values
                .Where(c => c.ExpiresAt < now)
                .Select(static c => c.SkillId)
                .ToArray();
            foreach (var skillId in expired)
            {
                _skillCooldowns.Remove(skillId);
            }

            return expired;
        }
    }

    /// <summary>P086：移除除了 <paramref name="keepSkillId"/> 以外的所有技能冷卻並回傳其技能 ID
    /// （對照 Java <c>MapleStatEffect.applyTo</c> 的 <c>isTimeLeap()</c> 分支）。</summary>
    public IReadOnlyList<int> ResetSkillCooldownsExcept(int keepSkillId)
    {
        lock (_skillsGate)
        {
            var reset = _skillCooldowns.Keys.Where(id => id != keepSkillId).ToArray();
            foreach (var skillId in reset)
            {
                _skillCooldowns.Remove(skillId);
            }

            return reset;
        }
    }

    public void AddSkillCooldown(int skillId, DateTimeOffset now, int seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        lock (_skillsGate)
        {
            _skillCooldowns[skillId] = new ActiveSkillCooldown(skillId, now, seconds * 1000);
        }
    }

    public PlayerSkillApplication ApplySkillEffect(MapleStatEffect effect, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.HpCon > 0 && Character.Stats.Hp <= effect.HpCon)
        {
            return new PlayerSkillApplication(PlayerSkillApplicationStatus.NotEnoughHp, null);
        }

        if (effect.MpCon > 0 && Character.Stats.Mp < effect.MpCon)
        {
            return new PlayerSkillApplication(PlayerSkillApplicationStatus.NotEnoughMp, null);
        }

        if (effect.HpCon > 0)
        {
            Character.Stats.Hp = (short)Math.Max(1, Character.Stats.Hp - effect.HpCon);
        }

        UseMp(effect.MpCon);
        ApplyImmediateHpMp(effect);
        return new PlayerSkillApplication(PlayerSkillApplicationStatus.Applied, ApplyBuffStats(effect, now));
    }

    /// <summary>
    /// P134：只套用 buff 部分（Java <c>applyBuffEffect</c> 的 statups 登記）；HP/MP 結算由呼叫端負責
    /// （技能走 <c>SkillService</c> 的 Java calcHPChange/calcMPChange）。非 overTime 或無 statups 回 null。
    /// </summary>
    /// <param name="statupsOverride">P156：以這組 statups 取代效果本身的（Java applyBuffEffect 的 Selfstat，例如變身）。</param>
    /// <param name="durationOverride">P157：取代效果持續時間（Java 騎寵 localDuration = 2100000000）。</param>
    public PlayerBuffChange? ApplyBuffStats(
        MapleStatEffect effect,
        DateTimeOffset now,
        IReadOnlyList<BuffStatValue>? statupsOverride = null,
        int? durationOverride = null)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var duration = durationOverride ?? effect.DurationMilliseconds;
        var source = statupsOverride ?? effect.Statups;
        if (!effect.IsOverTime || source.Count == 0)
        {
            return null;
        }

        var stats = source.ToArray();
        lock (_skillsGate)
        {
            foreach (var stat in stats)
            {
                _activeBuffs[stat.Stat] = new ActiveBuffStat(
                    stat.Stat,
                    stat.Value,
                    effect.SourceId,
                    effect.Level,
                    now,
                    duration);
            }
        }

        return new PlayerBuffChange(effect.SourceId, duration, now, stats);
    }

    public int AddAranCombo(int amount, DateTimeOffset now)
    {
        if (amount <= 0)
        {
            return AranComboCount;
        }

        lock (_skillsGate)
        {
            if (AranComboCount > 0 &&
                LastAranComboAt is { } last &&
                now - last > AranComboTimeout)
            {
                AranComboCount = 0;
            }

            AranComboCount = Math.Min(AranComboMaximum, AranComboCount + amount);
            LastAranComboAt = now;
            return AranComboCount;
        }
    }

    public PlayerBuffChange ApplyAranComboBuff(int sourceId, byte skillLevel, int combo, int durationMilliseconds, DateTimeOffset now)
    {
        var stats = new[] { new BuffStatValue(MapleBuffStat.ARAN_COMBO, combo) };
        lock (_skillsGate)
        {
            _activeBuffs[MapleBuffStat.ARAN_COMBO] = new ActiveBuffStat(
                MapleBuffStat.ARAN_COMBO,
                combo,
                sourceId,
                skillLevel,
                now,
                durationMilliseconds);
        }

        return new PlayerBuffChange(sourceId, durationMilliseconds, now, stats);
    }

    public IReadOnlyList<PlayerBuffCancellation> CancelBuffBySource(int sourceId)
    {
        lock (_skillsGate)
        {
            var stats = _activeBuffs.Values
                .Where(b => b.SourceId == sourceId)
                .Select(b => b.Stat)
                .Distinct()
                .ToArray();

            foreach (var stat in stats)
            {
                _activeBuffs.Remove(stat);
            }

            return stats.Length == 0
                ? Array.Empty<PlayerBuffCancellation>()
                : new[] { new PlayerBuffCancellation(sourceId, stats) };
        }
    }

    /// <summary>
    /// P129：對照 Java <c>MapleCharacter.dispel</c>（怪物技能 127）：取消所有「技能來源、有到期排程、非變身、非能量蓄積」的 buff。
    /// MapleForge 以 SourceId &gt; 0 視為技能、ExpiresAt 非空視為有排程、同來源含 MORPH 視為變身。
    /// </summary>
    public IReadOnlyList<PlayerBuffCancellation> DispelSkillBuffs()
    {
        lock (_skillsGate)
        {
            var groups = _activeBuffs.Values
                .GroupBy(static b => b.SourceId)
                .Where(g => g.Key > 0
                    && g.Key is not (5110001 or 15100004)
                    && g.Any(static b => b.ExpiresAt is not null)
                    && g.All(static b => b.Stat != MapleBuffStat.MORPH))
                .Select(g => new PlayerBuffCancellation(g.Key, g.Select(static b => b.Stat).Distinct().ToArray()))
                .ToArray();

            foreach (var stat in groups.SelectMany(static g => g.Stats))
            {
                _activeBuffs.Remove(stat);
            }

            return groups;
        }
    }

    public IReadOnlyList<PlayerBuffCancellation> CancelExpiredBuffs(DateTimeOffset now)
    {
        lock (_skillsGate)
        {
            var expired = _activeBuffs.Values
                .Where(b => b.ExpiresAt is { } expiresAt && expiresAt <= now)
                .GroupBy(b => b.SourceId)
                .Select(g => new PlayerBuffCancellation(g.Key, g.Select(b => b.Stat).Distinct().ToArray()))
                .ToArray();

            foreach (var cancellation in expired)
            {
                foreach (var stat in cancellation.Stats)
                {
                    _activeBuffs.Remove(stat);
                }
            }

            return expired;
        }
    }

    private void ApplyImmediateHpMp(MapleStatEffect effect)
    {
        if (effect.Hp > 0)
        {
            HealHp(effect.Hp);
        }
        else if (effect.Hp < 0)
        {
            TakeDamage(-effect.Hp);
        }

        if (effect.Mp > 0)
        {
            Character.Stats.Mp = (short)Math.Min(MaxMp, Character.Stats.Mp + effect.Mp);
        }
        else if (effect.Mp < 0)
        {
            UseMp(-effect.Mp);
        }

        if (effect.HpRate != 0)
        {
            var delta = (int)Math.Round(MaxHp * effect.HpRate);
            if (delta > 0) HealHp(delta);
            else if (delta < 0) TakeDamage(-delta);
        }

        if (effect.MpRate != 0)
        {
            var delta = (int)Math.Round(MaxMp * effect.MpRate);
            if (delta > 0)
            {
                Character.Stats.Mp = (short)Math.Min(MaxMp, Character.Stats.Mp + delta);
            }
            else if (delta < 0)
            {
                UseMp(-delta);
            }
        }
    }
}
