using Maple.Core.Inventory;

namespace Maple.Core.World;

/// <summary>地圖上的掉落物。對照 Java MapleMapItem 的 item/meso 共用地圖物件。</summary>
public sealed class MapDrop : IFieldObject
{
    private bool _pickedUp;

    /// <summary>對照 Java <c>MapleMap.spawn*Drop</c>：非 everlast 地圖的掉落物固定 120 秒後過期
    /// （<c>mdrop.registerExpire(120000)</c>）。MapleForge 目前沒有 everlast 地圖旗標，所有地圖統一
    /// 套用這個過期時間，Java 的 everlast 特殊地圖例外暫不移植（見 P061 任務歷程）。</summary>
    public static readonly TimeSpan ExpireAfter = TimeSpan.FromMilliseconds(120_000);

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
        short questId,
        DateTimeOffset spawnedAt)
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
        SpawnedAt = spawnedAt;
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

    public DateTimeOffset SpawnedAt { get; }

    public static MapDrop ForItem(
        int objectId,
        Item item,
        Position position,
        Position sourcePosition,
        int sourceObjectId,
        int ownerId,
        byte dropType,
        DateTimeOffset spawnedAt,
        bool playerDrop = false,
        short questId = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new MapDrop(objectId, position, sourcePosition, sourceObjectId, ownerId, dropType, playerDrop, item, 0, questId, spawnedAt);
    }

    public static MapDrop ForMeso(
        int objectId,
        int meso,
        Position position,
        Position sourcePosition,
        int sourceObjectId,
        int ownerId,
        byte dropType,
        DateTimeOffset spawnedAt,
        bool playerDrop = false)
    {
        if (meso <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(meso), "Meso drop must be positive.");
        }

        return new MapDrop(objectId, position, sourcePosition, sourceObjectId, ownerId, dropType, playerDrop, null, meso, 0, spawnedAt);
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

    /// <summary>對照 Java <c>MapleMapItem.shouldExpire</c>：尚未被撿走且已經過了 <see cref="ExpireAfter"/>。</summary>
    public bool ShouldExpire(DateTimeOffset now) => !_pickedUp && now - SpawnedAt >= ExpireAfter;
}
