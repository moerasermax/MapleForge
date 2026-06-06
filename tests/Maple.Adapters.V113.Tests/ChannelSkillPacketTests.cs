using Maple.Adapters.V113.Channel;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelSkillPacketTests
{
    [Fact]
    public void GiveBuff_MatchesJavaGiveBuffShape()
    {
        var packet = V113SkillPackets.GiveBuff(
            2001002,
            200_000,
            new[] { new BuffStatValue(MapleBuffStat.MAGIC_GUARD, 30) },
            new MapleStatEffect { SourceId = 2001002 });

        var r = new PacketReader(packet);
        Assert.Equal(V113SkillPackets.GiveBuffOp, r.ReadShort());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0x00000200, r.ReadInt());
        Assert.Equal(30, r.ReadShort());
        Assert.Equal(2001002, r.ReadInt());
        Assert.Equal(200_000, r.ReadInt());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void CancelBuff_WritesMaskAndCancelType()
    {
        var packet = V113SkillPackets.CancelBuff(new[] { MapleBuffStat.MAGIC_GUARD });

        var r = new PacketReader(packet);
        Assert.Equal(V113SkillPackets.CancelBuffOp, r.ReadShort());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0x00000200, r.ReadInt());
        Assert.Equal(3, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ParseSpecialMove_ReadsJavaFieldsAfterOpcode()
    {
        var w = new PacketWriter();
        w.WriteShort(V113SkillPackets.SpecialMoveRecvOp);
        w.WriteShort(10);
        w.WriteShort(20);
        w.WriteInt(2001002);
        w.WriteByte(3);

        var req = V113SkillPackets.ParseSpecialMove(new PacketReader(w.ToArray(), offset: 2));

        Assert.Equal((short)10, req.OldX);
        Assert.Equal((short)20, req.OldY);
        Assert.Equal(2001002, req.SkillId);
        Assert.Equal(3, req.SkillLevel);
    }

    [Fact]
    public void SkillMoveHandler_ReturnsGiveBuffPacketForSuccessfulCast()
    {
        var player = MakePlayer();
        player.ChangeSkillLevel(2001002, level: 1, masterLevel: 20);
        var service = new SkillService(new InMemorySkillCatalog(new[] { MagicGuardSkill() }));
        var body = BuildSpecialMoveBody(2001002, level: 1);

        var handled = V113SkillMoveHandler.HandleSpecialMove(
            new PacketReader(body, offset: 2),
            player,
            service,
            new DateTimeOffset(2026, 6, 6, 1, 2, 3, TimeSpan.Zero));

        Assert.Equal(SkillCastStatus.Success, handled.Cast?.Status);
        Assert.NotNull(handled.Packet);
        Assert.Equal(19, player.Mp);
    }

    [Fact]
    public void AddCharacterSkillInfo_WritesFourthJobMasterLevelOnlyWhenNeeded()
    {
        var chr = new Character();
        chr.Skills.Add(new CharacterSkillRecord { SkillId = 2001002, Level = 3, MasterLevel = 20 });
        chr.Skills.Add(new CharacterSkillRecord { SkillId = 1121000, Level = 5, MasterLevel = 10 });
        var w = new PacketWriter();

        V113SkillPackets.AddCharacterSkillInfo(w, chr);

        var r = new PacketReader(w.ToArray());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(2001002, r.ReadInt());
        Assert.Equal(3, r.ReadInt());
        Assert.Equal(1121000, r.ReadInt());
        Assert.Equal(5, r.ReadInt());
        Assert.Equal(10, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    private static byte[] BuildSpecialMoveBody(int skillId, byte level)
    {
        var w = new PacketWriter();
        w.WriteShort(V113SkillPackets.SpecialMoveRecvOp);
        w.WriteShort(0);
        w.WriteShort(0);
        w.WriteInt(skillId);
        w.WriteByte(level);
        return w.ToArray();
    }

    private static MapleSkill MagicGuardSkill()
        => new()
        {
            Id = 2001002,
            Effects = new[]
            {
                new MapleStatEffect
                {
                    SourceId = 2001002,
                    Level = 1,
                    IsOverTime = true,
                    DurationMilliseconds = 120_000,
                    MpCon = 11,
                    Statups = new[] { new BuffStatValue(MapleBuffStat.MAGIC_GUARD, 30) },
                },
            },
        };

    private static Player MakePlayer()
    {
        var chr = new Character
        {
            Id = 1,
            Name = "Skill",
            Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = 30, MaxMp = 50 },
        };
        return new Player(chr, new Position(0, 0, 0, 0));
    }
}
