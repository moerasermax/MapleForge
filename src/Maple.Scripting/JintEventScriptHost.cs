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
    private bool _started;

    public JintEventScriptHost(EventScriptOptions options, IEventWorld world, PortalScriptOverrideRegistry portals, ILogger<JintEventScriptHost> log)
    {
        _options = options;
        _world = world;
        _portals = portals;
        _log = log;
    }

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
}

/// <summary>P177：一個事件腳本的執行期（引擎、鎖、屬性、排程）。</summary>
internal sealed class EventRuntime
{
    private readonly object _lock = new();
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

    /// <summary>目前執行中的時間（排程起點）。</summary>
    public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;

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
            Now = now;
            if (_engine?.GetValue(function) is not Function callable)
            {
                return JsValue.Undefined;
            }

            try
            {
                return _engine.Invoke(callable, args);
            }
            catch (Exception ex)
            {
                Host.Log.LogWarning("[EventScript] {Name}.{Function} 失敗：{Msg}", Name, function, ex.Message);
                return JsValue.Undefined;
            }
        }
    }

    public ScheduleHandle Schedule(string function, long delayMs, params object[] args)
    {
        var handle = new ScheduleHandle();
        lock (_lock)
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
            lock (_lock)
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
            _runtime.Properties[key] = value.IsNull() || value.IsUndefined() ? "null" : value.ToString();
        }
    }

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
public sealed class EventMapApi
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

    public void resetReactors() => World.ResetMap(MapId);

    public void killAllMonsters(bool animate) => World.KillAllMonsters(MapId, animate);

    public void spawnMonsterOnGroundBelow(MonsterTemplate monster, JsValue point)
    {
        var (x, y) = ReadPoint(point);
        World.SpawnMonster(MapId, monster.Id, x, y);
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
public sealed class EventCharacterApi
{
    private readonly EventRuntime _runtime;

    internal EventCharacterApi(EventRuntime runtime, int id) => (_runtime, Id) = (runtime, id);

    public int Id { get; }

    private EventCharacterInfo? Info => _runtime.Host.World.GetCharacter(Id);

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

/// <summary>MapleLifeFactory.getMonster(id) 的結果（只帶怪物 ID）。</summary>
public sealed class MonsterTemplate
{
    public MonsterTemplate(int id) => Id = id;

    public int Id { get; }

    public int getId() => Id;
}

public sealed class LifeFactoryShim
{
    public MonsterTemplate getMonster(int id) => new(id);
}

/// <summary>腳本直接建的封包（MaplePacketCreator.xxx）→ 描述物件，由 map.broadcastMessage 轉成世界操作。</summary>
public abstract class PacketDescriptor
{
    internal abstract void Broadcast(IEventWorld world, int mapId);
}

internal sealed class NoticePacket(int type, string message) : PacketDescriptor
{
    internal override void Broadcast(IEventWorld world, int mapId) => world.BroadcastNotice(mapId, type, message);
}

internal sealed class ClockPacket(int seconds) : PacketDescriptor
{
    internal override void Broadcast(IEventWorld world, int mapId) => world.BroadcastClock(mapId, seconds);
}

internal sealed class EnvironmentPacket(int mode, string name) : PacketDescriptor
{
    internal override void Broadcast(IEventWorld world, int mapId) => world.BroadcastEnvironment(mapId, mode, name);
}

/// <summary>尚未支援的封包：廣播時忽略。</summary>
internal sealed class UnsupportedPacket : PacketDescriptor
{
    internal override void Broadcast(IEventWorld world, int mapId) { }
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
