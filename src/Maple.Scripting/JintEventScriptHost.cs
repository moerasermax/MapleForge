using Jint;
using Jint.Native;
using Jint.Native.Function;
using Maple.Application.Events;
using Microsoft.Extensions.Logging;

namespace Maple.Scripting;

/// <summary>P177：事件腳本設定（scripts/event 目錄與要載入的事件名稱）。</summary>
public sealed class EventScriptOptions
{
    public string ScriptsDirectory { get; set; } = "scripts";

    public IReadOnlyList<string> Events { get; set; } = [];
}

/// <summary>
/// P177：Java <c>EventScriptManager</c> 的 Jint 版。每個事件一個引擎與一把鎖；腳本拿到 <c>em</c>（<see cref="EventManagerApi"/>），
/// <c>em.schedule(fn, delay)</c> 進排程表由 <see cref="Tick"/> 執行。世界操作走 <see cref="IEventWorld"/>（不阻塞）。
/// </summary>
public sealed class JintEventScriptHost : IEventScriptHost
{
    private readonly EventScriptOptions _options;
    private readonly IEventWorld _world;
    private readonly PortalScriptOverrideRegistry _portals;
    private readonly ILogger<JintEventScriptHost> _log;
    private readonly Dictionary<string, EventRuntime> _events = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, EventInstanceApi> _playerInstances = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int MapId, int ObjectId), MonsterTemplate> _trackedMobs = new();
    private bool _started;

    public JintEventScriptHost(
        EventScriptOptions options,
        IEventWorld world,
        PortalScriptOverrideRegistry portals,
        ILogger<JintEventScriptHost> log,
        TimeProvider? time = null)
    {
        Clock = time ?? TimeProvider.System;
        _options = options;
        _world = world;
        _portals = portals;
        _log = log;
    }

    internal TimeProvider Clock { get; }

    internal IEventWorld World => _world;

    internal PortalScriptOverrideRegistry Portals => _portals;

    internal ILogger Log => _log;

    public void Start(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        var dir = Path.Combine(_options.ScriptsDirectory, "event");
        foreach (var name in _options.Events.Distinct(StringComparer.Ordinal))
        {
            var path = Path.Combine(dir, $"{name}.js");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !File.Exists(path))
            {
                _log.LogDebug("[EventScript] 找不到事件腳本 {Name}", name);
                continue;
            }

            var runtime = new EventRuntime(this, name);
            try
            {
                runtime.Load(JintNpcScriptFactory.Preprocess(File.ReadAllText(path)));
                lock (_gate)
                {
                    _events[name] = runtime;
                }

                runtime.Invoke(now, "init");
            }
            catch (Exception ex)
            {
                _log.LogWarning("[EventScript] {Name} 載入/init 失敗：{Msg}", name, ex.Message);
            }
        }
    }

    public void Tick(DateTimeOffset now)
    {
        EventRuntime[] runtimes;
        lock (_gate)
        {
            runtimes = _events.Values.ToArray();
        }

        foreach (var runtime in runtimes)
        {
            runtime.RunDue(now);
        }
    }

    public bool HasEvent(string name)
    {
        lock (_gate)
        {
            return _events.ContainsKey(name);
        }
    }

    public object? GetEventManager(string name)
    {
        lock (_gate)
        {
            return _events.TryGetValue(name, out var runtime) ? runtime.Api : null;
        }
    }

    internal EventManagerApi? FindEventManager(string name) => GetEventManager(name) as EventManagerApi;

    public object? GetEventInstance(int characterId)
        => _playerInstances.TryGetValue(characterId, out var eim) && !eim.Disposed ? eim : null;

    internal void SetPlayerInstance(int characterId, EventInstanceApi eim) => _playerInstances[characterId] = eim;

    private EventInstanceApi? InstanceOf(int characterId) => GetEventInstance(characterId) as EventInstanceApi;

    public void OnChangedMap(int characterId, int mapId) => InstanceOf(characterId)?.OnChangedMap(characterId, mapId);

    public int? GetTimeLeftSeconds(int characterId)
        => InstanceOf(characterId) is { } eim && eim.isTimerStarted() ? (int)(eim.getTimeLeft() / 1000) : null;

    public void OnPlayerDead(int characterId) => InstanceOf(characterId)?.OnPlayerDead(characterId);

    public bool OnPlayerRevive(int characterId) => InstanceOf(characterId)?.OnPlayerRevive(characterId) ?? false;

    public void OnPlayerDisconnected(int characterId) => InstanceOf(characterId)?.OnPlayerDisconnected(characterId);

    public void OnMonsterKilled(int mapId, int objectId, int monsterId, int? highestDamageCharacterId)
    {
        _trackedMobs.TryRemove((mapId, objectId), out var tracked);
        if (highestDamageCharacterId is { } killer)
        {
            (tracked?.EventInstance ?? InstanceOf(killer))?.OnMonsterKilled(killer, monsterId);
        }

        if (tracked?.EventInstance is { } owner)
        {
            owner.unregisterMonster(tracked);
        }
    }

    public void OnLeftParty(int characterId)
    {
        if (InstanceOf(characterId) is { } eim)
        {
            eim.leftParty(new EventCharacterApi(eim.Runtime, characterId));
        }
    }

    public void OnDisbandParty(int characterId) => InstanceOf(characterId)?.disbandParty();

    /// <summary>只在角色仍屬於該實例時清除（避免舊實例清掉新登記）。</summary>
    internal void ClearPlayerInstance(int characterId, EventInstanceApi eim)
        => _playerInstances.TryRemove(new KeyValuePair<int, EventInstanceApi>(characterId, eim));

    /// <summary>已生成（有 objectId）且登記了實例的怪物，供 P179 擊殺掛勾查詢。</summary>
    internal void TrackMonster(MonsterTemplate mob)
    {
        if (mob.ObjectId > 0 && mob.EventInstance is not null)
        {
            _trackedMobs[(mob.MapId, mob.ObjectId)] = mob;
        }
    }


}

