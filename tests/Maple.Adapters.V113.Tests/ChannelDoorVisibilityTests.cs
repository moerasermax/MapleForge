using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Application.Parties;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Maps;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P160：時空門可見性（對照 Java MapleMap.spawnDoor、MapleDoor.sendSpawnData／sendDestroyData、partyPortal）。</summary>
public sealed class ChannelDoorVisibilityTests
{
    private const int TargetMapId = 101010000;
    private const int TownMapId = 101000000;
    private static readonly Position DoorPos = new(123, 45, 0, 0);
    private static readonly Position TownPortalPos = new(500, 50, 0, 0);

    [Fact]
    public void PartyPortal_MatchesJavaLayout()
        => Assert.Equal(
            new byte[] { 0x37, 0x00, 0x24, 0x00, 0x00, 0x40, 0x23, 0x05, 0x06, 0x50, 0x4A, 0x05, 0x06, 0x7B, 0x00, 0x2D, 0x00 },
            V113DoorPackets.PartyPortal(TownMapId, TargetMapId, DoorPos));

    [Fact]
    public async Task Opening_TargetMapEveryone_TownOnlyOwnerParty_PartyPortalOnlyParty()
    {
        var (effects, door, packets) = Setup(withParty: true);

        await effects.SendDoorOpenedAsync(door, CancellationToken.None);

        var expectedParty = new[]
        {
            V113DoorPackets.SpawnDoor(1, DoorPos, town: false),
            V113DoorPackets.PartyPortal(TownMapId, TargetMapId, DoorPos),
            V113DoorPackets.SpawnPortal(TownMapId, TargetMapId, DoorPos),
            V113StatsPackets.EnableActions(),
        };
        Assert.Equal(expectedParty, packets[1]);
        Assert.Equal(expectedParty, packets[2]); // 隊友在村莊
        Assert.Equal([expectedParty[0], expectedParty[2], expectedParty[3]], packets[3]); // 目標地圖路人：無 partyPortal
        Assert.Empty(packets[4]); // 村莊路人
    }

    [Fact]
    public async Task Destroy_SendsPartyPortalResetToParty()
    {
        var (effects, door, packets) = Setup(withParty: true);

        await effects.SendDoorDestroyAsync(door, CancellationToken.None);

        Assert.Equal([V113DoorPackets.RemoveDoor(1), V113DoorPackets.PartyPortalReset(), V113DoorPackets.RemoveTownPortal()], packets[2]);
        Assert.Equal([V113DoorPackets.RemoveDoor(1), V113DoorPackets.RemoveTownPortal()], packets[3]);
        Assert.Empty(packets[4]);
    }

    [Fact]
    public void SpawnData_FollowsJavaSendSpawnData()
    {
        var (withParty, _, _) = Setup(withParty: true);
        Assert.Equal(
            [V113DoorPackets.SpawnDoor(1, DoorPos, town: true), V113DoorPackets.SpawnPortal(TownMapId, TargetMapId, DoorPos)],
            withParty.BuildDoorSpawnData(TargetMapId, 3));
        Assert.Empty(withParty.BuildDoorSpawnData(TownMapId, 1)); // 有隊伍的主人在村莊：Java 不送
        Assert.Empty(withParty.BuildDoorSpawnData(TownMapId, 2));

        var (solo, _, _) = Setup(withParty: false);
        Assert.Equal(
            [V113DoorPackets.SpawnDoor(1, TownPortalPos, town: true), V113DoorPackets.SpawnPortal(TownMapId, TargetMapId, DoorPos)],
            solo.BuildDoorSpawnData(TownMapId, 1));
    }

    private static (V113BuffCancellationEffects Effects, Door Door, Dictionary<int, List<byte[]>> Packets) Setup(bool withParty)
    {
        var parties = new InMemoryPartyRegistry();
        var doors = new DoorService(parties);
        var registry = new InMemoryMapSessionRegistry();
        var packets = new Dictionary<int, List<byte[]>>();
        var players = new Dictionary<int, Player>();
        foreach (var (id, mapId) in new[] { (1, TargetMapId), (2, TownMapId), (3, TargetMapId), (4, TownMapId) })
        {
            var player = new Player(new Character { Id = id, Name = $"P{id}", MapId = mapId }, new Position(0, 0, 0, 0));
            players[id] = player;
            var list = packets[id] = [];
            registry.Register(mapId, id, player, (p, _) => { list.Add(p); return Task.CompletedTask; }, new object());
        }

        if (withParty)
        {
            var created = parties.CreateParty(PartyMember.FromCharacter(players[1].Character, channelIndex: 1));
            parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(players[2].Character, channelIndex: 1));
        }

        var town = new MapData { MapId = TownMapId, Portals = [new MapPortal { Id = 5, Type = 6, Name = "tp", X = 500, Y = 50 }] };
        var door = doors.TryOpenDoor(players[1], new MapData { MapId = TargetMapId, ReturnMapId = TownMapId }, town, DoorPos).Door!;
        return (new V113BuffCancellationEffects(new SummonService(), doors, registry), door, packets);
    }
}
