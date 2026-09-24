using Maple.Application.Parties;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Maps;

public sealed record DoorWarpResult(bool CanWarp, int DestinationMapId, Position DestinationPosition)
{
    public static DoorWarpResult Denied { get; } = new(false, 0, default);
}

/// <summary>P087：開門結果。<see cref="Door"/> 為 null 表示村莊沒有空的時空門傳送點；<see cref="Replaced"/> 是被取代的舊門。</summary>
public sealed record DoorOpenResult(Door? Door, Door? Replaced);

public sealed class DoorService
{
    /// <summary>對照 Java <c>MapleStatEffect.isMagicDoor()</c>。</summary>
    public static bool IsMagicDoorSkill(int skillId)
        => skillId is 2311002 or 8001 or 10008001 or 20008001 or 20018001 or 30008001;

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

    /// <summary>
    /// P087：施放時空門。對照 Java <c>new MapleDoor(owner, pos, skillId)</c>：村莊 = 目標地圖的 <c>returnMap</c>；
    /// <c>getFreePortal</c> 取村莊 type 6 傳送點中 ID 最小、且沒被「同隊伍其他成員的門」佔用的那個；沒有空位回
    /// <c>Door = null</c>（Java 提示「無法使用時空門，村莊不可容納。」）。同主人的舊門會被取代並回傳。
    /// </summary>
    public DoorOpenResult TryOpenDoor(Player owner, MapData targetMap, MapData townMap, Position targetPosition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(targetMap);
        ArgumentNullException.ThrowIfNull(townMap);

        var ownerId = owner.Character.Id;
        var partyId = _parties?.GetPartyForCharacter(ownerId)?.Id;
        Door? replaced;
        MapPortal? portal;
        lock (_gate)
        {
            var taken = partyId is null
                ? new HashSet<Position>()
                : _doorsByOwner.Values
                    .Where(d => d.OwnerId != ownerId && d.TownMapId == townMap.MapId && d.OwnerPartyId == partyId)
                    .Select(static d => d.TownPortalPosition)
                    .ToHashSet();
            portal = townMap.Portals
                .Where(static p => p.Type == 6)
                .OrderBy(static p => p.Id)
                .FirstOrDefault(p => !taken.Contains(ToPosition(p)));
            replaced = _doorsByOwner.GetValueOrDefault(ownerId);
        }

        if (portal is null)
        {
            return new DoorOpenResult(null, null);
        }

        var door = CreateDoor(ownerId, partyId, targetMap.MapId, targetPosition, townMap.MapId, ToPosition(portal));
        return new DoorOpenResult(door, replaced);
    }

    private static Position ToPosition(MapPortal portal) => new((short)portal.X, (short)portal.Y, 0, 0);

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
