namespace Maple.Core.World;

/// <summary>
/// 執行期地圖實例（＝舊 OdinMS <c>MapleMap</c> 的乾淨版，每頻道一份）。
/// 持有場上所有 <see cref="IFieldObject"/>，支援範圍查詢（供戰鬥/NPC/技能命中）。
/// 並行：領域變更應由上層的 field-actor/命令佇列序列化執行；**刻意不在此用 ConcurrentDictionary**，
/// 不把並行語意洩進領域模型（見 docs/design/in-game-執行期狀態架構.md 風險#1）。
/// </summary>
public sealed class FieldInstance
{
    private readonly Dictionary<int, IFieldObject> _objects = new();

    public int MapId { get; }

    public FieldInstance(int mapId) => MapId = mapId;

    /// <summary>P065（M4-2 世界 tick，怪物重生）：這個 field 的怪物重生點清單，對照 Java
    /// <c>MapleMap.monsterSpawn</c>。由 <c>CombatService.SpawnMapMonsters</c> 在 field 建立時
    /// 一併填入（跟場上物件一樣，領域變更要由呼叫端 <c>lock(field)</c> 序列化）。</summary>
    public List<MobSpawnPoint> SpawnPoints { get; } = new();

    /// <summary>加入/取代一個場上物件（以 ObjectId 為鍵）。</summary>
    public void Add(IFieldObject obj) => _objects[obj.ObjectId] = obj;

    /// <summary>移除場上物件（離開地圖/斷線/死亡）。回傳是否確有移除。</summary>
    public bool Remove(int objectId) => _objects.Remove(objectId);

    public IFieldObject? Get(int objectId) => _objects.TryGetValue(objectId, out var o) ? o : null;

    /// <summary>場上所有物件。</summary>
    public IReadOnlyCollection<IFieldObject> Objects => _objects.Values;

    /// <summary>場上所有玩家。</summary>
    public IEnumerable<Player> Players => _objects.Values.OfType<Player>();

    /// <summary>以 objectId 查詢怪物（不存在或型別不符回 null）。</summary>
    public Mob? GetMob(int objectId) => _objects.TryGetValue(objectId, out var o) && o is Mob m ? m : null;

    /// <summary>
    /// 中心點半徑內的場上物件（含中心自身；要排除自身由呼叫端 filter）。
    /// 供戰鬥傷害、NPC 互動、技能 AoE 的範圍判定。
    /// </summary>
    public IEnumerable<IFieldObject> ObjectsInRange(Position center, double radius)
        => _objects.Values.Where(o => o.Position.DistanceTo(center) <= radius);
}
