using System.Text;
using Maple.Adapters.V113.Channel;
using Maple.Application.OnlinePlayers;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelPartyPacketTests
{
    [Fact]
    public void PartyCreated_WritesJavaHeaderAndDoorPlaceholders()
    {
        var packet = V113PartyPackets.PartyCreated(25);
        var reader = new PacketReader(packet);

        Assert.Equal(V113PartyPackets.SendPartyOperationOpcode, reader.ReadShort());
        Assert.Equal(8, reader.ReadByte());
        Assert.Equal(25, reader.ReadInt());
        Assert.Equal(PartyMember.NoDoorMapId, reader.ReadInt());
        Assert.Equal(PartyMember.NoDoorMapId, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void UpdatePartyJoin_WritesSixSlotStatusForRecipientChannel()
    {
        var leader = Member(1, "Leader", channelIndex: 0, mapId: 100000000);
        var guest = Member(2, "Guest", channelIndex: 1, mapId: 200000000);
        var party = new PartyState(25, leader.CharacterId, new[] { leader, guest });

        var packet = V113PartyPackets.UpdateParty(0, party, PartyUpdateKind.Join, guest);
        var reader = new PacketReader(packet);

        Assert.Equal(V113PartyPackets.SendPartyOperationOpcode, reader.ReadShort());
        Assert.Equal(0x0F, reader.ReadByte());
        Assert.Equal(25, reader.ReadInt());
        Assert.Equal("Guest", reader.ReadMapleString());
        Assert.Equal(new[] { 1, 2, 0, 0, 0, 0 }, ReadInts(reader, 6));
        Assert.Equal(new[] { "Leader", "Guest", "", "", "", "" }, ReadFixedNames(reader, 6));
        Assert.Equal(new[] { 100, 100, 0, 0, 0, 0 }, ReadInts(reader, 6));
        Assert.Equal(new[] { 30, 30, 0, 0, 0, 0 }, ReadInts(reader, 6));
        Assert.Equal(new[] { 0, 1, -2, -2, -2, -2 }, ReadInts(reader, 6));
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(new[] { 100000000, 0, 0, 0, 0, 0 }, ReadInts(reader, 6));

        Assert.Equal(PartyMember.NoDoorMapId, reader.ReadInt());
        Assert.Equal(PartyMember.NoDoorMapId, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(new int[20], ReadInts(reader, 20));
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public async Task HandlerJoinOperation_BroadcastsJoinPacketToLeaderAndSelf()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 40));
        service.CreateParty(Member(1, "Leader", channelIndex: 0));
        var hook = new FakePartySessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var guest = Player(2, "Guest", channelIndexMap: 100000000);
        var selfPackets = new List<byte[]>();

        var request = new PacketWriter()
            .WriteByte((byte)V113PartyClientOperation.Join)
            .WriteInt(40)
            .ToArray();

        await handler.HandlePartyOperationAsync(
            new PacketReader(request),
            guest,
            channelIndex: 0,
            (packet, _) =>
            {
                selfPackets.Add(packet);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var leaderPacket = Assert.Single(hook.SentPackets);
        Assert.Equal(1, leaderPacket.CharacterId);
        Assert.Single(selfPackets);
        AssertJoinPacket(leaderPacket.Packet, "Guest");
        AssertJoinPacket(selfPackets[0], "Guest");
    }

    [Fact]
    public async Task CentralPartySessionHook_UsesOnlineRegistryForLookupAndSend()
    {
        var online = new InMemoryOnlinePlayerRegistry();
        var player = Player(2, "Guest", channelIndexMap: 100000000);
        var sentPackets = new List<byte[]>();
        online.Register(player, 2, (packet, _) =>
            {
                sentPackets.Add(packet);
                return Task.CompletedTask;
            },
            new object());
        var hook = new CentralPartySessionHook(online);

        var found = await hook.FindOnlinePlayerByNameAsync("guest", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(player.Character.Id, found.CharacterId);
        Assert.Equal(1, found.ChannelIndex);

        var packet = new byte[] { 0x01, 0x02 };
        await hook.SendToCharacterAsync(player.Character.Id, packet, CancellationToken.None);
        Assert.Same(packet, Assert.Single(sentPackets));
    }

    private static void AssertJoinPacket(byte[] packet, string expectedTargetName)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113PartyPackets.SendPartyOperationOpcode, reader.ReadShort());
        Assert.Equal(0x0F, reader.ReadByte());
        reader.ReadInt();
        Assert.Equal(expectedTargetName, reader.ReadMapleString());
    }

    private static int[] ReadInts(PacketReader reader, int count)
    {
        var values = new int[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadInt();
        }

        return values;
    }

    private static string[] ReadFixedNames(PacketReader reader, int count)
    {
        var names = new string[count];
        for (var i = 0; i < names.Length; i++)
        {
            var bytes = new byte[15];
            for (var j = 0; j < bytes.Length; j++)
            {
                bytes[j] = reader.ReadByte();
            }

            names[i] = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        return names;
    }

    [Theory]
    [InlineData(2, "left:2")]
    [InlineData(1, "disband:1")]
    public async Task HandlerLeave_NotifiesEventInstance(int leaverId, string expected)
    {
        // P179：Java PartyHandler case 2——隊長離隊（解散）→ disbandParty，隊員離隊 → leftParty。
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 40));
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(40, Member(2, "Guest"));
        var events = new RecordingEventHost();
        var handler = new V113PartyOperationHandler(service, new FakePartySessionHook(), events: events);

        await handler.HandlePartyOperationAsync(
            new PacketReader(new PacketWriter().WriteByte((byte)V113PartyClientOperation.Leave).ToArray()),
            Player(leaverId, leaverId == 1 ? "Leader" : "Guest", channelIndexMap: 100000000),
            channelIndex: 0,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal([expected], events.Calls);
    }

    [Fact]
    public async Task HandlerExpel_OnlineMember_DisbandsLeadersEventInstance()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 40));
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(40, Member(2, "Guest"));
        var events = new RecordingEventHost();
        var handler = new V113PartyOperationHandler(service, new FakePartySessionHook(), events: events);

        await handler.HandlePartyOperationAsync(
            new PacketReader(new PacketWriter().WriteByte((byte)V113PartyClientOperation.Expel).WriteInt(2).ToArray()),
            Player(1, "Leader", channelIndexMap: 100000000),
            channelIndex: 0,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(["disband:1"], events.Calls);
    }

    private sealed class RecordingEventHost : Maple.Application.Events.IEventScriptHost
    {
        public List<string> Calls { get; } = [];

        public void Start(DateTimeOffset now) { }

        public void Tick(DateTimeOffset now) { }

        public bool HasEvent(string name) => false;

        public object? GetEventManager(string name) => null;

        public object? GetEventInstance(int characterId) => null;

        public void OnChangedMap(int characterId, int mapId) { }

        public int? GetTimeLeftSeconds(int characterId) => null;

        public void OnPlayerDead(int characterId) { }

        public bool OnPlayerRevive(int characterId) => false;

        public void OnPlayerDisconnected(int characterId) { }

        public void OnMonsterKilled(int mapId, int objectId, int monsterId, int? highestDamageCharacterId) { }

        public void OnLeftParty(int characterId) => Calls.Add($"left:{characterId}");

        public void OnDisbandParty(int characterId) => Calls.Add($"disband:{characterId}");
    }

    private static PartyMember Member(int id, string name, int channelIndex = 0, int mapId = 100000000) =>
        new(id, name, Level: 30, JobId: 100, mapId, channelIndex);

    private static Player Player(int id, string name, int channelIndexMap) =>
        new(new Character
        {
            Id = id,
            Name = name,
            Level = 30,
            Job = 100,
            MapId = channelIndexMap,
        }, new Position(0, 0, 0, 0));

    private sealed class FakePartySessionHook : IV113PartySessionHook
    {
        public List<(int CharacterId, byte[] Packet)> SentPackets { get; } = new();

        public ValueTask<V113PartySessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct) =>
            ValueTask.FromResult<V113PartySessionPlayer?>(null);

        public Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
        {
            SentPackets.Add((characterId, packet));
            return Task.CompletedTask;
        }
    }
}