/// <summary>P177：一個事件腳本的執行期（引擎、鎖、屬性、排程）。</summary>
internal sealed class EventRuntime
{
    private readonly object _lock = new();
    private readonly object _scheduleLock = new();
    private readonly Dictionary<string, EventInstanceApi> _instances = new(StringComparer.Ordinal);
    private DateTimeOffset? _invokeNow;
    private readonly List<(DateTimeOffset Due, long Seq, string Function, object[] Args, ScheduleHandle Handle)> _schedule = new();
    private Engine? _engine;
    private long _seq;

    public EventRuntime(JintEventScriptHost host, string name)
    {
        Host = host;
        Name = name;
        Api = new EventManagerApi(this);
    }

    public JintEventScriptHost Host { get; }

    public string Name { get; }

    public EventManagerApi Api { get; }

    public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>目前時間：腳本呼叫中為該次呼叫的時間（tick 時間），否則取主機時鐘（NPC 端直接呼叫 em／eim）。</summary>
    public DateTimeOffset Now => _invokeNow ?? Host.Clock.GetUtcNow();

    /// <summary>em.newInstance／getInstance 的實例表（Java EventManager.instances）。</summary>
    public Dictionary<string, EventInstanceApi> Instances => _instances;

    /// <summary>以 <see cref="Now"/> 呼叫腳本函式（NPC 端或 eim 內部觸發）。</summary>
    public JsValue InvokeNow(string function, params object?[] args) => Invoke(Now, function, args!);

    public void Load(string source)
    {
        var engine = new Engine(o => o.LimitRecursion(64).MaxStatements(500_000).TimeoutInterval(TimeSpan.FromSeconds(2)));
        engine.SetValue("load", new Action<object?>(_ => { }));
        engine.SetValue("importPackage", new Action<object?>(_ => { }));
        engine.SetValue("Packages", new { });
        engine.Execute(JintNpcScriptFactory.JavaShim);
        engine.SetValue("MaplePacketCreator", new PacketCreatorShim());
        engine.SetValue("MapleLifeFactory", new LifeFactoryShim());
        engine.SetValue("em", Api);
        engine.Execute(source);
        _engine = engine;
    }

    /// <summary>在事件鎖內呼叫腳本函式（不存在則略過）。腳本例外記錄後吞掉（Java 同樣只印錯誤）。</summary>
    public JsValue Invoke(DateTimeOffset now, string function, params object[] args)
    {
        lock (_lock)
        {
            var previous = _invokeNow;
            _invokeNow = now;
            try
            {
                if (_engine?.GetValue(function) is not Function callable)
                {
                    return JsValue.Undefined;
                }

                return _engine.Invoke(callable, args);
            }
            catch (Exception ex)
            {
                Host.Log.LogWarning("[EventScript] {Name}.{Function} 失敗：{Msg}", Name, function, ex.Message);
                return JsValue.Undefined;
            }
            finally
            {
                _invokeNow = previous;
            }
        }
    }

