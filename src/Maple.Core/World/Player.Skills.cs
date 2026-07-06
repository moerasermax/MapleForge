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

        if (!effect.IsOverTime || effect.Statups.Count == 0)
        {
            return new PlayerSkillApplication(PlayerSkillApplicationStatus.Applied, null);
        }

        var stats = effect.Statups.ToArray();
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
                    effect.DurationMilliseconds);
            }
        }

        return new PlayerSkillApplication(
            PlayerSkillApplicationStatus.Applied,
            new PlayerBuffChange(effect.SourceId, effect.DurationMilliseconds, now, stats));
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
            Character.Stats.Mp = (short)Math.Min(Character.Stats.MaxMp, Character.Stats.Mp + effect.Mp);
        }
        else if (effect.Mp < 0)
        {
            UseMp(-effect.Mp);
        }

        if (effect.HpRate != 0)
        {
            var delta = (int)Math.Round(Character.Stats.MaxHp * effect.HpRate);
            if (delta > 0) HealHp(delta);
            else if (delta < 0) TakeDamage(-delta);
        }

        if (effect.MpRate != 0)
        {
            var delta = (int)Math.Round(Character.Stats.MaxMp * effect.MpRate);
            if (delta > 0)
            {
                Character.Stats.Mp = (short)Math.Min(Character.Stats.MaxMp, Character.Stats.Mp + delta);
            }
            else if (delta < 0)
            {
                UseMp(-delta);
            }
        }
    }
}
