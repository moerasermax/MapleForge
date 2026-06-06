using Maple.Core.Characters;

namespace Maple.Core.World;

public enum AbilityPointTarget
{
    Str,
    Dex,
    Int,
    Luk,
}

public enum PlayerStatKind
{
    Skin,
    Face,
    Hair,
    Level,
    Job,
    Str,
    Dex,
    Int,
    Luk,
    Hp,
    MaxHp,
    Mp,
    MaxMp,
    AvailableAp,
    AvailableSp,
    Exp,
    Fame,
    Meso,
    GachaponExp,
}

public enum PlayerStatsFailure
{
    None,
    NoChange,
    UnsupportedAbilityTarget,
    NotEnoughAbilityPoints,
    StatLimitReached,
    InvalidSkill,
    NotEnoughSkillPoints,
    SkillLevelLimit,
    MaxLevel,
}

public readonly record struct PlayerStatUpdate(PlayerStatKind Kind, int Value);

public sealed record PlayerStatsMutation(
    PlayerStatsFailure Failure,
    IReadOnlyList<PlayerStatUpdate> Updates,
    int? SkillId = null,
    byte? SkillLevel = null)
{
    public bool Applied => Failure == PlayerStatsFailure.None && (Updates.Count > 0 || SkillId is not null);

    public static PlayerStatsMutation Failed(PlayerStatsFailure failure)
        => new(failure, Array.Empty<PlayerStatUpdate>());
}

public sealed partial class Player
{
    private const short MaxBaseStat = 999;
    private const short MaxVitalStat = 30000;
    private const int MaxLevel = 250;
    private const int CygnusMaxLevel = 200;
    private const int RecoveryIntervalMilliseconds = 1000;
    private const int DefaultSkillMaxLevel = 30;
    private const int BeginnerSkillMaxLevel = 3;

    private long _lastHpRecoveryAtMs;
    private long _lastMpRecoveryAtMs;

    public PlayerStatsMutation DistributeAbilityPoint(AbilityPointTarget target)
    {
        if (Character.RemainingAp <= 0)
        {
            return PlayerStatsMutation.Failed(PlayerStatsFailure.NotEnoughAbilityPoints);
        }

        var stats = Character.Stats;
        PlayerStatUpdate changed;
        switch (target)
        {
            case AbilityPointTarget.Str:
                if (stats.Str >= MaxBaseStat) return PlayerStatsMutation.Failed(PlayerStatsFailure.StatLimitReached);
                stats.Str++;
                changed = new PlayerStatUpdate(PlayerStatKind.Str, stats.Str);
                break;
            case AbilityPointTarget.Dex:
                if (stats.Dex >= MaxBaseStat) return PlayerStatsMutation.Failed(PlayerStatsFailure.StatLimitReached);
                stats.Dex++;
                changed = new PlayerStatUpdate(PlayerStatKind.Dex, stats.Dex);
                break;
            case AbilityPointTarget.Int:
                if (stats.Int >= MaxBaseStat) return PlayerStatsMutation.Failed(PlayerStatsFailure.StatLimitReached);
                stats.Int++;
                changed = new PlayerStatUpdate(PlayerStatKind.Int, stats.Int);
                break;
            case AbilityPointTarget.Luk:
                if (stats.Luk >= MaxBaseStat) return PlayerStatsMutation.Failed(PlayerStatsFailure.StatLimitReached);
                stats.Luk++;
                changed = new PlayerStatUpdate(PlayerStatKind.Luk, stats.Luk);
                break;
            default:
                return PlayerStatsMutation.Failed(PlayerStatsFailure.UnsupportedAbilityTarget);
        }

        Character.RemainingAp--;
        return new PlayerStatsMutation(PlayerStatsFailure.None, new[]
        {
            changed,
            new PlayerStatUpdate(PlayerStatKind.AvailableAp, Character.RemainingAp),
        });
    }