    public ScheduleHandle Schedule(string function, long delayMs, params object[] args)
    {
        var handle = new ScheduleHandle();
        lock (_scheduleLock)
        {
            _schedule.Add((Now.AddMilliseconds(delayMs), _seq++, function, args, handle));
        }

        return handle;
    }

    public void RunDue(DateTimeOffset now)
    {
        while (true)
        {
            (DateTimeOffset Due, long Seq, string Function, object[] Args, ScheduleHandle Handle) next;
            lock (_scheduleLock)
            {
                var index = -1;
                for (var i = 0; i < _schedule.Count; i++)
                {
                    if (_schedule[i].Due <= now && (index < 0 || (_schedule[i].Due, _schedule[i].Seq).CompareTo((_schedule[index].Due, _schedule[index].Seq)) < 0))
                    {
                        index = i;
                    }
                }

                if (index < 0)
                {
                    return;
                }

                next = _schedule[index];
                _schedule.RemoveAt(index);
            }

            if (!next.Handle.Cancelled)
            {
                Invoke(now, next.Function, next.Args);
            }
        }
    }
}

/// <summary>Java <c>ScheduledFuture</c> 子集：<c>cancel(bool)</c>。</summary>
public sealed class ScheduleHandle
{
    public bool Cancelled { get; private set; }

    public bool cancel(bool mayInterrupt = false)
    {
        Cancelled = true;
        return true;
    }
}

/// <summary>P177：腳本的 <c>em</c>（Java EventManager 子集）。也供 NPC/portal 腳本 <c>cm.getEventManager(name)</c> 使用。</summary>
public sealed class EventManagerApi
{
    private readonly EventRuntime _runtime;

    internal EventManagerApi(EventRuntime runtime) => _runtime = runtime;

    internal EventRuntime Runtime => _runtime;

    public string getName() => _runtime.Name;

    public void setProperty(string key, JsValue value)
    {
        lock (_runtime.Properties)
        {
            _runtime.Properties[key] = ToPropertyString(value);
        }
    }

    /// <summary>Java Properties 存字串；Rhino 數字 1 轉成 "1"（非 "1.0"）。</summary>
    internal static string ToPropertyString(JsValue value)
        => value.IsNull() || value.IsUndefined() ? "null" : value.ToString();

    public string? getProperty(string key)
    {
        lock (_runtime.Properties)
        {
            return _runtime.Properties.TryGetValue(key, out var value) ? value : null;
        }
    }

    public ScheduleHandle schedule(string function, double delay) => _runtime.Schedule(function, (long)delay);

    public ScheduleHandle scheduleAtTimestamp(string function, double timestamp)
        => _runtime.Schedule(function, Math.Max(0, (long)timestamp - _runtime.Now.ToUnixTimeMilliseconds()));

    public ChannelApi getChannelServer() => new(_runtime);

    public MapFactoryApi getMapFactory() => new(_runtime);

    public MonsterTemplate getMonster(int id) => new(id);

    public OverrideMonsterStatsShim newMonsterStats() => new();

    /// <summary>
    /// P201：Java scheduleSystemEvent()：逐一嘗試開 GM 系統活動（MapleEvent：椰子、雪球、OX…）。MapleForge 尚未移植 MapleEvent，
    /// 沒有可開的活動 → false（Java 在所有活動都在進行或頻道不存在時同樣回 false）。
    /// </summary>
    public bool scheduleSystemEvent()
    {
        _runtime.Host.Log.LogInformation("[EventScript] {Name}.scheduleSystemEvent：GM 系統活動尚未移植，略過", _runtime.Name);
        return false;
    }

    public bool scheduleSystemEvent(string type) => scheduleSystemEvent();

    // ── P178：事件實例 ───────────────────────────────────────────────────────────────
    public EventInstanceApi newInstance(string name)
    {
        var eim = new EventInstanceApi(_runtime, name);
        lock (_runtime.Instances)
        {
            _runtime.Instances[name] = eim;
        }

        return eim;
    }

    public EventInstanceApi? getInstance(string name)
    {
        lock (_runtime.Instances)
        {
            return _runtime.Instances.GetValueOrDefault(name);
        }
    }

