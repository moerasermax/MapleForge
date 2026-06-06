using System.Text;
using Maple.Adapters.V113.Channel;
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
