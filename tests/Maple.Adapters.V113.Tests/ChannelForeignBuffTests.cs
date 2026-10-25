using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P139：他人可見的 buff（Java giveForeignBuff／cancelForeignBuff）。</summary>
public sealed class ChannelForeignBuffTests
{
    [Fact]
    public void CancelForeignBuff_MatchesJavaLayout()
    {
        var r = new PacketReader(V113SkillPackets.CancelForeignBuff(7, [MapleBuffStat.DARKSIGHT]));

        Assert.Equal(unchecked((short)0xC1), r.ReadShort());
        Assert.Equal(7, r.ReadInt());
        var mask = new int[4];
        for (var i = 0; i < 4; i++)
        {
            mask[i] = r.ReadInt();
        }

        Assert.Equal(MapleBuffStat.DARKSIGHT.GetMaskValue(), mask[MapleBuffStat.DARKSIGHT.GetMaskPosition()]);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public async Task ApplyAsync_BroadcastsCancelForeignBuffToOthersOnly()
    {
        var registry = new InMemoryMapSessionRegistry();
        var effects = new V113BuffCancellationEffects(new SummonService(), new DoorService(), registry);
        var player = new Player(new Character { Id = 1, Name = "Thief", MapId = 100000000 }, new Position(0, 0, 0, 0));
        var other = new Player(new Character { Id = 2, Name = "Other", MapId = 100000000 }, new Position(0, 0, 0, 0));
        var selfPackets = new List<byte[]>();
        var otherPackets = new List<byte[]>();
        registry.Register(100000000, 1, player, (p, _) => { selfPackets.Add(p); return Task.CompletedTask; }, new object());
        registry.Register(100000000, 2, other, (p, _) => { otherPackets.Add(p); return Task.CompletedTask; }, new object());
        player.ApplyBuffStats(
            new MapleStatEffect
            {
                SourceId = 4001003,
                Level = 1,
                IsOverTime = true,
                DurationMilliseconds = 60_000,
                Statups = [new BuffStatValue(MapleBuffStat.SPEED, -10), new BuffStatValue(MapleBuffStat.DARKSIGHT, 1)],
            },
            DateTimeOffset.UnixEpoch);
        var cancellations = player.CancelBuffBySource(4001003);

        await effects.ApplyAsync(player, field: null, cancellations, CancellationToken.None);

        Assert.Empty(selfPackets);
        Assert.Equal(V113SkillPackets.CancelForeignBuff(1, Assert.Single(cancellations).Stats), Assert.Single(otherPackets));
    }

    [Theory]
    [InlineData(4001003, MapleBuffStat.DARKSIGHT, 0)]
    [InlineData(14001003, MapleBuffStat.DARKSIGHT, 0)]
    [InlineData(1111002, MapleBuffStat.COMBO, 0)]
    [InlineData(3101004, MapleBuffStat.SOULARROW, 0)]
    [InlineData(4111002, MapleBuffStat.SHADOWPARTNER, 0)]
    [InlineData(10001026, MapleBuffStat.SOARING, 1)]
    [InlineData(1011, MapleBuffStat.BERSERK_FURY, 1)]
    [InlineData(20001010, MapleBuffStat.DIVINE_BODY, 1)]
    public void GetForeignBuffStats_MatchesJavaSwitch(int skillId, MapleBuffStat stat, int value)
    {
        var foreign = SkillService.GetForeignBuffStats(new MapleStatEffect { SourceId = skillId });

        Assert.Equal(new BuffStatValue(stat, value), Assert.Single(foreign!));
    }

    [Fact]
    public void GetForeignBuffStats_Berserk2Statup_BroadcastsBerserkFury()
    {
        var effect = new MapleStatEffect { SourceId = 2022123, IsSkill = true, Statups = [new BuffStatValue(MapleBuffStat.BERSERK_FURY, 1)] };

        Assert.Equal(new BuffStatValue(MapleBuffStat.BERSERK_FURY, 1), Assert.Single(SkillService.GetForeignBuffStats(effect)!));
    }

    [Theory]
    [InlineData(2001002, true)]
    [InlineData(4001003, false)]
    public void GetForeignBuffStats_OrdinaryBuffOrItem_IsNull(int sourceId, bool isSkill)
        => Assert.Null(SkillService.GetForeignBuffStats(new MapleStatEffect { SourceId = sourceId, IsSkill = isSkill }));
}
