using Maple.Application.Maps;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113DoorHandleResult(
    bool Handled,
    DoorWarpResult? Warp,
    IReadOnlyList<byte[]> SelfPackets);

public sealed class V113DoorHandler
{
    private readonly DoorService _doors;

    public V113DoorHandler(DoorService doors)
    {
        ArgumentNullException.ThrowIfNull(doors);
        _doors = doors;
    }

    internal Task<V113DoorHandleResult> HandleUseDoorAsync(PacketReader reader, Player player, int currentMapId)
    {
        V113UseDoorRequest request;
        try
        {
            request = V113DoorPackets.ParseUseDoor(reader);
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(EnableActionsOnly());
        }

        var door = _doors.GetDoorByOwner(currentMapId, request.OwnerId);
        if (door is null)
        {
            return Task.FromResult(EnableActionsOnly());
        }

        var warp = _doors.WarpThroughDoor(door, player, request.Backwarp);
        return Task.FromResult(warp.CanWarp
            ? new V113DoorHandleResult(true, warp, Array.Empty<byte[]>())
            : EnableActionsOnly());
    }

    private static V113DoorHandleResult EnableActionsOnly()
        => new(true, null, [V113StatsPackets.EnableActions()]);
}
