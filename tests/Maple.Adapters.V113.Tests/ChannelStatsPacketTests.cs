using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelStatsPacketTests
{
    [Fact]
    public void ParseDistributeAp_ReadsTickAndStatMask()
    {
        var w = new PacketWriter();
        w.WriteInt(1234);
        w.WriteInt(0x40);

        var request = V113StatsPackets.ParseDistributeAp(new PacketReader(w.ToArray()));

        Assert.Equal(1234, request.Tick);
        Assert.Equal(0x40, request.RawStat);
        Assert.Equal(AbilityPointTarget.Str, request.Target);
    }

    [Fact]
    public void ParseDistributeSp_ReadsTickAndSkillId()
    {
        var w = new PacketWriter();
        w.WriteInt(77);
        w.WriteInt(1000000);

        var request = V113StatsPackets.ParseDistributeSp(new PacketReader(w.ToArray()));

        Assert.Equal(77, request.Tick);
        Assert.Equal(1000000, request.SkillId);
    }

    [Fact]
    public void ParseHealOverTime_ReadsTickHpMp()
    {
        var w = new PacketWriter();
        w.WriteInt(88);
        w.WriteShort(10);
        w.WriteShort(3);

        var request = V113StatsPackets.ParseHealOverTime(new PacketReader(w.ToArray()));

        Assert.Equal(88, request.Tick);
        Assert.Equal(10, request.Hp);
        Assert.Equal(3, request.Mp);
    }

    [Fact]
    public void UpdateStats_WritesJavaSortedMaskAndValueSizes()
    {
        var packet = V113StatsPackets.UpdateStats(new[]
        {
            new PlayerStatUpdate(PlayerStatKind.AvailableAp, 4),
            new PlayerStatUpdate(PlayerStatKind.Str, 13),
        });

        byte[] expected =
        {
            0x1D, 0x00,
            0x00,
            0x40, 0x40, 0x00, 0x00,
            0x0D, 0x00,
            0x04, 0x00,
        };
        Assert.Equal(expected, packet);
    }

    [Fact]
    public void UpdateStats_WritesLevelAsByteAndExpAsInt()
    {
        var packet = V113StatsPackets.UpdateStats(new[]
        {
            new PlayerStatUpdate(PlayerStatKind.Exp, 34),
            new PlayerStatUpdate(PlayerStatKind.Level, 2),
        });

        byte[] expected =
        {
            0x1D, 0x00,
            0x00,
            0x10, 0x00, 0x01, 0x00,
            0x02,
            0x22, 0x00, 0x00, 0x00,
        };
        Assert.Equal(expected, packet);
    }

    [Fact]
    public void EnableActions_WritesEmptyUpdateStatsWithItemReaction()
    {
        Assert.Equal(new byte[] { 0x1D, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 }, V113StatsPackets.EnableActions());
    }

    [Fact]
    public void UpdateSkill_WritesJavaUpdateSkillsLayout()
    {
        var packet = V113StatsPackets.UpdateSkill(1000000, 1, 1);
        var expected = new List<byte>
        {
            0x22, 0x00,
            0x01,
            0x01, 0x00,
            0x40, 0x42, 0x0F, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00,
        };
        expected.AddRange(BitConverter.GetBytes(150842304000000000L));
        expected.Add(0x04);

        Assert.Equal(expected.ToArray(), packet);
    }
}
