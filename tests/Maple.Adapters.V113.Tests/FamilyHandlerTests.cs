using Maple.Adapters.V113.Channel;
using Maple.Application.Families;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class FamilyHandlerTests
{
    [Fact]
    public async Task InviteAndAccept_CreatesFamilyAndSendsJoinPackets()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        harness.Hook.Put(senior);
        harness.Hook.Put(junior);

        var invite = await harness.Handler.HandleFamilyOperationAsync(
            Reader(new PacketWriter().WriteMapleString("Junior")),
            senior,
            CancellationToken.None);

        Assert.True(invite.Succeeded);
        var invitePacket = Assert.Single(harness.Hook.SentPackets[junior.Character.Id]);
        var inviteReader = new PacketReader(invitePacket);
        Assert.Equal(V113FamilyPackets.SendFamilyJoinRequest, inviteReader.ReadShort());
        Assert.Equal(senior.Character.Id, inviteReader.ReadInt());
        Assert.Equal("Senior", inviteReader.ReadMapleString());

        var accept = await harness.Handler.HandleAcceptFamilyAsync(
            Reader(new PacketWriter().WriteInt(senior.Character.Id).WriteMapleString("Senior").WriteByte(1)),
            junior,
            CancellationToken.None);

        Assert.True(accept.Succeeded);
        Assert.True(senior.Character.FamilyId > 0);
        Assert.Equal(senior.Character.FamilyId, junior.Character.FamilyId);
        Assert.Equal(junior.Character.Id, senior.Character.Junior1);
        Assert.Equal(senior.Character.Id, junior.Character.SeniorId);
        Assert.Contains(accept.SelfPackets, packet => PacketOpcode(packet) == V113FamilyPackets.SendFamilyJoinAccepted);
        Assert.Contains(accept.SelfPackets, packet => PacketOpcode(packet) == V113FamilyPackets.SendFamilyInfoResult);

        var response = Assert.Single(harness.Hook.SentPackets[senior.Character.Id]);
        var responseReader = new PacketReader(response);
        Assert.Equal(V113FamilyPackets.SendFamilyJunior, responseReader.ReadShort());
        Assert.Equal(1, responseReader.ReadByte());
        Assert.Equal("Junior", responseReader.ReadMapleString());
    }

    [Fact]
    public async Task DeleteJunior_DetachesJuniorAndKeepsRemainingBranch()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 35);
        var junior1 = Player(2, "JuniorA", level: 25);
        var junior2 = Player(3, "JuniorB", level: 24);
        await JoinAsync(harness, senior, junior1);
        await JoinAsync(harness, senior, junior2);

        var result = await harness.Handler.HandleDeleteJuniorAsync(
            Reader(new PacketWriter().WriteInt(junior1.Character.Id)),
            senior,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, junior1.Character.FamilyId);
        Assert.Equal(0, junior1.Character.SeniorId);
        Assert.Equal(junior2.Character.Id, senior.Character.Junior1);
        Assert.Equal(senior.Character.Id, junior2.Character.SeniorId);
        Assert.Equal(senior.Character.FamilyId, junior2.Character.FamilyId);
    }

    [Fact]
    public async Task DeleteSenior_LeavesSeniorAndClearsSingleMemberFamily()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);

        var result = await harness.Handler.HandleDeleteSeniorAsync(
            new PacketReader(Array.Empty<byte>()),
            junior,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, junior.Character.FamilyId);
        Assert.Equal(0, junior.Character.SeniorId);
        Assert.Equal(0, senior.Character.FamilyId);
        Assert.Equal(0, senior.Character.Junior1);
    }

    [Fact]
    public async Task UseFamilyBuff_SpendsRepAndWritesChangeRepPacket()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30, currentRep: 1000, totalRep: 1000);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);

        var result = await harness.Handler.HandleUseFamilyAsync(
            Reader(new PacketWriter().WriteInt(2)),
            senior,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(700, senior.Character.CurrentRep);
        var changeRepPacket = Assert.Single(result.SelfPackets, packet => PacketOpcode(packet) == V113FamilyPackets.SendFamilyFamousPointIncResult);
        var reader = new PacketReader(changeRepPacket);
        reader.ReadShort();
        Assert.Equal(-300, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
    }

    [Fact]
    public async Task FamilyPrecept_LeaderSetsNotice()
    {
        var harness = NewHarness();
        var senior = Player(1, "Senior", level: 30);
        var junior = Player(2, "Junior", level: 20);
        await JoinAsync(harness, senior, junior);

        var result = await harness.Handler.HandleFamilyPreceptAsync(
            Reader(new PacketWriter().WriteMapleString("notice")),
            senior,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains(result.SelfPackets, packet => PacketOpcode(packet) == V113FamilyPackets.SendFamilyInfoResult);
        var info = harness.Service.GetFamilyInfo(senior.Character.Id);
        Assert.Equal("notice", info.Notice);
    }

    private static async Task JoinAsync(Harness harness, Player senior, Player junior)
    {
        harness.Hook.Put(senior);
        harness.Hook.Put(junior);
        await harness.Handler.HandleFamilyOperationAsync(
            Reader(new PacketWriter().WriteMapleString(junior.Character.Name)),
            senior,
            CancellationToken.None);
        await harness.Handler.HandleAcceptFamilyAsync(
            Reader(new PacketWriter().WriteInt(senior.Character.Id).WriteMapleString(senior.Character.Name).WriteByte(1)),
            junior,
            CancellationToken.None);
        harness.Hook.ClearSent();
    }

    private static Harness NewHarness()
    {
        var service = new FamilyService(new InMemoryFamilyRepository());
        var hook = new FakeFamilySessionHook();
        var handler = new V113FamilyHandler(service, hook);
        return new Harness(service, hook, handler);
    }

    private static PacketReader Reader(PacketWriter writer) => new(writer.ToArray());

    private static short PacketOpcode(byte[] packet) => new PacketReader(packet).ReadShort();

    private static Player Player(int id, string name, byte level, int mapId = 100000000, int currentRep = 0, int totalRep = 0) =>
        new(
            new Character
            {
                Id = id,
                Name = name,
                Level = level,
                Job = 100,
                MapId = mapId,
                CurrentRep = currentRep,
                TotalRep = totalRep,
            },
            new Position(0, 0, 0, 0));

    private sealed record Harness(FamilyService Service, FakeFamilySessionHook Hook, V113FamilyHandler Handler);

    private sealed class FakeFamilySessionHook : IV113FamilySessionHook
    {
        private readonly Dictionary<int, Player> _playersById = new();
        private readonly Dictionary<string, Player> _playersByName = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<int, List<byte[]>> SentPackets { get; } = new();

        public void Put(Player player)
        {
            _playersById[player.Character.Id] = player;
            _playersByName[player.Character.Name] = player;
        }

        public void ClearSent() => SentPackets.Clear();

        public ValueTask<Player?> FindOnlinePlayerByNameAsync(string name, CancellationToken ct) =>
            ValueTask.FromResult(_playersByName.GetValueOrDefault(name));

        public ValueTask<Player?> FindOnlinePlayerByIdAsync(int characterId, CancellationToken ct) =>
            ValueTask.FromResult(_playersById.GetValueOrDefault(characterId));

        public ValueTask SendPacketAsync(int characterId, byte[] packet, CancellationToken ct)
        {
            if (!SentPackets.TryGetValue(characterId, out var packets))
            {
                packets = [];
                SentPackets[characterId] = packets;
            }

            packets.Add(packet);
            return ValueTask.CompletedTask;
        }
    }
}
