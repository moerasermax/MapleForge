using Maple.Adapters.V113.Channel;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelPlayerEventHandlerTests
{
    [Fact]
    public void AranCombo_AtTenCombosAppliesRuntimeBuffAndWritesUnverifiedGiveBuffFixture()
    {
        var player = Player(job: 2000);
        player.ChangeSkillLevel(21000000, level: 1, masterLevel: 10);
        player.AddAranCombo(9, FixedNow);
        var service = new SkillService(new InMemorySkillCatalog(new[] { AranComboSkill() }));

        var result = V113PlayerEventHandler.HandleAranCombo(
            Reader(w => w.WriteInt(1234)),
            player,
            service,
            FixedNow.AddMilliseconds(1));

        Assert.Equal(10, result.AranCombo?.Combo);
        Assert.Single(result.SelfPackets);

        // server-to-client buff fixture is Java-source candidate/unverified until live client smoke.
        var r = new PacketReader(result.SelfPackets[0]);
        Assert.Equal(V113ChannelSendOp.GiveBuff, r.ReadShort());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0x00000004, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(10, r.ReadShort());
        Assert.Equal(21000000, r.ReadInt());
        Assert.Equal(99_999, r.ReadInt());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void CygnusSummon_MapsBeginnerJobsToJavaNpcIds_AndUnsupportedJobEnablesActions()
    {
        Assert.Equal(1202000, V113PlayerEventHandler.HandleCygnusSummon(Player(job: 2000)).StartNpcId);
        Assert.Equal(1101008, V113PlayerEventHandler.HandleCygnusSummon(Player(job: 1000)).StartNpcId);

        var unsupported = V113PlayerEventHandler.HandleCygnusSummon(Player(job: 100));

        Assert.Null(unsupported.StartNpcId);
        Assert.Equal(new byte[] { 0x1D, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 }, Assert.Single(unsupported.SelfPackets));
    }

    [Fact]
    public void Snowball_ParseReadsCommentedJavaShapeAndEnablesActions()
    {
        var request = V113PlayerEventHandler.ParseSnowball(Reader(w =>
            w.WriteByte(1)
                .WriteShort(0x1234)
                .WriteByte(0x89)
                .WriteByte(2)));
        var result = V113PlayerEventHandler.HandleSnowball(Reader(w =>
            w.WriteByte(1)
                .WriteShort(0x1234)
                .WriteByte(0x89)
                .WriteByte(2)));

        Assert.Equal(1, request.Team);
        Assert.Equal(0x1234, request.Unknown);
        Assert.Equal(0x89, request.Position);
        Assert.Equal(2, request.Stage);
        Assert.Equal(V113StatsPackets.EnableActions(), Assert.Single(result.SelfPackets));
    }

    [Fact]
    public void LeftKnockBack_InSnowballMapWritesUnverifiedLeftKnockBackAndEnableActions()
    {
        var result = V113PlayerEventHandler.HandleLeftKnockBack(
            Reader(w => w.WriteByte(0xFF)),
            Player(job: 2000, mapId: 109060000));

        Assert.Equal(2, result.SelfPackets.Count);
        Assert.Equal(new byte[] { 0x1A, 0x01 }, result.SelfPackets[0]);
        Assert.Equal(V113StatsPackets.EnableActions(), result.SelfPackets[1]);
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 7, 6, 1, 2, 3, TimeSpan.Zero);

    private static PacketReader Reader(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return new PacketReader(writer.ToArray());
    }

    private static Player Player(short job, int mapId = 100000000)
        => new(
            new Character
            {
                Id = 77,
                Name = "D2",
                Job = job,
                MapId = mapId,
                Stats = new CharacterStats { Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100 },
            },
            new Position(0, 0, 0, 0));

    private static MapleSkill AranComboSkill()
        => new()
        {
            Id = 21000000,
            Effects = Enumerable.Range(1, 10)
                .Select(level => new MapleStatEffect
                {
                    SourceId = 21000000,
                    Level = (byte)level,
                    IsOverTime = true,
                    DurationMilliseconds = 99_999,
                    IsCombo = true,
                })
                .ToArray(),
        };
}