    public EventInstanceList getInstances()
    {
        lock (_runtime.Instances)
        {
            return new(_runtime.Instances.Values.ToArray());
        }
    }

    /// <summary>Java disposeInstance：移除；沒有實例時 state → "0"、leader "false" → "true"。</summary>
    public void disposeInstance(string name)
    {
        bool empty;
        lock (_runtime.Instances)
        {
            _runtime.Instances.Remove(name);
            empty = _runtime.Instances.Count == 0;
        }

        if (!empty)
        {
            return;
        }

        if (getProperty("state") is not null)
        {
            setProperty("state", "0");
        }

        if (getProperty("leader") == "false")
        {
            setProperty("leader", "true");
        }
    }

    /// <summary>Java startInstance()：setup(null)。</summary>
    public void startInstance() => _runtime.InvokeNow("setup", (object?)null);

    /// <summary>Java startInstance(MapleParty, MapleMap)：setup(partyId) → registerParty(party, map)。</summary>
    public void startInstance(IScriptPartyRef party, IScriptMapRef map)
        => Setup(party.PartyId)?.registerParty(party, map);

    /// <summary>Java startInstance(MapleCharacter)：setup(null) → registerPlayer。</summary>
    public void startInstance(IScriptCharacterRef character) => Setup(null)?.registerPlayer(character);

    /// <summary>Java startInstance(MapleCharacter, String leader)（GPQ）：registerPlayer 後設 leader。</summary>
    public void startInstance(IScriptCharacterRef character, JsValue leader)
    {
        if (Setup(null) is { } eim)
        {
            eim.registerPlayer(character);
            eim.setProperty("leader", leader);
        }
    }

    /// <summary>Java startInstance(EventInstanceManager, String leader)：setup(eim)。</summary>
    public void startInstance(EventInstanceApi eim, string leader)
    {
        _runtime.InvokeNow("setup", eim);
        eim.setProperty("leader", leader);
    }

    /// <summary>
    /// Java startInstance(String mapid, chr)：setup(mapid) → registerCarnivalParty。擂台（CPQ）隊伍尚未移植：
    /// 先以 registerParty（隊伍不存在時 registerPlayer）處理。
    /// </summary>
    public void startInstance(string mapId, IScriptCharacterRef character) => RegisterPartyOf(Setup(mapId), character);

    /// <summary>Java startInstance_Party(mapid, chr)：setup(mapid) → registerParty(chr.getParty(), chr.getMap())。</summary>
    public void startInstance_Party(string mapId, IScriptCharacterRef character) => RegisterPartyOf(Setup(mapId), character);

    /// <summary>Java startInstance_CharID(chr)：setup(chr.getId()) → registerPlayer。</summary>
    public void startInstance_CharID(IScriptCharacterRef character) => Setup(character.CharacterId)?.registerPlayer(character);

    /// <summary>Java startInstance_NoID(party, map)：setup(null) → registerParty。</summary>
    public void startInstance_NoID(IScriptPartyRef party, IScriptMapRef map) => Setup(null)?.registerParty(party, map);

    // ── P190：遠征隊 ─────────────────────────────────────────────────────────────────
    public void startInstance(IScriptSquadRef squad, IScriptMapRef map) => StartSquad(squad, map, -1, null);

    public void startInstance(IScriptSquadRef squad, IScriptMapRef map, int questId) => StartSquad(squad, map, questId, null);

    public void startInstance(IScriptSquadRef squad, IScriptMapRef map, int questId, string bossName) => StartSquad(squad, map, questId, bossName);

    /// <summary>
    /// Java startInstance(MapleSquad, map, questID, bossname)：已清除（狀態 0）不開；CWKPQ 需 5 種職業（GM 系統尚無，一律檢查）；
    /// setup(隊長名) → eim.registerSquad。
    /// </summary>
    private void StartSquad(IScriptSquadRef squadRef, IScriptMapRef map, int questId, string? bossName)
    {
        var squad = squadRef.Squad;
        if (squad.Status == 0)
        {
            return;
        }

        if (_runtime.Name == "CWKPQ" && squad.GetJobs().Count < 5)
        {
            if (_runtime.Host.World.FindCharacterByName(squad.LeaderName) is { } leader)
            {
                _runtime.Host.World.SendNotice(leader.Id, 5, "The squad requires members from every type of job.");
            }

            return;
        }

        Setup(squad.LeaderName)?.registerSquad(squadRef, map, questId, bossName);
    }

