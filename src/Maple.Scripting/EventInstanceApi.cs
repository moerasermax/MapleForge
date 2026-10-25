using Jint;
using Jint.Native;
using Maple.Application.Events;

namespace Maple.Scripting;

/// <summary>
/// P178：Java <c>EventInstanceManager</c>（eim）。玩家以角色 ID 登記；地圖一律是真實共用地圖
/// （<c>createInstanceMap</c> 暫以 <c>setInstanceMap</c> 處理，副本地圖尚未支援）。dispose 後的行為照 Java：
/// getProperty 回 ""、getPlayers 空、其餘操作略過。
/// </summary>
public sealed class EventInstanceApi
{
    private readonly EventRuntime _runtime;
    private readonly object _lock = new();
    private readonly List<int> _players = [];
    private readonly List<int> _disconnected = [];
    private readonly List<MonsterTemplate> _mobs = [];
    private readonly Dictionary<int, int> _killCount = [];
    private readonly List<int> _mapIds = [];
    private readonly List<ScheduleHandle> _scheduled = [];
    private readonly Dictionary<string, string> _props = new(StringComparer.Ordinal);
    private ScheduleHandle? _eventTimer;
    private DateTimeOffset _timeStarted;
    private long _eventTime;

    internal EventInstanceApi(EventRuntime runtime, string name) => (_runtime, Name) = (runtime, name);

    public string Name { get; }

    public bool Disposed { get; private set; }

    private IEventWorld World => _runtime.Host.World;

    internal EventRuntime Runtime => _runtime;

    public string getName() => Name;

    public EventManagerApi getEventManager() => _runtime.Api;

    public MapFactoryApi getMapFactory() => new(_runtime);

    public ChannelApi getChannelServer() => new(_runtime);

    // ── 屬性 ─────────────────────────────────────────────────────────────────────
    public void setProperty(string key, JsValue value)
    {
        lock (_lock)
        {
            if (!Disposed)
            {
                _props[key] = EventManagerApi.ToPropertyString(value);
            }
        }
    }

    public string? setProperty(string key, JsValue value, bool prev)
    {
        lock (_lock)
        {
            if (Disposed)
            {
                return null;
            }

            _props.TryGetValue(key, out var old);
            _props[key] = EventManagerApi.ToPropertyString(value);
            return old;
        }
    }

    public string? getProperty(string key)
    {
        lock (_lock)
        {
            return Disposed ? string.Empty : _props.GetValueOrDefault(key);
        }
    }

    // ── 玩家 ─────────────────────────────────────────────────────────────────────
    /// <summary>Java registerPlayer：加入名單、設 eventInstance、呼叫 <c>playerEntry(eim, chr)</c>。</summary>
    public void registerPlayer(IScriptCharacterRef? chr)
    {
        if (chr is null || !AddPlayer(chr.CharacterId))
        {
            return;
        }

        _runtime.InvokeNow("playerEntry", this, new EventCharacterApi(_runtime, chr.CharacterId));
    }

    /// <summary>Java registerParty：隊員中「在該地圖上」的逐一 registerPlayer。</summary>
    public void registerParty(IScriptPartyRef party, IScriptMapRef map)
    {
        if (Disposed)
        {
            return;
        }

        var onMap = World.GetCharacterIdsOnMap(map.MapId);
        foreach (var id in party.MemberIds)
        {
            if (onMap.Contains(id))
            {
                registerPlayer(new EventCharacterApi(_runtime, id));
            }
        }
    }

    public void unregisterPlayer(IScriptCharacterRef chr)
    {
        lock (_lock)
        {
            UnregisterNoLock(chr.CharacterId);
        }
    }

    public EventCharacterList getPlayers()
    {
        lock (_lock)
        {
            return new(_runtime, Disposed ? [] : _players.ToArray());
        }
    }

    public int getPlayerCount()
    {
        lock (_lock)
        {
            return Disposed ? 0 : _players.Count;
        }
    }

