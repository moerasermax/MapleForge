using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Stats;

/// <summary>P162：AP 重設的結果；<see cref="MpToNonHp"/> 對應 Java 的「MP無法增加到HP以外的屬性唷」。</summary>
public sealed record ApResetResult(bool Used, IReadOnlyList<PlayerStatUpdate> Updates, bool MpToNonHp = false)
{
    public static ApResetResult NotUsed { get; } = new(false, []);
}

/// <summary>
/// P162：對照 Java <c>StatsHandling.DistributeAP</c>（MAXHP/MAXMP 分支）與 <c>InventoryHandler</c> 現金道具 5050000（AP 重設）。
/// HP/MP 增減依職業區間擲 <c>Randomizer.rand(min, max)</c>（含兩端），加上強化 HP/MP 技能的 x/y；
/// 兩處表格有細微差異（配點戰士加 1000001 的 <c>x</c>、重設加 <c>y</c>；重設 MP 戰士 5~7、配點 2~4），一律照抄。
/// </summary>
public sealed class AbilityPointService
{
    public const int MaxStat = 999;
    public const int MaxVital = 30000;

    private readonly ISkillCatalog? _skills;
    private readonly Random _random;

    public AbilityPointService(ISkillCatalog? skills = null, Random? random = null)
    {
        _skills = skills;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Java DistributeAP 的 MAXHP/MAXMP：hpmpApUsed ≥ 10000 或上限 ≥ 30000 → 不動作；否則加值、<c>min(30000, abs)</c>、
    /// hpmpApUsed + 1、AP − 1；更新 [MAXHP|MAXMP, AVAILABLEAP]。
    /// </summary>
    public PlayerStatsMutation DistributeHpMp(Player player, AbilityPointTarget target)
    {
        ArgumentNullException.ThrowIfNull(player);

        var chr = player.Character;
        var stats = chr.Stats;
        if (chr.RemainingAp <= 0)
        {
            return PlayerStatsMutation.Failed(PlayerStatsFailure.NotEnoughAbilityPoints);
        }

        PlayerStatUpdate changed;
        switch (target)
        {
            case AbilityPointTarget.MaxHp:
                if (chr.HpMpApUsed >= 10000 || stats.MaxHp >= MaxVital)
                {
                    return PlayerStatsMutation.Failed(PlayerStatsFailure.StatLimitReached);
                }

                stats.MaxHp = Clamp(stats.MaxHp + HpGain(player, warriorUsesX: true));
                changed = new PlayerStatUpdate(PlayerStatKind.MaxHp, stats.MaxHp);
                break;
            case AbilityPointTarget.MaxMp:
                if (chr.HpMpApUsed >= 10000 || stats.MaxMp >= MaxVital)
                {
                    return PlayerStatsMutation.Failed(PlayerStatsFailure.StatLimitReached);
                }

                stats.MaxMp = Clamp(stats.MaxMp + MpGain(player, reset: false));
                changed = new PlayerStatUpdate(PlayerStatKind.MaxMp, stats.MaxMp);
                break;
            default:
                return player.DistributeAbilityPoint(target);
        }

        chr.HpMpApUsed++;
        chr.RemainingAp--;
        return new PlayerStatsMutation(PlayerStatsFailure.None, [changed, new PlayerStatUpdate(PlayerStatKind.AvailableAp, chr.RemainingAp)]);
    }

    /// <summary>
    /// Java 5050000：to == from → 不使用；from = MAXMP 且 to ≠ MAXHP → 提示並不使用；to 上限（素質 ≥ 999、HP/MP ≥ 30000 或
    /// hpmpApUsed ≥ 500）與 from 下限（≤ 4；劍士 STR ≤ 35、弓箭手/盜賊 DEX ≤ 25、海盜 DEX ≤ 20、法師 INT ≤ 20；
    /// HP/MP 需 0 &lt; hpmpApUsed &lt; 500）任一不符 → 不使用。成功：to 加、from 減（HP/MP 依職業表；扣 HP/MP 不回減 hpmpApUsed），
    /// 扣 MAXHP/MAXMP 時 Java 以「舊上限」判斷 <c>hp &gt; maxHp</c> 才壓低（照抄）並帶 HP/MP 更新。
    /// </summary>
    public ApResetResult ResetAp(Player player, AbilityPointTarget to, AbilityPointTarget from)
    {
        ArgumentNullException.ThrowIfNull(player);

        var chr = player.Character;
        var stats = chr.Stats;
        if (to == from)
        {
            return ApResetResult.NotUsed;
        }

        if (from == AbilityPointTarget.MaxMp && to != AbilityPointTarget.MaxHp)
        {
            return new ApResetResult(false, [], MpToNonHp: true);
        }

        var branch = chr.Job % 1000 / 100;
        var used = to switch
        {
            AbilityPointTarget.Str => stats.Str < MaxStat,
            AbilityPointTarget.Dex => stats.Dex < MaxStat,
            AbilityPointTarget.Int => stats.Int < MaxStat,
            AbilityPointTarget.Luk => stats.Luk < MaxStat,
            AbilityPointTarget.MaxHp => stats.MaxHp < MaxVital && chr.HpMpApUsed < 500,
            AbilityPointTarget.MaxMp => stats.MaxMp < MaxVital && chr.HpMpApUsed < 500,
            _ => false,
        };
        used &= from switch
        {
            AbilityPointTarget.Str => !(stats.Str <= 4 || (branch == 1 && stats.Str <= 35)),
            AbilityPointTarget.Dex => !(stats.Dex <= 4 || (branch is 3 or 4 && stats.Dex <= 25) || (branch == 5 && stats.Dex <= 20)),
            AbilityPointTarget.Int => !(stats.Int <= 4 || (branch == 2 && stats.Int <= 20)),
            AbilityPointTarget.Luk => stats.Luk > 4,
            AbilityPointTarget.MaxHp or AbilityPointTarget.MaxMp => chr.HpMpApUsed is > 0 and < 500,
            _ => false,
        };
        if (!used)
        {
            return ApResetResult.NotUsed;
        }

        var updates = new List<PlayerStatUpdate>(3);
        switch (to)
        {
            case AbilityPointTarget.MaxHp:
                stats.MaxHp = Clamp(stats.MaxHp + HpGain(player, warriorUsesX: false));
                chr.HpMpApUsed++;
                updates.Add(new PlayerStatUpdate(PlayerStatKind.MaxHp, stats.MaxHp));
                break;
            case AbilityPointTarget.MaxMp:
                stats.MaxMp = Clamp(stats.MaxMp + MpGain(player, reset: true));
                chr.HpMpApUsed++;
                updates.Add(new PlayerStatUpdate(PlayerStatKind.MaxMp, stats.MaxMp));
                break;
            default:
                updates.Add(AdjustBase(stats, to, +1));
                break;
        }

        switch (from)
        {
            case AbilityPointTarget.MaxHp:
            {
                var oldMax = stats.MaxHp;
                var newMax = (short)(oldMax - HpLoss(player));
                if (stats.Hp > oldMax)
                {
                    stats.Hp = newMax;
                }

                stats.MaxHp = newMax;
                updates.Add(new PlayerStatUpdate(PlayerStatKind.MaxHp, newMax));
                updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, stats.Hp));
                break;
            }

            case AbilityPointTarget.MaxMp:
            {
                var oldMax = stats.MaxMp;
                var newMax = (short)(oldMax - MpLoss(player));
                if (stats.Mp > oldMax)
                {
                    stats.Mp = newMax;
                }

                stats.MaxMp = newMax;
                updates.Add(new PlayerStatUpdate(PlayerStatKind.MaxMp, newMax));
                updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, stats.Mp));
                break;
            }

            default:
                updates.Add(AdjustBase(stats, from, -1));
                break;
        }

        return new ApResetResult(true, updates);
    }

    private static PlayerStatUpdate AdjustBase(Maple.Core.Characters.CharacterStats stats, AbilityPointTarget target, int delta)
    {
        switch (target)
        {
            case AbilityPointTarget.Str:
                stats.Str = (short)(stats.Str + delta);
                return new PlayerStatUpdate(PlayerStatKind.Str, stats.Str);
            case AbilityPointTarget.Dex:
                stats.Dex = (short)(stats.Dex + delta);
                return new PlayerStatUpdate(PlayerStatKind.Dex, stats.Dex);
            case AbilityPointTarget.Int:
                stats.Int = (short)(stats.Int + delta);
                return new PlayerStatUpdate(PlayerStatKind.Int, stats.Int);
            default:
                stats.Luk = (short)(stats.Luk + delta);
                return new PlayerStatUpdate(PlayerStatKind.Luk, stats.Luk);
        }
    }

    /// <summary>Java <c>(short) Math.min(30000, Math.abs(maxhp))</c>（short 加法先截斷）。</summary>
    private static short Clamp(int value) => (short)Math.Min(MaxVital, Math.Abs((int)(short)value));

    private int HpGain(Player player, bool warriorUsesX)
    {
        var job = player.Character.Job;
        return job switch
        {
            0 => Rand(8, 12),
            (>= 100 and <= 132) or (>= 3200 and <= 3212) => Rand(20, 25) + SkillValue(player, 1000001, warriorUsesX ? 'x' : 'y'),
            >= 200 and <= 232 => Rand(10, 20),
            (>= 300 and <= 322) or (>= 400 and <= 434) or (>= 1300 and <= 1312) or (>= 1400 and <= 1412) or (>= 3300 and <= 3312) => Rand(16, 20),
            (>= 500 and <= 522) or (>= 3500 and <= 3512) => Rand(18, 22) + SkillValue(player, 5100000, 'y'),
            >= 1500 and <= 1512 => Rand(18, 22) + SkillValue(player, 15100000, 'y'),
            >= 1100 and <= 1112 => Rand(36, 42) + SkillValue(player, 11000000, 'y'),
            >= 1200 and <= 1212 => Rand(15, 21),
            >= 2000 and <= 2112 => Rand(40, 50),
            _ => Rand(50, 100),
        };
    }

    private int MpGain(Player player, bool reset)
    {
        var job = player.Character.Job;
        return job switch
        {
            0 => Rand(6, 8),
            >= 100 and <= 132 => reset ? Rand(5, 7) : Rand(2, 4),
            (>= 200 and <= 232) or (>= 3200 and <= 3212) => Rand(18, 20) + SkillValue(player, 2000001, 'y') * 2,
            (>= 300 and <= 322) or (>= 400 and <= 434) or (>= 500 and <= 522) or (>= 3500 and <= 3512)
                or (>= 1300 and <= 1312) or (>= 1400 and <= 1412) or (>= 1500 and <= 1512) => Rand(10, 12),
            >= 1100 and <= 1112 => Rand(6, 9),
            >= 1200 and <= 1212 => Rand(18, 20) + SkillValue(player, 12000000, 'y') * 2,
            >= 2000 and <= 2112 => Rand(6, 9),
            _ => Rand(50, 100),
        };
    }

    private int HpLoss(Player player)
    {
        var job = player.Character.Job;
        return job switch
        {
            0 => Rand(9, 13),
            >= 100 and <= 132 => Rand(21, 26) + SkillValue(player, 1000001, 'y'),
            >= 200 and <= 232 => Rand(18, 19),
            (>= 300 and <= 322) or (>= 400 and <= 434) or (>= 1300 and <= 1312) or (>= 1400 and <= 1412)
                or (>= 3300 and <= 3312) or (>= 3500 and <= 3512) => Rand(17, 21),
            >= 500 and <= 522 => Rand(19, 23) + SkillValue(player, 5100000, 'y'),
            >= 1500 and <= 1512 => Rand(19, 23) + SkillValue(player, 15100000, 'y'),
            >= 1100 and <= 1112 => Rand(38, 43) + SkillValue(player, 11000000, 'y'),
            >= 1200 and <= 1212 => Rand(16, 22),
            (>= 2000 and <= 2112) or (>= 3200 and <= 3212) => Rand(44, 54),
            _ => 20,
        };
    }

    private int MpLoss(Player player)
    {
        var job = player.Character.Job;
        return job switch
        {
            0 => 8,
            >= 100 and <= 132 => Rand(6, 7),
            >= 200 and <= 232 => Rand(19, 21) + SkillValue(player, 2000001, 'y'),
            (>= 500 and <= 522) or (>= 300 and <= 322) or (>= 400 and <= 434) or (>= 1300 and <= 1312)
                or (>= 1400 and <= 1412) or (>= 1500 and <= 1512) or (>= 3300 and <= 3312) or (>= 3500 and <= 3512) => Rand(10, 13),
            >= 1100 and <= 1112 => Rand(7, 10),
            >= 1200 and <= 1212 => Rand(18, 23) + SkillValue(player, 12000000, 'y'),
            >= 2000 and <= 2112 => Rand(8, 10),
            _ => 20,
        };
    }

    /// <summary>強化最大 HP/MP 技能的 x 或 y（技能等級 ≥ 1 才算）。</summary>
    private int SkillValue(Player player, int skillId, char field)
    {
        var level = player.GetSkillLevel(skillId);
        if (level < 1 || _skills?.GetSkill(skillId)?.GetEffect(level) is not { } effect)
        {
            return 0;
        }

        return field == 'x' ? effect.X : effect.Y;
    }

    /// <summary>Java <c>Randomizer.rand(lbound, ubound)</c>：含兩端。</summary>
    private int Rand(int min, int max) => _random.Next(min, max + 1);
}
