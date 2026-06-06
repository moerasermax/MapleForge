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
        return player.GainExperience(amount, _rollInclusive);
    }

    public PlayerStatsMutation RecoverOverTime(Player player, int requestedHp, int requestedMp)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.RecoverOverTime(requestedHp, requestedMp, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
    }

    private static int RollInclusive(int min, int max) => Random.Shared.Next(min, max + 1);
}