    internal IReadOnlyList<int> PlayerIds
    {
        get
        {
            lock (_lock)
            {
                return _players.ToArray();
            }
        }
    }

    public bool isLeader(IScriptCharacterRef? chr)
        => chr is not null && World.GetParty(chr.CharacterId)?.LeaderId == chr.CharacterId;

    /// <summary>Java check()：全員 30~50 級。</summary>
    public bool check() => getPlayers().Ids.All(id => World.GetCharacter(id) is { Level: >= 30 and <= 50 });

    /// <summary>Java check1()：全員 51~120 級。</summary>
    public bool check1() => getPlayers().Ids.All(id => World.GetCharacter(id) is { Level: >= 51 and <= 120 });

    /// <summary>
    /// Java disposeIfPlayerBelow：人數 ≤ size 時全部 unregister、towarp &gt; 0 則傳到該圖 portal 0，然後 dispose；回傳是否已 dispose。
    /// </summary>
    public bool disposeIfPlayerBelow(int size, int towarp)
    {
        int[] warped;
        lock (_lock)
        {
            if (Disposed)
            {
                return true;
            }

            if (_players.Count > size)
            {
                return false;
            }

            warped = _players.ToArray();
            foreach (var id in warped)
            {
                UnregisterNoLock(id);
            }

            DisposeNoLock();
        }

        if (towarp > 0)
        {
            foreach (var id in warped)
            {
                World.Warp(id, towarp, 0);
            }
        }

        return true;
    }

    public void dispose()
    {
        lock (_lock)
        {
            DisposeNoLock();
        }
    }

    /// <summary>Java removePlayer → <c>playerExit(eim, chr)</c>。</summary>
    public void removePlayer(IScriptCharacterRef chr) => Call("playerExit", new EventCharacterApi(_runtime, chr.CharacterId));

    /// <summary>Java finishPQ → <c>clearPQ(eim)</c>。</summary>
    public void finishPQ() => Call("clearPQ");

    public void leftParty(IScriptCharacterRef chr) => Call("leftParty", new EventCharacterApi(_runtime, chr.CharacterId));

    public void disbandParty() => Call("disbandParty");

    public bool isDisconnected(IScriptCharacterRef chr)
    {
        lock (_lock)
        {
            return !Disposed && _disconnected.Contains(chr.CharacterId);
        }
    }

    public void removeDisconnected(int id)
    {
        lock (_lock)
        {
            _disconnected.Remove(id);
        }
    }

    public void broadcastPlayerMsg(int type, string message)
    {
        foreach (var id in getPlayers().Ids)
        {
            World.SendNotice(id, type, message);
        }
    }

    public void broadcastPacket(PacketDescriptor packet)
    {
        foreach (var id in getPlayers().Ids)
        {
            packet.SendTo(World, id);
        }
    }

    // ── 地圖 ─────────────────────────────────────────────────────────────────────
    public EventMapApi setInstanceMap(int mapId)
    {
        lock (_lock)
        {
            if (!Disposed)
            {
                _mapIds.Add(mapId);
            }
        }

        return new(_runtime, mapId);
    }

    /// <summary>副本地圖尚未支援：以真實地圖代替（同 setInstanceMap）。</summary>
    public EventMapApi? createInstanceMap(int mapId) => Disposed ? null : setInstanceMap(mapId);

    public EventMapApi? createInstanceMapS(int mapId) => createInstanceMap(mapId);

    /// <summary>Java getMapInstance：索引在已登記地圖數內 → 該地圖，否則視為真實 mapId。</summary>
    public EventMapApi? getMapInstance(int index)
    {
        lock (_lock)
        {
            if (Disposed)
            {
                return null;
            }

            return new(_runtime, index >= 0 && index < _mapIds.Count ? _mapIds[index] : index);
        }
    }

    // ── 怪物 ─────────────────────────────────────────────────────────────────────
    public void registerMonster(MonsterTemplate mob)
    {
        lock (_lock)
        {
            if (Disposed)
            {
                return;
            }

            _mobs.Add(mob);
        }

        mob.EventInstance = this;
        _runtime.Host.TrackMonster(mob);
    }

