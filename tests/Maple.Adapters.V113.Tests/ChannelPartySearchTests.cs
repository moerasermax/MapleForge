using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Application.OnlinePlayers;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelPartySearchTests
{
    [Fact]
    public void ParseStart_ReadsFourIntsInOrder()
    {
        var w = new PacketWriter();
        w.WriteInt(10);
        w.WriteInt(40);
        w.WriteInt(4);
        w.WriteInt(0x1);
        var reader = new PacketReader(w.ToArray());

        var (minLevel, maxLevel, memberNum, jobMask) = V113PartySearchPackets.ParseStart(reader);

        Assert.Equal(10, minLevel);
        Assert.Equal(40, maxLevel);
        Assert.Equal(4, memberNum);
        Assert.Equal(0x1, jobMask);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public async Task HandleStartAsync_RejectsNonLeader_SendsPopupToSelf()
    {
        var (handler, parties, online, mapRegistry) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        parties.JoinParty(1, Member(2, "Follower"));
        var follower = OnlinePlayer(2, level: 30, job: 100, mapId: 100000000);
        RegisterOnline(online, follower);

        var sent = new List<byte[]>();
        var reader = StartPacket(10, 40, 4, 0x1);

        await handler.HandleStartAsync(reader, follower, (pkt, ct) => { sent.Add(pkt); return Task.CompletedTask; }, CancellationToken.None);

        var popup = new PacketReader(Assert.Single(sent));
        popup.ReadShort(); // V113ChannelSendOp.ServerMessage
        Assert.Equal(1, popup.ReadByte()); // popup type
        Assert.Equal("您並非隊伍的隊長！", popup.ReadMapleString());
    }

    [Fact]
    public async Task HandleStartAsync_MatchesExistingMapPlayer_SendsAutoInvite()
    {
        var (handler, parties, online, mapRegistry) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        var leader = OnlinePlayer(1, level: 30, job: 100, mapId: 100000000);
        RegisterOnline(online, leader);

        var candidateSent = new List<byte[]>();
        var candidateCharacter = new Character { Id = 2, Name = "Candidate", Level = 20, Job = 300, MapId = 100000000 };
        mapRegistry.Register(100000000, 2, candidateCharacter, (pkt, ct) => { candidateSent.Add(pkt); return Task.CompletedTask; }, new object());

        var reader = StartPacket(10, 40, 4, 0x1 /* AllJobs */);
        await handler.HandleStartAsync(reader, leader, (_, _) => Task.CompletedTask, CancellationToken.None);

        var invite = new PacketReader(Assert.Single(candidateSent));
        Assert.Equal(V113PartyPackets.SendPartyOperationOpcode, invite.ReadShort());
        Assert.Equal(4, invite.ReadByte());
        Assert.Equal(1, invite.ReadInt());
        Assert.Equal("Leader", invite.ReadMapleString());
        Assert.Equal(1, invite.ReadByte()); // auto=true
    }

    [Fact]
    public async Task HandleStop_ClearsRegisteredSearch()
    {
        var (handler, parties, online, mapRegistry) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        var leader = OnlinePlayer(1, level: 30, job: 100, mapId: 100000000);
        RegisterOnline(online, leader);
        await handler.HandleStartAsync(StartPacket(10, 40, 4, 0x1), leader, (_, _) => Task.CompletedTask, CancellationToken.None);

        handler.HandleStop(leader);

        var candidate = OnlinePlayer(2, level: 20, job: 300, mapId: 100000000);
        var candidateSent = new List<byte[]>();
        await handler.NotifyMapEntryAsync(candidate, (pkt, ct) => { candidateSent.Add(pkt); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Empty(candidateSent);
    }

    private static PacketReader StartPacket(int minLevel, int maxLevel, int memberNum, int jobMask)
    {
        var w = new PacketWriter();
        w.WriteInt(minLevel);
        w.WriteInt(maxLevel);
        w.WriteInt(memberNum);
        w.WriteInt(jobMask);
        return new PacketReader(w.ToArray());
    }

    private static (V113PartySearchHandler Handler, IPartyRegistry Parties, IOnlinePlayerRegistry Online, IMapSessionRegistry MapRegistry) CreateHarness()
    {
        var parties = new InMemoryPartyRegistry(firstPartyId: 1);
        var online = new InMemoryOnlinePlayerRegistry();
        var mapRegistry = new InMemoryMapSessionRegistry();
        var registry = new InMemoryPartySearchRegistry();
        var service = new PartySearchService(registry, parties, online);
        return (new V113PartySearchHandler(service, mapRegistry, parties), parties, online, mapRegistry);
    }

    private Player OnlinePlayer(int id, int level, int job, int mapId)
    {
        var character = new Character { Id = id, Name = id == 1 ? "Leader" : $"C{id}", Level = (byte)level, Job = (short)job, MapId = mapId };
        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static void RegisterOnline(IOnlinePlayerRegistry online, Player player) =>
        online.Register(player, channel: 1, (_, _) => Task.CompletedTask, new object());

    private static PartyMember Member(int id, string name) =>
        new(id, name, Level: 30, JobId: 100, MapId: 100000000, ChannelIndex: 0);
}
