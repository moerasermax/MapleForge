using Maple.Application.Parties;
using Maple.Core.World;

namespace Maple.Application.Maps;

public sealed record DoorWarpResult(bool CanWarp, int DestinationMapId, Position DestinationPosition)
{
    public static DoorWarpResult Denied { get; } = new(false, 0, default);
}

public sealed class DoorService
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Door> _doorsByOwner = new();
    private readonly Dictionary<(int MapId, int OwnerId), Door> _doorsByMapAndOwner = new();
    private readonly IPartyRegistry? _parties;

    public DoorService(IPartyRegistry? parties = null)
    {
        _parties = parties;
    }

    public Door CreateDoor(
        int ownerId,
        int? partyId,
        int targetMapId,
        Position targetPos,
        int townMapId,
        Position townPortalPos)
    {
        var door = new Door(ownerId, partyId, townMapId, townPortalPos, targetMapId, targetPos);

        lock (_gate)
        {
            RemoveDoorLocked(ownerId);
            _doorsByOwner[ownerId] = door;
            _doorsByMapAndOwner[(targetMapId, ownerId)] = door;
            _doorsByMapAndOwner[(townMapId, ownerId)] = door;
        }

        return door;
    }

    public Door? GetDoorByOwner(int mapId, int ownerId)
    {
        lock (_gate)
        {
            return _doorsByMapAndOwner.TryGetValue((mapId, ownerId), out var door) ? door : null;
        }
    }

    public void RemoveDoor(int ownerId)
    {
        lock (_gate)
        {
            RemoveDoorLocked(ownerId);
        }
    }

    public DoorWarpResult WarpThroughDoor(Door door, Player player, bool backwarp)
    {
        ArgumentNullException.ThrowIfNull(door);
        ArgumentNullException.ThrowIfNull(player);

        if (!CanUseDoor(door, player))
        {
            return DoorWarpResult.Denied;
        }

        return backwarp
            ? new DoorWarpResult(true, door.TownMapId, door.TownPortalPosition)
            : new DoorWarpResult(true, door.TargetMapId, door.TargetPosition);
    }

    private bool CanUseDoor(Door door, Player player)
    {
        if (player.Character.Id == door.OwnerId)
        {
            return true;
        }

        if (door.OwnerPartyId is not { } ownerPartyId || _parties is null)
        {
            return false;
        }

        return _parties.GetPartyForCharacter(player.Character.Id)?.Id == ownerPartyId;
    }

    private void RemoveDoorLocked(int ownerId)
    {
        if (!_doorsByOwner.Remove(ownerId, out var existing))
        {
            return;
        }

        _doorsByMapAndOwner.Remove((existing.TargetMapId, ownerId));
        _doorsByMapAndOwner.Remove((existing.TownMapId, ownerId));
    }
}
