using Maple.Adapters.V113.Channel;
using Maple.Application.Skills;
using Maple.Core.IO;
using Maple.Core.Skills;

namespace Maple.Adapters.V113.Tests;

/// <summary>P147：海盜衝刺類 buff 封包（Java givePirate／giveForeignPirate）。</summary>
public sealed class ChannelPirateBuffPacketTests
{
    private static readonly BuffStatValue[] DashStats =
        [new BuffStatValue(MapleBuffStat.DASH_SPEED, 30), new BuffStatValue(MapleBuffStat.DASH_JUMP, 20)];

    [Fact]
    public void GivePirate_Dash_MatchesJavaLayout()
    {
        var r = new PacketReader(V113SkillPackets.GivePirate(DashStats, 20, 5001005));

        Assert.Equal(V113SkillPackets.GiveBuffOp, r.ReadShort());
        SkipMask(r);
        Assert.Equal(0, r.ReadShort());
        foreach (var stat in DashStats)
        {
            Assert.Equal(stat.Value, r.ReadInt());
            Assert.Equal(5001005, r.ReadInt());
            Assert.Equal(0, r.ReadInt()); // long 高位
            Assert.Equal(0, r.ReadByte());
            Assert.Equal(20, r.ReadShort());
        }

        Assert.Equal(0, r.ReadShort());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void GivePirate_SpeedInfusion_UsesSixZeroBytesAnd600()
    {
        var stats = new[] { new BuffStatValue(MapleBuffStat.SPEED_INFUSION, 5) };
        var r = new PacketReader(V113SkillPackets.GivePirate(stats, 60, 5121009));

        r.ReadShort();
        SkipMask(r);
        r.ReadShort();
        Assert.Equal(5, r.ReadInt());
        Assert.Equal(5121009, r.ReadInt());
        r.ReadInt();
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(0, r.ReadByte());
        }

        Assert.Equal(60, r.ReadShort());
        Assert.Equal(600, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void GiveForeignPirate_HasCharacterId_AndSevenZeroBytesForInfusion()
    {
        var stats = new[] { new BuffStatValue(MapleBuffStat.SPEED_INFUSION, 5) };
        var r = new PacketReader(V113SkillPackets.GiveForeignPirate(stats, 60, 7, 15111005));

        Assert.Equal(V113SkillPackets.GiveForeignBuffOp, r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        SkipMask(r);
        r.ReadShort();
        Assert.Equal(5, r.ReadInt());
        Assert.Equal(15111005, r.ReadInt());
        r.ReadInt();
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(0, r.ReadByte());
        }

        Assert.Equal(60, r.ReadShort());
        Assert.Equal(600, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Theory]
    [InlineData(5001005, true)]
    [InlineData(15001003, true)]
    [InlineData(5121009, true)]
    [InlineData(15111005, true)]
    [InlineData(4321000, true)]
    [InlineData(5101004, false)]
    public void IsPirateBuff_MatchesJavaSwitch(int skillId, bool expected)
        => Assert.Equal(expected, SkillService.IsPirateBuff(new MapleStatEffect { SourceId = skillId }));

    [Fact]
    public void BuildGiveBuff_SelectsPirateLayoutForDash()
    {
        var buff = new PlayerBuffChange(5001005, 20_000, DateTimeOffset.UnixEpoch, DashStats);
        var effect = new MapleStatEffect { SourceId = 5001005 };

        Assert.Equal(V113SkillPackets.GivePirate(DashStats, 20, 5001005), V113SkillMoveHandler.BuildGiveBuff(5001005, buff, effect));
        Assert.Equal(
            V113SkillPackets.GiveBuff(2001002, 20_000, DashStats, new MapleStatEffect { SourceId = 2001002 }),
            V113SkillMoveHandler.BuildGiveBuff(2001002, buff, new MapleStatEffect { SourceId = 2001002 }));
    }

    private static void SkipMask(PacketReader r)
    {
        for (var i = 0; i < 4; i++)
        {
            r.ReadInt();
        }
    }
}
