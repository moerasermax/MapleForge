using Maple.Adapters.V113.Channel;
using Maple.Application.Stats;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelStatsPacketTests
{
    // ── P068：HEAL_OVER_TIME（自然回血）+ REGEN_HIGH_HP 反作弊檢查 ─────────────────

    [Fact]
    public void HandleHealOverTime_AppliesHealAndReturnsRequestedHp()
    {
        var player = new Player(
            new Character { Id = 1, Name = "Hero", Stats = new CharacterStats { Hp = 10, MaxHp = 100, Mp = 10, MaxMp = 100 } },
            new Position(0, 0, 0, 0));
        var body = new PacketWriter().WriteInt(1234).WriteShort(20).WriteShort(5).ToArray();

        var result = V113StatsHandlers.HandleHealOverTime(new PacketReader(body), player, new StatsService());

        Assert.Equal(20, result.RequestedHp);
        Assert.Equal(30, player.Character.Stats.Hp);
        Assert.Equal(15, player.Character.Stats.Mp);
    }

    [Theory]
    [InlineData(50, 0, false)]   // check = 10，門檻 50，剛好不算異常
    [InlineData(51, 0, true)]    // 超過門檻
    [InlineData(51, 1500001, false)] // 坐椅：check = 160，門檻 800，遠低於門檻
    public void IsRegenHighHp_MatchesJavaThresholdWithChairBonus(int requestedHp, int chairItemId, bool expected)
    {
        Assert.Equal(expected, V113ChannelConnectionHandler.IsRegenHighHp(requestedHp, chairItemId));
    }

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
    public void HandleAutoAssignAp_ParsesTwoSlots_DistributesBoth()
    {
        var chr = new Maple.Core.Characters.Character
        {
            Id = 1, Name = "T", Level = 10, Job = 100, RemainingAp = 10,
            Stats = new Maple.Core.Characters.CharacterStats
            { Str = 12, Dex = 5, Int = 4, Luk = 4, Hp = 50, MaxHp = 50, Mp = 5, MaxMp = 5 }
        };
        var player = new Maple.Core.World.Player(chr, new Maple.Core.World.Position(0, 0, 0, 0));

        var w = new PacketWriter();
        w.WriteInt(999);   // tick
        w.WriteInt(0);     // unknown
        w.WriteInt(0x40);  // STR mask
        w.WriteInt(6);     // count
        w.WriteInt(0x80);  // DEX mask
        w.WriteInt(4);     // count

        var result = V113StatsHandlers.HandleAutoAssignAp(new PacketReader(w.ToArray()), player);

        Assert.True(result.Applied);
        Assert.Equal((short)18, chr.Stats.Str);
        Assert.Equal((short)9, chr.Stats.Dex);
        Assert.Equal((short)0, chr.RemainingAp);
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
