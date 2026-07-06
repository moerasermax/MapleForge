using Maple.Application.Skills;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113AranComboRequest(byte[] Payload);

internal readonly record struct V113CygnusSummonRequest(int? NpcId);

internal readonly record struct V113SnowballRequest(byte Team, short Unknown, byte Position, byte Stage);

internal readonly record struct V113LeftKnockBackRequest(byte[] Payload);

internal sealed record V113PlayerEventResult(
    bool Handled,
    IReadOnlyList<byte[]> SelfPackets,
    int? StartNpcId = null,
    AranComboResult? AranCombo = null);

internal static class V113PlayerEventHandler
{
    private const int AranComboSkillId = 21000000;

    public static V113AranComboRequest ParseAranCombo(PacketReader reader)
        => new(reader.ReadBytes(reader.Remaining));

    public static V113CygnusSummonRequest ParseCygnusSummon(Player player)
        => new(player.Character.Job switch
        {
            2000 => 1202000,
            1000 => 1101008,
            _ => null,
        });

    public static V113SnowballRequest ParseSnowball(PacketReader reader)
    {
        if (reader.Remaining < 5)
        {
            _ = reader.ReadBytes(reader.Remaining);
            return new V113SnowballRequest(0, 0, 0, 0);
        }

        return new V113SnowballRequest(
            reader.ReadByte(),
            reader.ReadShort(),
            reader.ReadByte(),
            reader.ReadByte());
    }

    public static V113LeftKnockBackRequest ParseLeftKnockBack(PacketReader reader)
        => new(reader.ReadBytes(reader.Remaining));

    public static V113PlayerEventResult HandleAranCombo(
        PacketReader reader,
        Player player,
        SkillService skillService,
        DateTimeOffset now)
    {
        _ = ParseAranCombo(reader);
        var result = skillService.AddAranCombo(player, amount: 1, now);
        if (result.AppliedBuff is null || result.Effect is null)
        {
            return new V113PlayerEventResult(true, [], AranCombo: result);
        }

        return new V113PlayerEventResult(
            true,
            [V113SkillPackets.GiveBuff(AranComboSkillId, result.AppliedBuff.DurationMilliseconds, result.AppliedBuff.Stats, result.Effect)],
            AranCombo: result);
    }

    public static V113PlayerEventResult HandleCygnusSummon(Player player)
    {
        var request = ParseCygnusSummon(player);
        return request.NpcId is { } npcId
            ? new V113PlayerEventResult(true, [], StartNpcId: npcId)
            : EnableActionsOnly();
    }

    public static V113PlayerEventResult HandleSnowball(PacketReader reader)
    {
        _ = ParseSnowball(reader);
        return EnableActionsOnly();
    }

    public static V113PlayerEventResult HandleLeftKnockBack(PacketReader reader, Player player)
    {
        _ = ParseLeftKnockBack(reader);
        return player.Character.MapId / 10000 == 10906
            ? new V113PlayerEventResult(true, [LeftKnockBack(), V113StatsPackets.EnableActions()])
            : EnableActionsOnly();
    }

    /// <summary>Java-source candidate/unverified: MaplePacketCreator.leftKnockBack().</summary>
    public static byte[] LeftKnockBack()
    {
        var w = new PacketWriter(2);
        w.WriteShort(V113ChannelSendOp.LeftKnockBack);
        return w.ToArray();
    }

    private static V113PlayerEventResult EnableActionsOnly()
        => new(true, [V113StatsPackets.EnableActions()]);
}
