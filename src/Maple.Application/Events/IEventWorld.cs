namespace Maple.Application.Events;

/// <summary>P177：事件腳本看到的角色資訊（Java MapleCharacter 的唯讀子集）。</summary>
public sealed record EventCharacterInfo(int Id, string Name, int Level, int Job, int MapId);

/// <summary>P178：事件腳本看到的隊伍（Java MapleParty 子集）。</summary>
public sealed record EventPartyInfo(int Id, int LeaderId, IReadOnlyList<int> MemberIds);

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

    /// <summary>P178：單一玩家 environmentChange（eim.broadcastPacket 的 showEffect／playSound／musicChange）。</summary>
    void SendEnvironment(int characterId, int mode, string name);

    /// <summary>P178：玩家所在隊伍（<c>eim.isLeader</c>、<c>chr.getParty()</c>）；無隊伍回 null。</summary>
    EventPartyInfo? GetParty(int characterId);

    /// <summary>全地圖 <c>broadcastMessage(type, msg)</c>。</summary>
    void BroadcastNotice(int mapId, int type, string message);

    /// <summary>全地圖 environmentChange（3 showEffect、4 playSound、6 musicChange）。</summary>
    void BroadcastEnvironment(int mapId, int mode, string name);

    /// <summary>全地圖 getClock。</summary>
    void BroadcastClock(int mapId, int seconds);

    /// <summary>Java <c>map.resetFully()</c>：清怪、重生出生點怪物、重設反應堆。</summary>
    void ResetMap(int mapId);

    void KillAllMonsters(int mapId, bool animate);

    /// <summary>P185：Java <c>map.setReactorState(state)</c>（resetReactors＝0、setReactorState()＝1）。</summary>
    void SetReactorState(int mapId, byte state);

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

    /// <summary>P178：角色目前所屬事件實例（Java <c>chr.getEventInstance()</c>）；腳本引擎層型別，無則 null。</summary>
    object? GetEventInstance(int characterId);

    // ── P179：遊戲流程掛勾（呼叫端不可持有 field 鎖：腳本可能回頭操作地圖）────────────────────────
    /// <summary>Java <c>changeMapInternal</c> 開頭：<c>eim.changedMap(chr, to.getId())</c>（換圖前）。</summary>
    void OnChangedMap(int characterId, int mapId);

    /// <summary>Java <c>MapleMap.addPlayer</c>：eim 計時中 → getClock(剩餘秒)；否則 null。</summary>
    int? GetTimeLeftSeconds(int characterId);

    /// <summary>Java <c>playerDead</c> → <c>eim.playerKilled</c>。</summary>
    void OnPlayerDead(int characterId);

    /// <summary>Java <c>ChangeMap</c> 死亡復活：<c>eim.revivePlayer</c>（腳本回 boolean，否則 true）。</summary>
    bool OnPlayerRevive(int characterId);

    /// <summary>Java <c>MapleClient.disconnect</c> → <c>eim.playerDisconnected</c>。</summary>
    void OnPlayerDisconnected(int characterId);

    /// <summary>
    /// Java <c>MapleMonster.killBy</c>：最高傷害者領經驗時 <c>monsterKilled</c>（怪物的實例優先，否則該玩家的實例），
    /// 之後怪物登記的實例 <c>unregisterMonster</c>（清空時 allMonstersDead）。
    /// </summary>
    void OnMonsterKilled(int mapId, int objectId, int monsterId, int? highestDamageCharacterId);

    /// <summary>Java PartyHandler 離隊（非隊長）→ <c>eim.leftParty(chr)</c>。</summary>
    void OnLeftParty(int characterId);

    /// <summary>Java PartyHandler 隊長離隊／踢出在線隊員 → <c>eim.disbandParty()</c>。</summary>
    void OnDisbandParty(int characterId);
}
