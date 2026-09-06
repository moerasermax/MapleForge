using Maple.Adapters.V113.Channel;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelPartyMapEntryTests
{
    [Fact]
    public async Task NotifyMapEntryAsync_PlayerNotInParty_NoPacketsSent()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        var hook = new FakeSessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var player = Player(1, "Solo", level: 30, job: 100, mapId: 100000000, hp: 50, maxHp: 50);
        var selfPackets = new List<byte[]>();

        await handler.NotifyMapEntryAsync(player, channelIndex: 0, Capture(selfPackets), CancellationToken.None);

        Assert.Empty(selfPackets);
        Assert.Empty(hook.SentPackets);
    }

    [Fact]
    public async Task NotifyMapEntryAsync_SoloLeader_SendsSelfSilentUpdateOnly()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        service.CreateParty(Member(1, "Leader"));
        var hook = new FakeSessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var player = Player(1, "Leader", level: 35, job: 122, mapId: 200000000, hp: 100, maxHp: 100);
        var selfPackets = new List<byte[]>();

        await handler.NotifyMapEntryAsync(player, channelIndex: 0, Capture(selfPackets), CancellationToken.None);

        var packet = Assert.Single(selfPackets);
        var reader = new PacketReader(packet);
        Assert.Equal(V113PartyPackets.SendPartyOperationOpcode, reader.ReadShort());
        Assert.Equal(0x07, reader.ReadByte()); // SilentUpdate/LogOnOff wire tag

        // Level/job/map snapshot for this character should reflect the refreshed entry, proving
        // silentPartyUpdate() actually updated the party registry before the packet was built.
        var updated = service.GetPartyForCharacter(1)!.Leader!;
        Assert.Equal(35, updated.Level);
        Assert.Equal(122, updated.JobId);
        Assert.Equal(200000000, updated.MapId);

        Assert.Empty(hook.SentPackets); // no other online party members to sync HP with
    }

    [Fact]
    public async Task NotifyMapEntryAsync_WithOnlinePartner_BroadcastsUpdateAndSyncsHpBothWays()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(1, Member(2, "Guest"));

        var hook = new FakeSessionHook();
        hook.Register(new V113PartySessionPlayer(2, "Guest", 30, 100, 100000000, ChannelIndex: 0, Hp: 77, MaxHp: 200));

        var handler = new V113PartyOperationHandler(service, hook);
        var player = Player(1, "Leader", level: 35, job: 122, mapId: 200000000, hp: 40, maxHp: 400);
        var selfPackets = new List<byte[]>();

        await handler.NotifyMapEntryAsync(player, channelIndex: 0, Capture(selfPackets), CancellationToken.None);

        // Self: one SilentUpdate party packet + one HP packet describing the online partner (Guest).
        Assert.Equal(2, selfPackets.Count);
        AssertOpcode(selfPackets[0], V113PartyPackets.SendPartyOperationOpcode);
        AssertHpPacket(selfPackets[1], expectedCharacterId: 2, expectedHp: 77, expectedMaxHp: 200);

        // Other online member: one SilentUpdate party packet (via hook) + one HP packet describing self.
        Assert.Equal(2, hook.SentPackets.Count);
        Assert.All(hook.SentPackets, p => Assert.Equal(2, p.CharacterId));
        AssertOpcode(hook.SentPackets[0].Packet, V113PartyPackets.SendPartyOperationOpcode);
        AssertHpPacket(hook.SentPackets[1].Packet, expectedCharacterId: 1, expectedHp: 40, expectedMaxHp: 400);
    }

    private static void AssertOpcode(byte[] packet, short expectedOpcode) =>
        Assert.Equal(expectedOpcode, new PacketReader(packet).ReadShort());

    private static void AssertHpPacket(byte[] packet, int expectedCharacterId, int expectedHp, int expectedMaxHp)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113PartyPackets.SendUpdatePartyMemberHpOpcode, reader.ReadShort());
        Assert.Equal(expectedCharacterId, reader.ReadInt());
        Assert.Equal(expectedHp, reader.ReadInt());
        Assert.Equal(expectedMaxHp, reader.ReadInt());
    }

    private static Func<byte[], CancellationToken, Task> Capture(List<byte[]> sink) =>
        (packet, _) =>
        {
            sink.Add(packet);
            return Task.CompletedTask;
        };

    private static PartyMember Member(int id, string name) =>
        new(id, name, Level: 30, JobId: 100, MapId: 100000000, ChannelIndex: 0);

    private static Player Player(int id, string name, int level, int job, int mapId, int hp, int maxHp) =>
        new(
            new Character
            {
                Id = id,
                Name = name,
                Level = (byte)level,
                Job = (short)job,
                MapId = mapId,
                Stats = new CharacterStats { Hp = (short)hp, MaxHp = (short)maxHp },
            },
            new Position(0, 0, 0, 0));

    private sealed class FakeSessionHook : IV113PartySessionHook
    {
        private readonly Dictionary<string, V113PartySessionPlayer> _byName = new();

        public List<(int CharacterId, byte[] Packet)> SentPackets { get; } = new();

        public void Register(V113PartySessionPlayer player) => _byName[player.Name] = player;

        public ValueTask<V113PartySessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct) =>
            ValueTask.FromResult(_byName.TryGetValue(characterName, out var player) ? player : null);

        public Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
        {
            SentPackets.Add((characterId, packet));
            return Task.CompletedTask;
        }
    }
}
