using Maple.Core.Inventory;

namespace Maple.Core.World;

/// <summary>地圖上的掉落物。對照 Java MapleMapItem 的 item/meso 共用地圖物件。</summary>
public sealed class MapDrop : IFieldObject
{
    private bool _pickedUp;

    private MapDrop(
        int objectId,
        Position position,
        Position sourcePosition,
        int sourceObjectId,
        int ownerId,
        byte dropType,
        bool playerDrop,
        Item? item,
        int meso,
        short questId)
    {
        ObjectId = objectId;
        Position = position;
        SourcePosition = sourcePosition;
        SourceObjectId = sourceObjectId;
        OwnerId = ownerId;
        DropType = dropType;
        PlayerDrop = playerDrop;
        Item = item;
        Meso = Math.Max(0, meso);
        QuestId = questId;
    }

    public int ObjectId { get; }

    public Position Position { get; }

    public FieldObjectType Type => FieldObjectType.Drop;

    public Position SourcePosition { get; }

    public int SourceObjectId { get; }

    public int OwnerId { get; }

    public byte DropType { get; }

    public bool PlayerDrop { get; }

    public Item? Item { get; }

    public int Meso { get; }

    public short QuestId { get; }

    public bool IsMeso => Meso > 0;

    public int ItemId => IsMeso ? Meso : Item?.ItemId ?? 0;

    public bool IsPickedUp => _pickedUp;

    public static MapDrop ForItem(
        int objectId,
        Item item,
        Position position,
        Position sourcePosition,
        int sourceObjectId,
        int ownerId,
        byte dropType,
        bool playerDrop = false,
        short questId = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new MapDrop(objectId, position, sourcePosition, sourceObjectId, ownerId, dropType, playerDrop, item, 0, questId);
    }

    public static MapDrop ForMeso(
        int objectId,
        int meso,
        Position position,
        Position sourcePosition,
        int sourceObjectId,
        int ownerId,
        byte dropType,
        bool playerDrop = false)
    {
        if (meso <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(meso), "Meso drop must be positive.");
        }

        return new MapDrop(objectId, position, sourcePosition, sourceObjectId, ownerId, dropType, playerDrop, null, meso, 0);
    }

    public bool TryMarkPickedUp()
    {
        if (_pickedUp)
        {
            return false;
        }

        _pickedUp = true;
        return true;
    }
}
