namespace Maple.Core.World;

public static class DoorFieldObjectTypes
{
    public const FieldObjectType Door = (FieldObjectType)6;
}

public sealed class Door : IFieldObject
{
    public int ObjectId => OwnerId;

    public int OwnerId { get; }

    public int? OwnerPartyId { get; }

    public int TownMapId { get; }

    public Position TownPortalPosition { get; }

    public int TargetMapId { get; }

    public Position TargetPosition { get; }

    public Position Position => TargetPosition;

    public FieldObjectType Type => DoorFieldObjectTypes.Door;

    public Door(
        int ownerId,
        int? ownerPartyId,
        int townMapId,
        Position townPortalPosition,
        int targetMapId,
        Position targetPosition)
    {
        if (ownerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerId));
        }

        OwnerId = ownerId;
        OwnerPartyId = ownerPartyId;
        TownMapId = townMapId;
        TownPortalPosition = townPortalPosition;
        TargetMapId = targetMapId;
        TargetPosition = targetPosition;
    }

    public bool IsVisibleFromMap(int mapId) => mapId == TargetMapId || mapId == TownMapId;

    public Position GetPositionForMap(int mapId) => mapId == TownMapId ? TownPortalPosition : TargetPosition;
}
