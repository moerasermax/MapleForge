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
    public void SkillCooldown_MatchesJavaSkillCooldownShape()
    {
        var r = new PacketReader(V113SkillPackets.SkillCooldown(1121010, 60));

        Assert.Equal(V113SkillPackets.CooldownOp, r.ReadShort());
        Assert.Equal(1121010, r.ReadInt());
        Assert.Equal(60, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void SkillMoveHandler_CooldownSkill_SendsCooldownThenEnableActionsWhileCooling()
    {
        var player = MakePlayer();
        player.ChangeSkillLevel(CooldownSkillId, level: 1, masterLevel: 10);
        var service = new SkillService(new InMemorySkillCatalog(new[] { CooldownSkill(CooldownSkillId) }));
        var now = new DateTimeOffset(2026, 9, 24, 1, 0, 0, TimeSpan.Zero);

        var first = V113SkillMoveHandler.HandleSpecialMove(
            new PacketReader(BuildSpecialMoveBody(CooldownSkillId, level: 1), offset: 2), player, service, now);

        Assert.Equal(SkillCastStatus.Success, first.Cast?.Status);
        Assert.NotNull(first.CooldownPacket);
        var r = new PacketReader(first.CooldownPacket!);
        Assert.Equal(V113SkillPackets.CooldownOp, r.ReadShort());
        Assert.Equal(CooldownSkillId, r.ReadInt());
        Assert.Equal(30, r.ReadShort());

        var second = V113SkillMoveHandler.HandleSpecialMove(
            new PacketReader(BuildSpecialMoveBody(CooldownSkillId, level: 1), offset: 2), player, service, now.AddSeconds(5));

        Assert.Equal(SkillCastStatus.OnCooldown, second.Cast?.Status);
        Assert.Equal(V113StatsPackets.EnableActions(), second.CooldownPacket);
    }

    [Fact]
    public void SkillMoveHandler_NoCooldownSkill_SendsNoCooldownPacket()
    {
        var player = MakePlayer();
        player.ChangeSkillLevel(2001002, level: 1, masterLevel: 20);
        var service = new SkillService(new InMemorySkillCatalog(new[] { MagicGuardSkill() }));

        var handled = V113SkillMoveHandler.HandleSpecialMove(
            new PacketReader(BuildSpecialMoveBody(2001002, level: 1), offset: 2),
            player,
            service,
            new DateTimeOffset(2026, 9, 24, 1, 0, 0, TimeSpan.Zero));

        Assert.Equal(SkillCastStatus.Success, handled.Cast?.Status);
        Assert.Null(handled.CooldownPacket);
    }

    [Fact]
    public void SkillMoveHandler_Battleship_DoesNotStartCooldownOnCast()
    {
        var player = MakePlayer();
        player.ChangeSkillLevel(SkillService.CorsairBattleshipSkillId, level: 1, masterLevel: 10);
        var service = new SkillService(new InMemorySkillCatalog(new[] { CooldownSkill(SkillService.CorsairBattleshipSkillId) }));
        var now = new DateTimeOffset(2026, 9, 24, 1, 0, 0, TimeSpan.Zero);

        var handled = V113SkillMoveHandler.HandleSpecialMove(
            new PacketReader(BuildSpecialMoveBody(SkillService.CorsairBattleshipSkillId, level: 1), offset: 2), player, service, now);

        Assert.Equal(SkillCastStatus.Success, handled.Cast?.Status);
        Assert.Null(handled.CooldownPacket);
        Assert.False(player.SkillIsCooling(SkillService.CorsairBattleshipSkillId, now.AddSeconds(1)));
    }

    private const int CooldownSkillId = 1121010;

    private static MapleSkill CooldownSkill(int skillId)
        => new()
        {
            Id = skillId,
            Effects = new[]
            {
                new MapleStatEffect
                {
                    SourceId = skillId,
                    Level = 1,
                    MpCon = 1,
                    CooldownSeconds = 30,
                },
            },
        };

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