    /// <summary>Java unregisterMonster：移除後若清單已空 → <c>allMonstersDead(eim)</c>。</summary>
    public void unregisterMonster(MonsterTemplate mob)
    {
        mob.EventInstance = null;
        bool empty;
        lock (_lock)
        {
            if (Disposed || !_mobs.Remove(mob))
            {
                return;
            }

            empty = _mobs.Count == 0;
        }

        if (empty)
        {
            Call("allMonstersDead");
        }
    }

    public int getMobsCount()
    {
        lock (_lock)
        {
            return _mobs.Count;
        }
    }

    public int getKillCount(IScriptCharacterRef chr)
    {
        lock (_lock)
        {
            return Disposed ? 0 : _killCount.GetValueOrDefault(chr.CharacterId);
        }
    }

    // ── 計時 ─────────────────────────────────────────────────────────────────────
    public void startEventTimer(double time) => restartEventTimer(time);

    /// <summary>Java restartEventTimer：對成員送 getClock(秒)，time 毫秒後 <c>scheduledTimeout(eim)</c>。</summary>
    public void restartEventTimer(double time)
    {
        int[] players;
        lock (_lock)
        {
            if (Disposed)
            {
                return;
            }

            _timeStarted = _runtime.Now;
            _eventTime = (long)time;
            _eventTimer?.cancel(false);
            _eventTimer = _runtime.Schedule("scheduledTimeout", (long)time, this);
            _scheduled.Add(_eventTimer);
            players = _players.ToArray();
        }

        foreach (var id in players)
        {
            World.SendClock(id, (int)(time / 1000));
        }
    }

    public void stopEventTimer()
    {
        lock (_lock)
        {
            _eventTime = 0;
            _timeStarted = default;
            _eventTimer?.cancel(false);
        }
    }

    public bool isTimerStarted()
    {
        lock (_lock)
        {
            return _eventTime > 0 && _timeStarted != default;
        }
    }

    public double getTimeLeft()
    {
        lock (_lock)
        {
            return _eventTime - (_runtime.Now - _timeStarted).TotalMilliseconds;
        }
    }

    /// <summary>Java eim.schedule：delay 毫秒後 <c>methodName(eim)</c>（已 dispose 則不跑）。</summary>
    public void schedule(string methodName, double delay)
    {
        lock (_lock)
        {
            if (!Disposed)
            {
                _scheduled.Add(_runtime.Schedule(methodName, (long)delay, this));
            }
        }
    }

    // Java 的 Boss 任務點數、點數卡、成就：MapleForge 尚無對應資料，no-op。
    public void saveBossQuest(int points) { }

    public void saveNX(int points) { }

    public void applyBuff(IScriptCharacterRef chr, int itemId) { }

    // ── 世界事件掛勾（P179 由遊戲流程呼叫）──────────────────────────────────────────
    internal void OnMonsterKilled(int characterId, int monsterId)
    {
        if (Disposed)
        {
            return;
        }

        var inc = _runtime.InvokeNow("monsterValue", this, monsterId);
        lock (_lock)
        {
            if (!Disposed && inc.IsNumber())
            {
                _killCount[characterId] = _killCount.GetValueOrDefault(characterId) + (int)inc.AsNumber();
            }
        }
    }

    internal void OnChangedMap(int characterId, int mapId) => Call("changedMap", new EventCharacterApi(_runtime, characterId), mapId);

    internal void OnPlayerDead(int characterId) => Call("playerDead", new EventCharacterApi(_runtime, characterId));

    /// <summary>Java revivePlayer：腳本回傳 boolean 則用之，否則 true。</summary>
    internal bool OnPlayerRevive(int characterId)
    {
        if (Disposed)
        {
            return false;
        }

        var result = _runtime.InvokeNow("playerRevive", this, new EventCharacterApi(_runtime, characterId));
        return !result.IsBoolean() || result.AsBoolean();
    }