    public PlayerStatsMutation DistributeSkillPoint(int skillId)
    {
        if (skillId <= 0)
        {
            return PlayerStatsMutation.Failed(PlayerStatsFailure.InvalidSkill);
        }

        var beginnerGroup = GetBeginnerSkillGroup(skillId);
        var isBeginnerSkill = beginnerGroup is not null;
        var currentLevel = GetSkillLevel(skillId);
        var maxLevel = isBeginnerSkill ? BeginnerSkillMaxLevel : DefaultSkillMaxLevel;
        if (currentLevel >= maxLevel)
        {
            return PlayerStatsMutation.Failed(PlayerStatsFailure.SkillLevelLimit);
        }

        var updates = new List<PlayerStatUpdate>(1);
        if (beginnerGroup is { } group)
        {
            var spent = GetSkillLevel(group.FirstSkillId) + GetSkillLevel(group.SecondSkillId) + GetSkillLevel(group.ThirdSkillId);
            var remaining = Math.Min(Character.Level - 1, group.TotalCap) - spent;
            if (remaining <= 0)
            {
                return PlayerStatsMutation.Failed(PlayerStatsFailure.NotEnoughSkillPoints);
            }
        }
        else
        {
            if (Character.RemainingSp <= 0)
            {
                return PlayerStatsMutation.Failed(PlayerStatsFailure.NotEnoughSkillPoints);
            }

            Character.RemainingSp--;
            updates.Add(new PlayerStatUpdate(PlayerStatKind.AvailableSp, Character.RemainingSp));
        }

        var skill = GetOrCreateSkill(skillId);
        skill.Level = (byte)(currentLevel + 1);
        if (skill.MasterLevel < skill.Level)
        {
            skill.MasterLevel = skill.Level;
        }

        return new PlayerStatsMutation(PlayerStatsFailure.None, updates, skillId, skill.Level);
    }

