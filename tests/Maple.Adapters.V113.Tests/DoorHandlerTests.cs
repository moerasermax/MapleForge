using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class DoorHandlerTests
{
    [Fact]
    public async Task HandleUseDoor_TownToTarget_ReturnsTargetWarp()
    {
        var service = new DoorService();
        var targetPos = new Position(120, 240, 0, 0);
        var townPos = new Position(10, 20, 0, 0);
        service.CreateDoor(1, null, targetMapId: 100000000, targetPos, townMapId: 101000000, townPos);

        var handler = new V113DoorHandler(service);
        var result = await handler.HandleUseDoorAsync(UseDoorBody(ownerId: 1, mode: 1), Owner(), currentMapId: 101000000);

        Assert.True(result.Handled);
        Assert.NotNull(result.Warp);
        Assert.True(result.Warp.CanWarp);
        Assert.Equal(100000000, result.Warp.DestinationMapId);
        Assert.Equal(targetPos, result.Warp.DestinationPosition);
        Assert.Empty(result.SelfPackets);
    }

    [Fact]
    public async Task HandleUseDoor_TargetToTownBackwarp_ReturnsTownWarp()
    {
        var service = new DoorService();
        var targetPos = new Position(120, 240, 0, 0);
        var townPos = new Position(10, 20, 0, 0);
        service.CreateDoor(1, null, targetMapId: 100000000, targetPos, townMapId: 101000000, townPos);

        var handler = new V113DoorHandler(service);
        var result = await handler.HandleUseDoorAsync(UseDoorBody(ownerId: 1, mode: 0), Owner(), currentMapId: 100000000);

        Assert.True(result.Handled);
        Assert.NotNull(result.Warp);
        Assert.True(result.Warp.CanWarp);
        Assert.Equal(101000000, result.Warp.DestinationMapId);
        Assert.Equal(townPos, result.Warp.DestinationPosition);
        Assert.Empty(result.SelfPackets);
    }

    [Fact]
    public void ParseUseDoor_ReadsOwnerIdAndBackwarpMode()
    {
        var request = V113DoorPackets.ParseUseDoor(UseDoorBody(ownerId: 1234, mode: 0));

        Assert.Equal(1234, request.OwnerId);
        Assert.True(request.Backwarp);
    }

    [Fact]
    public void DoorPackets_WriteJavaSpawnAndRemoveLayouts()
    {
        Assert.Equal(0x7D, V113DoorPackets.RecvUseDoor);

        Assert.Equal(
            new byte[]
            {
                0x0E, 0x01,
                0x01,
                0xD2, 0x04, 0x00, 0x00,
                0x0A, 0x00,
                0x14, 0x00,
            },
            V113DoorPackets.SpawnDoor(1234, new Position(10, 20, 0, 0), town: true));

        Assert.Equal(
            new byte[]
            {
                0x0F, 0x01,
                0x01,
                0xD2, 0x04, 0x00, 0x00,
            },
            V113DoorPackets.RemoveDoor(1234));
    }

    private static PacketReader UseDoorBody(int ownerId, byte mode)
    {
        var body = new PacketWriter()
            .WriteInt(ownerId)
            .WriteByte(mode)
            .ToArray();
        return new PacketReader(body);
    }

    private static Player Owner() => new(
        new Character
        {
            Id = 1,
            Name = "Owner",
            MapId = 100000000,
        },
        new Position(120, 240, 0, 0));
}