    /// <summary>
    /// Java playerDisconnected：ret = 腳本回傳（非數字 0）；記斷線、unregister；ret 0 → 沒人就 dispose；
    /// ret &gt; 0 且人數 &lt; ret，或 ret &lt; 0 且（斷線者是隊長或人數 &lt; -ret）→ 其他人 playerExit 後 dispose。
    /// </summary>
    internal void OnPlayerDisconnected(int characterId)
    {
        if (Disposed)
        {
            return;
        }

        var chr = new EventCharacterApi(_runtime, characterId);
        var result = _runtime.InvokeNow("playerDisconnected", this, chr);
        var ret = result.IsNumber() ? (int)(sbyte)(long)result.AsNumber() : 0;
        var leader = isLeader(chr);
        int[] others = [];
        bool dispose;
        lock (_lock)
        {
            if (Disposed)
            {
                return;
            }

            _disconnected.Add(characterId);
            UnregisterNoLock(characterId);
            var count = _players.Count;
            if (ret == 0)
            {
                dispose = count <= 0;
            }
            else
            {
                dispose = (ret > 0 && count < ret) || (ret < 0 && (leader || count < -ret));
                if (dispose)
                {
                    others = _players.Where(id => id != characterId).ToArray();
                }
            }
        }

        foreach (var id in others)
        {
            removePlayer(new EventCharacterApi(_runtime, id));
        }

        if (dispose)
        {
            this.dispose();
        }
    }

    private void Call(string function, params object[] extra)
    {
        if (!Disposed)
        {
            _runtime.InvokeNow(function, [this, .. extra]);
        }
    }

    private bool AddPlayer(int characterId)
    {
        lock (_lock)
        {
            if (Disposed)
            {
                return false;
            }

            if (!_players.Contains(characterId))
            {
                _players.Add(characterId);
            }
        }

        _runtime.Host.SetPlayerInstance(characterId, this);
        return true;
    }

    private void UnregisterNoLock(int characterId)
    {
        _runtime.Host.ClearPlayerInstance(characterId, this);
        if (!Disposed)
        {
            _players.Remove(characterId);
        }
    }

    private void DisposeNoLock()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        foreach (var id in _players)
        {
            _runtime.Host.ClearPlayerInstance(id, this);
        }

        foreach (var mob in _mobs)
        {
            mob.EventInstance = null;
        }

        foreach (var handle in _scheduled)
        {
            handle.cancel(false);
        }

        _players.Clear();
        _mobs.Clear();
        _killCount.Clear();
        _disconnected.Clear();
        _props.Clear();
        _mapIds.Clear();
        _scheduled.Clear();
        _eventTime = 0;
        _timeStarted = default;
        _runtime.Api.disposeInstance(Name);
    }
}

/// <summary>Java <c>em.getInstances()</c>（Collection）子集。</summary>
public sealed class EventInstanceList
{
    private readonly EventInstanceApi[] _items;

    internal EventInstanceList(EventInstanceApi[] items) => _items = items;

    public int size() => _items.Length;

    public bool isEmpty() => _items.Length == 0;

    public EventInstanceApi get(int index) => _items[index];

    public EventInstanceIterator iterator() => new(_items);
}

public sealed class EventInstanceIterator
{
    private readonly EventInstanceApi[] _items;
    private int _next;

    internal EventInstanceIterator(EventInstanceApi[] items) => _items = items;

    public bool hasNext() => _next < _items.Length;

    public EventInstanceApi next() => _items[_next++];
}

/// <summary>Java <c>OverrideMonsterStats</c>（em.newMonsterStats）：記錄覆寫值；生怪時尚未套用。</summary>
public sealed class OverrideMonsterStatsShim
{
    public long Hp { get; private set; }

    public int Mp { get; private set; }

    public int Exp { get; private set; }

    public void setOHp(double hp) => Hp = (long)hp;

    public void setOMp(double mp) => Mp = (int)mp;

    public void setOExp(double exp) => Exp = (int)exp;
}