    public PlayerStatsMutation RecoverOverTime(int requestedHp, int requestedMp, long nowUnixMilliseconds)
    {
        if (!IsAlive)
        {
            return PlayerStatsMutation.Failed(PlayerStatsFailure.NoChange);
        }

        var updates = new List<PlayerStatUpdate>(2);
        if (requestedHp > 0 && CanRecoverHp(nowUnixMilliseconds))
        {
            var before = Hp;
            HealHp(requestedHp);
            if (Hp != before)
            {
                updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, Hp));
            }
        }

        if (requestedMp > 0 && CanRecoverMp(nowUnixMilliseconds))
        {
            var before = Mp;
            HealMp(requestedMp);
            if (Mp != before)
            {
                updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, Mp));
            }
        }

        return updates.Count == 0
            ? PlayerStatsMutation.Failed(PlayerStatsFailure.NoChange)
            : new PlayerStatsMutation(PlayerStatsFailure.None, updates);
    }

    public void HealMp(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Character.Stats.Mp = (short)Math.Min(Character.Stats.MaxMp, Character.Stats.Mp + amount);
    }

    public PlayerStatsMutation GainExperience(int amount, Func<int, int, int>? rollInclusive = null)
    {
        if (amount == 0)
        {
            return PlayerStatsMutation.Failed(PlayerStatsFailure.NoChange);
        }

        var needed = GetExpNeededForLevel(Character.Level);
        var nextExp = Math.Clamp((long)Character.Exp + amount, 0, int.MaxValue);

        if (amount > 0 && IsAtLevelCap())
        {
            Character.Exp = (int)Math.Min(nextExp, needed);
            return new PlayerStatsMutation(PlayerStatsFailure.None, new[]
            {
                new PlayerStatUpdate(PlayerStatKind.Exp, Character.Exp),
            });
        }

        if (amount > 0 && nextExp >= needed)
        {
            Character.Exp = (int)nextExp;
            var updates = new List<PlayerStatUpdate>(10);
            ApplyLevelUp(rollInclusive ?? RollInclusive, updates);

            var newNeeded = GetExpNeededForLevel(Character.Level);
            if (Character.Exp > newNeeded)
            {
                Character.Exp = newNeeded;
            }

            updates.Add(new PlayerStatUpdate(PlayerStatKind.Exp, Character.Exp));
            return new PlayerStatsMutation(PlayerStatsFailure.None, updates);
        }

        Character.Exp = (int)nextExp;
        return new PlayerStatsMutation(PlayerStatsFailure.None, new[]
        {
            new PlayerStatUpdate(PlayerStatKind.Exp, Character.Exp),
        });
    }

    public PlayerStatsMutation LevelUp(Func<int, int, int>? rollInclusive = null)
    {
        if (IsAtLevelCap())
        {
            return PlayerStatsMutation.Failed(PlayerStatsFailure.MaxLevel);
        }

        var updates = new List<PlayerStatUpdate>(10);
        ApplyLevelUp(rollInclusive ?? RollInclusive, updates);
        updates.Add(new PlayerStatUpdate(PlayerStatKind.Exp, Character.Exp));
        return new PlayerStatsMutation(PlayerStatsFailure.None, updates);
    }

    public byte GetSkillLevel(int skillId)
        => Character.Skills.FirstOrDefault(s => s.SkillId == skillId)?.Level ?? 0;

    private void ApplyLevelUp(Func<int, int, int> rollInclusive, List<PlayerStatUpdate> updates)
    {
        var oldLevel = Character.Level;
        var oldNeeded = GetExpNeededForLevel(oldLevel);

        Character.RemainingAp = (short)(Character.RemainingAp + (IsCygnus(Character.Job) && oldLevel <= 70 ? 6 : 5));

        var maxHp = Character.Stats.MaxHp;
        var maxMp = Character.Stats.MaxMp;
        AddLevelUpVitals(Character.Job, rollInclusive, ref maxHp, ref maxMp);
        maxMp = ClampVital(maxMp + (Character.Stats.Int / 10));

        Character.Exp -= oldNeeded;
        if (Character.Exp < 0)
        {
            Character.Exp = 0;
        }

        Character.Level++;
        Character.Stats.MaxHp = ClampVital(maxHp);
        Character.Stats.MaxMp = ClampVital(maxMp);
        Character.Stats.Hp = Character.Stats.MaxHp;
        Character.Stats.Mp = Character.Stats.MaxMp;

        updates.Add(new PlayerStatUpdate(PlayerStatKind.MaxHp, Character.Stats.MaxHp));
        updates.Add(new PlayerStatUpdate(PlayerStatKind.MaxMp, Character.Stats.MaxMp));
        updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, Character.Stats.Hp));
        updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, Character.Stats.Mp));
        updates.Add(new PlayerStatUpdate(PlayerStatKind.Level, Character.Level));

        if (GrantsSkillPointsOnLevelUp(Character.Job))
        {
            Character.RemainingSp = (short)(Character.RemainingSp + 3);
            updates.Add(new PlayerStatUpdate(PlayerStatKind.AvailableSp, Character.RemainingSp));
        }
        else if (Character.Level <= 10)
        {
            Character.Stats.Str = ClampBaseStat(Character.Stats.Str + Character.RemainingAp);
            Character.RemainingAp = 0;
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Str, Character.Stats.Str));
        }

        updates.Add(new PlayerStatUpdate(PlayerStatKind.AvailableAp, Character.RemainingAp));
    }

    private bool CanRecoverHp(long nowUnixMilliseconds)
    {
        if (_lastHpRecoveryAtMs + RecoveryIntervalMilliseconds > nowUnixMilliseconds)
        {
            return false;
        }

        _lastHpRecoveryAtMs = nowUnixMilliseconds;
        return true;
    }

    private bool CanRecoverMp(long nowUnixMilliseconds)
    {
        if (_lastMpRecoveryAtMs + RecoveryIntervalMilliseconds > nowUnixMilliseconds)
        {
            return false;
        }

        _lastMpRecoveryAtMs = nowUnixMilliseconds;
        return true;
    }

    private CharacterSkill GetOrCreateSkill(int skillId)
    {
        var skill = Character.Skills.FirstOrDefault(s => s.SkillId == skillId);
        if (skill is not null)
        {
            return skill;
        }

        skill = new CharacterSkill { SkillId = skillId };
        Character.Skills.Add(skill);
        return skill;
    }

    private static void AddLevelUpVitals(short job, Func<int, int, int> rollInclusive, ref short maxHp, ref short maxMp)
    {
        int hpGain;
        int mpGain;
        if (job is 0 or 1000 or 2000)
        {
            hpGain = rollInclusive(12, 16);
            mpGain = rollInclusive(10, 12);
        }
        else if (job is >= 100 and <= 132)
        {
            hpGain = rollInclusive(24, 28);
            mpGain = rollInclusive(4, 6);
        }
        else if (job is >= 200 and <= 232)
        {
            hpGain = rollInclusive(10, 14);
            mpGain = rollInclusive(22, 24);
        }
        else if (job is >= 300 and <= 322 || job is >= 400 and <= 422 || job is >= 1300 and <= 1311 || job is >= 1400 and <= 1411)
        {
            hpGain = rollInclusive(20, 24);
            mpGain = rollInclusive(14, 16);
        }
        else if (job is >= 500 and <= 522)
        {
            hpGain = rollInclusive(22, 26);
            mpGain = rollInclusive(18, 22);
        }
        else if (job is >= 1100 and <= 1111)
        {
            hpGain = rollInclusive(24, 28);
            mpGain = rollInclusive(4, 6);
        }
        else if (job is >= 1200 and <= 1212)
        {
            hpGain = rollInclusive(10, 14);
            mpGain = rollInclusive(22, 24);
        }
        else if (job is >= 1500 and <= 1512)
        {
            hpGain = rollInclusive(22, 26);
            mpGain = rollInclusive(18, 22);
        }
        else if (job is >= 2100 and <= 2112)
        {
            hpGain = rollInclusive(50, 52);
            mpGain = rollInclusive(4, 6);
        }
        else
        {
            hpGain = rollInclusive(50, 100);
            mpGain = rollInclusive(50, 100);
        }

        maxHp = ClampVital(maxHp + hpGain);
        maxMp = ClampVital(maxMp + mpGain);
    }

    private bool IsAtLevelCap()
        => Character.Level >= MaxLevel || (IsCygnus(Character.Job) && Character.Level >= CygnusMaxLevel);

    private static bool GrantsSkillPointsOnLevelUp(short job)
        => job is not 0 and not 1000 and not 2000 and not 2001 and not 3000;

    private static bool IsCygnus(short job) => job >= 1000 && job < 2000;

    private static short ClampBaseStat(int value) => (short)Math.Clamp(value, 0, MaxBaseStat);

    private static short ClampVital(int value) => (short)Math.Clamp(Math.Abs(value), 0, MaxVitalStat);

    private static int RollInclusive(int min, int max) => Random.Shared.Next(min, max + 1);

    private static BeginnerSkillGroup? GetBeginnerSkillGroup(int skillId)
        => skillId switch
        {
            1000 or 1001 or 1002 => new BeginnerSkillGroup(1000, 1001, 1002, 6),
            10001000 or 10001001 or 10001002 => new BeginnerSkillGroup(10001000, 10001001, 10001002, 6),
            20001000 or 20001001 or 20001002 => new BeginnerSkillGroup(20001000, 20001001, 20001002, 6),
            20011000 or 20011001 or 20011002 => new BeginnerSkillGroup(20011000, 20011001, 20011002, 6),
            30001000 or 30001001 or 30000002 => new BeginnerSkillGroup(30001000, 30001001, 30000002, 9),
            _ => null,
        };

    private static int GetExpNeededForLevel(int level)
    {
        ReadOnlySpan<int> table =
        [
            0, 15, 34, 57, 92, 135, 372, 560, 840, 1242, 1716,
            2360, 3216, 4200, 5460, 7050, 8840, 11040, 13716, 16680, 20216,
            24402, 28980, 34320, 40512, 47216, 54900, 63666, 73080, 83720, 95700,
            108480, 122760, 138666, 155540, 174216, 194832, 216600, 240500, 266682, 294216,
            324240, 356916, 391160, 428280, 468450, 510420, 555680, 604416, 655200, 709716,
            748608, 789631, 832902, 878545, 926689, 977471, 1031036, 1087536, 1147132, 1209994,
            1276301, 1346242, 1420016, 1497832, 1579913, 1666492, 1757815, 1854143, 1955750, 2062925,
            2175973, 2295216, 2410993, 2553663, 2693603, 2841212, 2996910, 3161140, 3334370, 3517093,
            3709829, 3913127, 4127566, 4353756, 4592341, 4844001, 5109452, 5389449, 5684790, 5996316,
            6324914, 6671519, 7037118, 7422752, 7829518, 8258575, 8711144, 9188514, 9692044, 10223168,
            10783397, 11374327, 11997640, 12655110, 13348610, 14080113, 14851703, 15665576, 16524049, 17429566,
            18384706, 19392187, 20454878, 21575805, 22758159, 24005306, 25320796, 26708375, 28171993, 29715818,
            31344244, 33061908, 34873700, 36784778, 38800583, 40926854, 43169645, 45535341, 48030677, 50662758,
            53439077, 56367538, 59456479, 62714694, 66151459, 69776558, 73600313, 77633610, 81887931, 86375389,
            91108760, 96101520, 101367883, 106922842, 112782213, 118962678, 125481832, 132358236, 139611467, 147262175,
            155332142, 163844343, 172823012, 182293713, 192283408, 202820538, 213935103, 225658746, 238024845, 251068606,
            264827165, 279339693, 294647508, 310794191, 327825712, 345790561, 364739883, 384727628, 405810702, 428049128,
            451506220, 476248760, 502347192, 529875818, 558913012, 589541445, 621848316, 655925603, 691870326, 729784819,
            769777027, 811960808, 856456260, 903390063, 952895838, 1005114529, 1060194805, 1118293480, 1179575962, 1244216724,
            1312399800, 1384319309, 1460180007, 1540197871, 1624600714, 1713628833, 1807535693, 1906588648, 2011069705, 2121276324,
        ];

        return level < 0 || level >= table.Length ? int.MaxValue : table[level];
    }

    private sealed record BeginnerSkillGroup(int FirstSkillId, int SecondSkillId, int ThirdSkillId, int TotalCap);
}