    /// <summary>Java getDisconnected：isDisconnected(chr) 且 getPlayerCount() &gt; 0 的第一個實例。</summary>
    internal EventInstanceApi? FindDisconnectedInstance(int characterId)
    {
        lock (_runtime.Instances)
        {
            return _runtime.Instances.Values.FirstOrDefault(e => e.IsDisconnectedId(characterId) && e.getPlayerCount() > 0);
        }
    }

    /// <summary>Java warpAllPlayer(from, to)：from 圖上所有人 → to 圖 portal 0。</summary>
    public void warpAllPlayer(int from, int to)
    {
        var world = _runtime.Host.World;
        foreach (var id in world.GetCharacterIdsOnMap(from).ToArray())
        {
            world.Warp(id, to, 0);
        }
    }

    private EventInstanceApi? Setup(object? argument)
    {
        var result = _runtime.InvokeNow("setup", argument);
        return result.IsObject() ? result.ToObject() as EventInstanceApi : null;
    }

    private void RegisterPartyOf(EventInstanceApi? eim, IScriptCharacterRef character)
    {
        if (eim is null)
        {
            return;
        }

        var world = _runtime.Host.World;
        if (world.GetParty(character.CharacterId) is { } party && world.GetCharacter(character.CharacterId) is { } info)
        {
            eim.registerParty(new PartyRef(party), new MapRef(info.MapId));
        }
        else
        {
            eim.registerPlayer(character);
        }
    }

    private sealed record PartyRef(EventPartyInfo Party) : IScriptPartyRef
    {
        public int PartyId => Party.Id;

        public IReadOnlyList<int> MemberIds => Party.MemberIds;
    }

    private sealed record MapRef(int MapId) : IScriptMapRef;

    /// <summary>Java <c>broadcastYellowMsg</c>：目前以黃字公告（type 6）送給事件相關地圖以外無法定位，先記錄。</summary>
    public void broadcastYellowMsg(string message) => _runtime.Host.Log.LogInformation("[EventScript] {Name} 公告：{Msg}", _runtime.Name, message);
}

/// <summary>Java ChannelServer 子集：getMapFactory／getEventSM。</summary>
public sealed class ChannelApi
{
    private readonly EventRuntime _runtime;

    internal ChannelApi(EventRuntime runtime) => _runtime = runtime;

    public MapFactoryApi getMapFactory() => new(_runtime);

    public ChannelApi getEventSM() => this;

    public EventManagerApi? getEventManager(string name) => _runtime.Host.FindEventManager(name);
}

/// <summary>Java MapleMapFactory 子集：getMap。</summary>
public sealed class MapFactoryApi
{
    private readonly EventRuntime _runtime;

    internal MapFactoryApi(EventRuntime runtime) => _runtime = runtime;

    public EventMapApi getMap(int mapId) => new(_runtime, mapId);
}

/// <summary>P177：事件腳本的 MapleMap 子集（真實共用地圖）。</summary>
public sealed class EventMapApi : IScriptMapRef
{
    private readonly EventRuntime _runtime;

    internal EventMapApi(EventRuntime runtime, int mapId) => (_runtime, MapId) = (runtime, mapId);

    public int MapId { get; }

    private IEventWorld World => _runtime.Host.World;

    public int getId() => MapId;

    public EventPortalApi getPortal(string name) => new(_runtime, MapId, name, -1);

    public EventPortalApi getPortal(int id) => new(_runtime, MapId, string.Empty, id);

    public EventCharacterList getCharacters() => new(_runtime, World.GetCharacterIdsOnMap(MapId));

    public EventCharacterList getCharactersThreadsafe() => getCharacters();

    public int getCharactersSize() => World.GetCharacterIdsOnMap(MapId).Count;

    public EventCharacterApi? getCharacterById(int id)
        => World.GetCharacterIdsOnMap(MapId).Contains(id) ? new EventCharacterApi(_runtime, id) : null;

    public void broadcastMessage(PacketDescriptor packet) => packet.Broadcast(World, MapId);

    public void resetFully() => World.ResetMap(MapId);

    /// <summary>P185：Java resetReactors() = setReactorState(0)（只動反應堆，不清怪）。</summary>
    public void resetReactors() => World.SetReactorState(MapId, 0);

    public void setReactorState() => World.SetReactorState(MapId, 1);

    public void setReactorState(int state) => World.SetReactorState(MapId, (byte)state);

