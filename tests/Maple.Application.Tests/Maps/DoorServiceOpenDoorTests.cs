using Maple.Application.Maps;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.Maps;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Application.Tests.Maps;

/// <summary>P087：<see cref="DoorService.TryOpenDoor"/> 對照 Java <c>MapleDoor</c> 建構子與 <c>getFreePortal</c>。</summary>
public sealed class DoorServiceOpenDoorTests
{
    private static readonly MapData Target = new() { MapId = 101010000, ReturnMapId = 101000000 };

    private static readonly MapData Town = new()
    {
        MapId = 101000000,
        Portals =
        [
            new MapPortal { Id = 0, Type = 0, Name = "sp", X = 0, Y = 0 },
            new MapPortal { Id = 7, Type = 6, Name = "tp", X = 700, Y = 70 },
            new MapPortal { Id = 5, Type = 6, Name = "tp", X = 500, Y = 50 },
        ],
    };

    private static readonly Position DoorPos = new(123, 45, 0, 0);

    [Fact]
    public void TryOpenDoor_PicksLowestIdType6TownPortal()
    {
        var service = new DoorService();

        var result = service.TryOpenDoor(NewPlayer(1), Target, Town, DoorPos);

        var door = Assert.IsType<Door>(result.Door);
        Assert.Equal((101010000, DoorPos, 101000000, new Position(500, 50, 0, 0)), (door.TargetMapId, door.TargetPosition, door.TownMapId, door.TownPortalPosition));
        Assert.Null(result.Replaced);
        Assert.Same(door, service.GetDoorByOwner(101010000, 1));
    }

    [Fact]
    public void TryOpenDoor_NoType6Portal_ReturnsNullDoor()
    {
        var result = new DoorService().TryOpenDoor(NewPlayer(1), Target, new MapData { MapId = 101000000 }, DoorPos);

        Assert.Null(result.Door);
    }

    [Fact]
    public void TryOpenDoor_PartyMemberDoorOccupiesPortal_UsesNextFreePortal()
    {
        var parties = new InMemoryPartyRegistry();
        var leader = NewPlayer(1);
        var member = NewPlayer(2);
        var created = parties.CreateParty(PartyMember.FromCharacter(leader.Character, channelIndex: 1));
        parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(member.Character, channelIndex: 1));
        var service = new DoorService(parties);

        service.TryOpenDoor(leader, Target, Town, DoorPos);
        var second = service.TryOpenDoor(member, Target, Town, DoorPos);

        Assert.Equal(new Position(700, 70, 0, 0), second.Door!.TownPortalPosition);
    }

    [Fact]
    public void TryOpenDoor_SoloPlayers_CanShareSamePortal()
    {
        // Java getFreePortal 只排除「同隊伍」成員的門。
        var service = new DoorService();

        var a = service.TryOpenDoor(NewPlayer(1), Target, Town, DoorPos);
        var b = service.TryOpenDoor(NewPlayer(2), Target, Town, DoorPos);

        Assert.Equal(a.Door!.TownPortalPosition, b.Door!.TownPortalPosition);
    }

    [Fact]
    public void TryOpenDoor_Recast_ReturnsReplacedDoor()
    {
        var service = new DoorService();
        var owner = NewPlayer(1);
        var first = service.TryOpenDoor(owner, Target, Town, DoorPos).Door;

        var second = service.TryOpenDoor(owner, Target, Town, new Position(9, 9, 0, 0));

        Assert.Same(first, second.Replaced);
        Assert.Same(second.Door, service.GetDoorByOwner(101010000, 1));
    }

    [Fact]
    public void CloseDoor_ReturnsDoorAndForgetsIt_ThenNull()
    {
        // P088：對照 Java removeDoor + clearDoors。
        var service = new DoorService();
        var door = service.TryOpenDoor(NewPlayer(1), Target, Town, DoorPos).Door;

        Assert.Same(door, service.CloseDoor(1));
        Assert.Null(service.GetDoorByOwner(101010000, 1));
        Assert.Null(service.GetDoorByOwner(101000000, 1));
        Assert.Null(service.CloseDoor(1));
    }

    [Fact]
    public void CloseDoor_FreesPortalForPartyMember()
    {
        var parties = new InMemoryPartyRegistry();
        var leader = NewPlayer(1);
        var member = NewPlayer(2);
        var created = parties.CreateParty(PartyMember.FromCharacter(leader.Character, channelIndex: 1));
        parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(member.Character, channelIndex: 1));
        var service = new DoorService(parties);
        service.TryOpenDoor(leader, Target, Town, DoorPos);

        service.CloseDoor(1);
        var memberDoor = service.TryOpenDoor(member, Target, Town, DoorPos);

        Assert.Equal(new Position(500, 50, 0, 0), memberDoor.Door!.TownPortalPosition);
    }

    [Theory]
    [InlineData(2311002, true)]
    [InlineData(8001, true)]
    [InlineData(2311003, false)]
    public void IsMagicDoorSkill_MatchesJava(int skillId, bool expected)
    {
        Assert.Equal(expected, DoorService.IsMagicDoorSkill(skillId));
    }

    private static Player NewPlayer(int id)
        => new(new Character { Id = id, Name = $"P{id}", MapId = 101010000 }, new Position(0, 0, 0, 0));
}
