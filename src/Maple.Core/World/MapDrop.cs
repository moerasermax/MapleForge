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

    /// <summary>對照 Java <c>MapleMap.spawn*Drop</c>：<c>dropType</c> 0（限定主人）/1（限定隊伍）的
    /// 掉落物固定 30 秒後開放給任何人撿（<c>mdrop.registerFFA(30000)</c>，<c>dropType&gt;=2</c>
    /// 本來就已經開放不需要這個轉換）。P069：對照 Java <c>World.handleMap</c> 裡
    /// <c>item.shouldFFA()</c> 由世界 tick 巡邏觸發，不是玩家操作觸發。</summary>
    public static readonly TimeSpan FfaAfter = TimeSpan.FromMilliseconds(30_000);

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

    public byte DropType { get; private set; }

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

    /// <summary>對照 Java <c>MapleMapItem.shouldFFA</c>：尚未被撿走、<c>DropType &lt; 2</c>（還沒開放）
    /// 且已經過了 <see cref="FfaAfter"/>。</summary>
    public bool ShouldBecomeFfa(DateTimeOffset now) => !_pickedUp && DropType < 2 && now - SpawnedAt >= FfaAfter;

    /// <summary>對照 Java <c>MapleMapItem.setType(2)</c>（經由 <c>World.handleMap</c> 的
    /// <c>item.shouldFFA()</c> 觸發）：開放給任何人撿取，不廣播任何封包（Java 這裡本身也沒有送
    /// 封包，純粹是伺服器內部拾取權限狀態轉換）。</summary>
    public void MarkFfa() => DropType = 2;
}