    public void killAllMonsters(bool animate) => World.KillAllMonsters(MapId, animate);

    public void spawnMonsterOnGroundBelow(MonsterTemplate monster, JsValue point)
    {
        var (x, y) = ReadPoint(point);
        monster.MapId = MapId;
        monster.ObjectId = World.SpawnMonster(MapId, monster.Id, x, y);
        _runtime.Host.TrackMonster(monster);
    }

    public EventCountList getAllMonstersThreadsafe() => new(World.GetMonsterIds(MapId).Count);

    public int countMonsterById(int id) => World.GetMonsterIds(MapId).Count(m => m == id);

    // Java 有但 MapleForge 尚無對應狀態的旗標／洗牌：no-op（記錄於任務歷程）。
    public void shuffleReactors() { }

    public void toggleDrops() { }

    public void setDocked(bool docked) { }

    internal static (int X, int Y) ReadPoint(JsValue point)
    {
        if (point.IsObject())
        {
            var obj = point.AsObject();
            return ((int)obj.Get("x").AsNumber(), (int)obj.Get("y").AsNumber());
        }

        return (0, 0);
    }
}

/// <summary>MaplePortal 子集：getId／getName／setScriptName（動態 portal 腳本）。</summary>
public sealed class EventPortalApi
{
    private readonly EventRuntime _runtime;
    private readonly int _mapId;

    internal EventPortalApi(EventRuntime runtime, int mapId, string name, int id) => (_runtime, _mapId, Name, Id) = (runtime, mapId, name, id);

    public string Name { get; }

    public int Id { get; }

    public int getId() => Id < 0 ? 0 : Id;

    public string getName() => Name;

    public void setScriptName(string script)
    {
        if (Name.Length > 0)
        {
            _runtime.Host.Portals.Set(_mapId, Name, script);
        }
    }
}

/// <summary>Java List&lt;MapleCharacter&gt;／Collection 子集。</summary>
public sealed class EventCharacterList
{
    private readonly EventRuntime _runtime;
    private readonly IReadOnlyList<int> _ids;

    internal EventCharacterList(EventRuntime runtime, IReadOnlyList<int> ids) => (_runtime, _ids) = (runtime, ids);

    public int size() => _ids.Count;

    public bool isEmpty() => _ids.Count == 0;

    public EventCharacterApi get(int index) => new(_runtime, _ids[index]);

    public EventCharacterIterator iterator() => new(this);

    internal IReadOnlyList<int> Ids => _ids;
}

public sealed class EventCharacterIterator
{
    private readonly EventCharacterList _list;
    private int _next;

    internal EventCharacterIterator(EventCharacterList list) => _list = list;

    public bool hasNext() => _next < _list.size();

    public EventCharacterApi next() => _list.get(_next++);
}

/// <summary>只需要 size() 的清單（怪物數量等）。</summary>
public sealed class EventCountList
{
    private readonly int _count;

    internal EventCountList(int count) => _count = count;

    public int size() => _count;

    public bool isEmpty() => _count == 0;
}

/// <summary>P177：事件腳本的 MapleCharacter 子集。</summary>
public sealed class EventCharacterApi : IScriptCharacterRef
{
    private readonly EventRuntime _runtime;

    internal EventCharacterApi(EventRuntime runtime, int id) => (_runtime, Id) = (runtime, id);

    public int Id { get; }

    int IScriptCharacterRef.CharacterId => Id;

    private EventCharacterInfo? Info => _runtime.Host.World.GetCharacter(Id);

    public object? getEventInstance() => _runtime.Host.GetEventInstance(Id);

    public EventPartyApi? getParty() => _runtime.Host.World.GetParty(Id) is { } party ? new(party) : null;

    public int getId() => Id;

    public string getName() => Info?.Name ?? string.Empty;

    public int getLevel() => Info?.Level ?? 0;

    public int getJob() => Info?.Job ?? 0;

    public int getMapId() => Info?.MapId ?? 0;

    public EventMapApi getMap() => new(_runtime, getMapId());

    public void changeMap(EventMapApi map, EventPortalApi? portal) => _runtime.Host.World.Warp(Id, map.MapId, portal?.getId() ?? 0);

    public void changeMap(EventMapApi map) => _runtime.Host.World.Warp(Id, map.MapId, 0);

    public void dropMessage(int type, string message) => _runtime.Host.World.SendNotice(Id, type, message);

