using Maple.Core.World;

namespace Maple.Application.Stats;

/// <summary>Character stats use cases. Protocol encoding stays in adapters.</summary>
public sealed class StatsService
{
    private readonly TimeProvider _timeProvider;
    private readonly Func<int, int, int> _rollInclusive;

    public StatsService()
        : this(TimeProvider.System, RollInclusive)
    {
    }

    public StatsService(TimeProvider timeProvider, Func<int, int, int> rollInclusive)
    {
        _timeProvider = timeProvider;
        _rollInclusive = rollInclusive;
    }

    /// <summary>P165：DI 用——帶升級加成計算器。</summary>
    public StatsService(Maple.Application.Combat.PlayerLocalStatsCalculator localStats)
        : this(TimeProvider.System, RollInclusive)
    {
        _localStats = localStats;
    }

    private readonly Maple.Application.Combat.PlayerLocalStatsCalculator? _localStats;

    public PlayerStatsMutation DistributeAbilityPoint(Player player, AbilityPointTarget target)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.DistributeAbilityPoint(target);
    }

    public PlayerStatsMutation DistributeSkillPoint(Player player, int skillId)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.DistributeSkillPoint(skillId);
    }

    public PlayerStatsMutation GainExperience(Player player, int amount)
    {
        ArgumentNullException.ThrowIfNull(player);
        var bonus = _localStats is not null && player.WouldLevelUp(amount) ? _localStats.ComputeLevelUpBonus(player) : default;
        return player.GainExperience(amount, _rollInclusive, bonus);
    }

    public PlayerStatsMutation RecoverOverTime(Player player, int requestedHp, int requestedMp)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.RecoverOverTime(requestedHp, requestedMp, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
    }

    private static int RollInclusive(int min, int max) => Random.Shared.Next(min, max + 1);
}
