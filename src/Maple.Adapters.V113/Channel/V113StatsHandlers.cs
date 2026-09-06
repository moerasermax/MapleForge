using Maple.Application.Stats;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>P068：<see cref="V113StatsHandlers.HandleHealOverTime"/> 的結果，額外帶出玩家端宣稱的
/// 回血量，供呼叫端做 REGEN_HIGH_HP 反作弊檢查（對照 Java PlayerHandler.Heal，只記錄不阻擋，
/// 回血本身已經套用在 <see cref="Mutation"/> 裡，不受這個檢查結果影響）。</summary>
internal readonly record struct V113HealOverTimeResult(PlayerStatsMutation Mutation, int RequestedHp);

internal static class V113StatsHandlers
{
    public static PlayerStatsMutation HandleAutoAssignAp(PacketReader reader, Player player)
    {
        reader.ReadInt(); // tick
        reader.ReadInt(); // unknown 0

        if (reader.Remaining < 16)
            return PlayerStatsMutation.Failed(PlayerStatsFailure.NoChange);

        var assignments = new (AbilityPointTarget Target, int Count)[2];
        for (int i = 0; i < 2; i++)
        {
            var rawStat = reader.ReadInt();
            var count = reader.ReadInt();
            var target = rawStat switch
            {
                0x40 => AbilityPointTarget.Str,
                0x80 => AbilityPointTarget.Dex,
                0x100 => AbilityPointTarget.Int,
                0x200 => AbilityPointTarget.Luk,
                _ => (AbilityPointTarget?)null,
            };
            if (target is null || count < 0)
                return PlayerStatsMutation.Failed(PlayerStatsFailure.UnsupportedAbilityTarget);
            assignments[i] = (target.Value, count);
        }

        return player.AutoAssignAbilityPoints(assignments);
    }

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

    public static V113HealOverTimeResult HandleHealOverTime(PacketReader reader, Player player, StatsService statsService)
    {
        var request = V113StatsPackets.ParseHealOverTime(reader);
        var mutation = statsService.RecoverOverTime(player, request.Hp, request.Mp);
        return new V113HealOverTimeResult(mutation, request.Hp);
    }

    public static byte[]? EncodeUpdateStats(PlayerStatsMutation mutation)
        => mutation.Updates.Count == 0 ? null : V113StatsPackets.UpdateStats(mutation.Updates);

    public static byte[]? EncodeUpdateSkill(PlayerStatsMutation mutation)
        => mutation.SkillId is { } skillId && mutation.SkillLevel is { } level
            ? V113StatsPackets.UpdateSkill(skillId, level, level)
            : null;
}
