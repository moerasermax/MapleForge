namespace Maple.Application.Events;

/// <summary>P177：事件腳本看到的角色資訊（Java MapleCharacter 的唯讀子集）。</summary>
public sealed record EventCharacterInfo(int Id, string Name, int Level, int Job, int MapId);

/// <summary>
/// P177：事件腳本（scripts/event/*.js）對世界的操作。實作端必須「不阻塞」：換圖與送包都排進各玩家連線或直接入送出佇列，
/// 讓事件腳本可以在自己的鎖內同步呼叫。
/// </summary>
public interface IEventWorld
{
    IReadOnlyList<int> GetCharacterIdsOnMap(int mapId);

    EventCharacterInfo? GetCharacter(int characterId);

    /// <summary>Java <c>chr.changeMap(map, map.getPortal(portal))</c>。</summary>
    void Warp(int characterId, int mapId, int portal);

    /// <summary>Java <c>chr.dropMessage(type, msg)</c>／<c>broadcastMessage(type, msg)</c> 給單一玩家。</summary>
    void SendNotice(int characterId, int type, string message);

    /// <summary>Java <c>getClock(seconds)</c> 給單一玩家。</summary>
    void SendClock(int characterId, int seconds);

    /// <summary>全地圖 <c>broadcastMessage(type, msg)</c>。</summary>
    void BroadcastNotice(int mapId, int type, string message);

    /// <summary>全地圖 environmentChange（3 showEffect、4 playSound、6 musicChange）。</summary>
    void BroadcastEnvironment(int mapId, int mode, string name);

    /// <summary>全地圖 getClock。</summary>
    void BroadcastClock(int mapId, int seconds);

    /// <summary>Java <c>map.resetFully()</c>：清怪、重生出生點怪物、重設反應堆。</summary>
    void ResetMap(int mapId);

    void KillAllMonsters(int mapId, bool animate);

    /// <summary>Java <c>spawnMonsterOnGroundBelow</c>；回傳 objectId，失敗 -1。</summary>
    int SpawnMonster(int mapId, int monsterId, int x, int y);

    IReadOnlyList<int> GetMonsterIds(int mapId);
}

/// <summary>
/// P177：腳本動態改的 portal 腳本（Java <c>map.getPortal(name).setScriptName(script)</c>，如 KPQ 的 next00 → kpq1）。
/// </summary>
public sealed class PortalScriptOverrideRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int MapId, string Portal), string> _overrides = new();

    public void Set(int mapId, string portalName, string script) => _overrides[(mapId, portalName)] = script;

    /// <summary>動態覆寫優先，其次交通船班固定覆寫（Boats.js 船艙出口）。</summary>
    public string? Get(int mapId, string portalName)
        => _overrides.TryGetValue((mapId, portalName), out var script) ? script : TransportService.GetPortalScriptOverride(mapId, portalName);
}

/// <summary>
/// P177：事件腳本主機（Java EventScriptManager）。<see cref="GetEventManager"/> 回傳給 NPC/portal 腳本用的 em 物件（腳本引擎層型別）。
/// </summary>
public interface IEventScriptHost
{
    /// <summary>載入並 init 設定中的事件（只執行一次）。</summary>
    void Start(DateTimeOffset now);

    /// <summary>執行到期的 em／eim 排程。</summary>
    void Tick(DateTimeOffset now);

    bool HasEvent(string name);

    /// <summary>給腳本用的 em facade（找不到回 null）。</summary>
    object? GetEventManager(string name);
}