    public void dropMessage(string message) => _runtime.Host.World.SendNotice(Id, 6, message);

    /// <summary>Java 自訂 PQ 紀錄（tryPartyQuest）：MapleForge 尚無，no-op。</summary>
    public void tryPartyQuest(int id) { }

    public override bool Equals(object? obj) => obj is EventCharacterApi other && other.Id == Id;

    public override int GetHashCode() => Id;
}

/// <summary>P178：事件腳本看到的 MapleParty 子集（getId／getLeader().getId()／getMembers().size()）。</summary>
public sealed class EventPartyApi : IScriptPartyRef
{
    private readonly EventPartyInfo _party;

    internal EventPartyApi(EventPartyInfo party) => _party = party;

    int IScriptPartyRef.PartyId => _party.Id;

    IReadOnlyList<int> IScriptPartyRef.MemberIds => _party.MemberIds;

    public int getId() => _party.Id;

    public int getLeaderId() => _party.LeaderId;

    public EventCountList getMembers() => new(_party.MemberIds.Count);
}

/// <summary>MapleLifeFactory.getMonster(id) 的結果；生成後帶 mapId／objectId（P178 實例怪物登記用）。</summary>
public sealed class MonsterTemplate
{
    public MonsterTemplate(int id) => Id = id;

    public int Id { get; }

    public int MapId { get; internal set; }

    public int ObjectId { get; internal set; } = -1;

    public EventInstanceApi? EventInstance { get; internal set; }

    public OverrideMonsterStatsShim? OverrideStats { get; private set; }

    public int getId() => Id;

    public int getObjectId() => ObjectId;

    public void setOverrideStats(OverrideMonsterStatsShim stats) => OverrideStats = stats;
}

public sealed class LifeFactoryShim
{
    public MonsterTemplate getMonster(int id) => new(id);
}

/// <summary>腳本直接建的封包（MaplePacketCreator.xxx）→ 描述物件，由 map.broadcastMessage 轉成世界操作。</summary>
public abstract class PacketDescriptor
{
    internal abstract void Broadcast(IEventWorld world, int mapId);

    /// <summary>P178：單一玩家（eim.broadcastPacket）。</summary>
    internal abstract void SendTo(IEventWorld world, int characterId);
}

internal sealed class NoticePacket(int type, string message) : PacketDescriptor
{
    internal override void Broadcast(IEventWorld world, int mapId) => world.BroadcastNotice(mapId, type, message);

    internal override void SendTo(IEventWorld world, int characterId) => world.SendNotice(characterId, type, message);
}

internal sealed class ClockPacket(int seconds) : PacketDescriptor
{
    internal override void Broadcast(IEventWorld world, int mapId) => world.BroadcastClock(mapId, seconds);

    internal override void SendTo(IEventWorld world, int characterId) => world.SendClock(characterId, seconds);
}

internal sealed class EnvironmentPacket(int mode, string name) : PacketDescriptor
{
    internal override void Broadcast(IEventWorld world, int mapId) => world.BroadcastEnvironment(mapId, mode, name);

    internal override void SendTo(IEventWorld world, int characterId) => world.SendEnvironment(characterId, mode, name);
}

/// <summary>尚未支援的封包：廣播時忽略。</summary>
internal sealed class UnsupportedPacket : PacketDescriptor
{
    internal override void Broadcast(IEventWorld world, int mapId) { }

    internal override void SendTo(IEventWorld world, int characterId) { }
}

/// <summary>P177：Java MaplePacketCreator 在事件腳本中常用的方法（broadcastMessage／getClock／musicChange…）。</summary>
public sealed class PacketCreatorShim
{
    public PacketDescriptor broadcastMessage(int type, string message) => new NoticePacket(type, message);

    public PacketDescriptor serverNotice(int type, string message) => new NoticePacket(type, message);

    public PacketDescriptor getClock(int seconds) => new ClockPacket(seconds);

    public PacketDescriptor musicChange(string song) => new EnvironmentPacket(6, song);

    public PacketDescriptor showEffect(string effect) => new EnvironmentPacket(3, effect);

    public PacketDescriptor playSound(string sound) => new EnvironmentPacket(4, sound);

    public PacketDescriptor environmentChange(string env, int mode) => new EnvironmentPacket(mode, env);

    public PacketDescriptor boatPacket(bool docked) => new UnsupportedPacket();

    public PacketDescriptor boatEffect(int effect) => new UnsupportedPacket();
}
