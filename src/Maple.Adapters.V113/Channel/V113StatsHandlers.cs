using Maple.Application.Stats;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal static class V113StatsHandlers
{
    public static PlayerStatsMutation HandleDistributeAp(PacketReader reader, Player player, StatsService statsService)
    {
        var request = V113StatsPackets.ParseDistributeAp(reader);
        return request.Target is { } target
            ? statsService.DistributeAbilityPoint(player, target)
            : PlayerStatsMutation.Failed(PlayerStatsFailure.UnsupportedAbilityTarget);
    }

    public static PlayerStatsMutation HandleDistributeSp(PacketReader reader, Player player, StatsService statsService)
    {
        var request = V113StatsPackets.ParseDistributeSp(reader);
        return statsService.DistributeSkillPoint(player, request.SkillId);
    }

    public static PlayerStatsMutation HandleHealOverTime(PacketReader reader, Player player, StatsService statsService)
    {
        var request = V113StatsPackets.ParseHealOverTime(reader);
        return statsService.RecoverOverTime(player, request.Hp, request.Mp);
    }

    public static byte[]? EncodeUpdateStats(PlayerStatsMutation mutation)
        => mutation.Updates.Count == 0 ? null : V113StatsPackets.UpdateStats(mutation.Updates);

    public static byte[]? EncodeUpdateSkill(PlayerStatsMutation mutation)
        => mutation.SkillId is { } skillId && mutation.SkillLevel is { } level
            ? V113StatsPackets.UpdateSkill(skillId, level, level)
            : null;
}
