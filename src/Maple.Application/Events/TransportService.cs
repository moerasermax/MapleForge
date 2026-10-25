namespace Maple.Application.Events;

/// <summary>P173：船班事件對地圖造成的效果（Adapters 編成封包、生怪、清怪）。</summary>
public interface ITransportEffects
{
    /// <summary>Java <c>boatPacket(docked)</c> 廣播給該地圖。</summary>
    Task BoatPacketAsync(int mapId, bool docked, CancellationToken ct);

    /// <summary>Java <c>boatEffect(effect)</c>（1034：巴洛古船出現）。</summary>
    Task BoatEffectAsync(int mapId, int effect, CancellationToken ct);

    /// <summary>Java <c>musicChange(song)</c>。</summary>
    Task MusicAsync(int mapId, string song, CancellationToken ct);

    /// <summary>Java <c>broadcastMessage(type, msg)</c>。</summary>
    Task NoticeAsync(int mapId, int type, string message, CancellationToken ct);

    /// <summary>Java <c>getClock(seconds)</c>。</summary>
    Task ClockAsync(int mapId, int seconds, CancellationToken ct);

    /// <summary>Java <c>spawnMonsterOnGroundBelow(getMonster(id), point)</c>。</summary>
    Task SpawnMonsterAsync(int mapId, int monsterId, short x, short y, CancellationToken ct);

    /// <summary>Java <c>killAllMonsters(false)</c>。</summary>
    Task KillAllMonstersAsync(int mapId, CancellationToken ct);
}

/// <summary>
/// P173：交通船班（對照 scripts/event/Boats.js、Trains.js、Geenie.js、Flight.js、AirPlane.js、Subway.js）。
/// 每個事件是腳本的 C# 移植：<c>em.schedule(fn, delay)</c> 進本服務的排程表，由 <see cref="TickAsync"/>（hosted service 每秒）執行；
/// <c>em.setProperty</c> → <see cref="GetProperty"/>（NPC 售票員讀 entry／docked）；<c>map.setDocked</c> → <see cref="IsDocked"/>（進圖送 boatPacket）。
/// 換圖走 <see cref="Maps.IPlayerTransferService.WarpAll"/>（Java <c>getCharacters()</c> 逐一 changeMap(map, portal 0)）。
/// </summary>
public sealed class TransportService
{
    private readonly Maps.IPlayerTransferService _transfer;
    private readonly ITransportEffects _effects;
    private readonly Random _random;
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, string>> _properties = new(StringComparer.Ordinal);
    private readonly HashSet<int> _docked = new();
    private readonly List<(DateTimeOffset Due, long Seq, Func<DateTimeOffset, CancellationToken, Task> Action)> _schedule = new();
    private long _seq;
    private bool _started;

    public TransportService(Maps.IPlayerTransferService transfer, ITransportEffects effects, Random? random = null)
    {
        _transfer = transfer;
        _effects = effects;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Java <c>Boats.js OBoatsetup／EBoatsetup</c>：船艙出口 portal 改用腳本（getPortal("out00").setScriptName("OBoat1") 等）。
    /// </summary>
    public static string? GetPortalScriptOverride(int mapId, string portalName) => (mapId, portalName) switch
    {
        (200090011, "out00") => "OBoat1",
        (200090011, "out01") => "OBoat2",
        (200090001, "out00") => "EBoat1",
        (200090001, "out01") => "EBoat2",
        _ => null,
    };

    /// <summary>Java <c>em.getProperty(key)</c>；未設定回 null（AirPlane 一開始沒有 entry，照抄）。</summary>
    public string? GetProperty(string eventName, string key)
    {
        lock (_gate)
        {
            return _properties.TryGetValue(eventName, out var props) && props.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <summary>Java <c>em.setProperty</c>（NPC 腳本也可呼叫）。</summary>
    public void SetProperty(string eventName, string key, string value)
    {
        lock (_gate)
        {
            if (!_properties.TryGetValue(eventName, out var props))
            {
                _properties[eventName] = props = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            props[key] = value;
        }
    }

    /// <summary>是否為已知事件（cm.getEventManager(name) 不為 null）。</summary>
    public static bool IsKnownEvent(string eventName) => eventName is "Boats" or "Trains" or "Geenie" or "Flight" or "AirPlane" or "Subway";

    /// <summary>Java <c>MapleMap.hasBoat() == 2</c>（docked）。</summary>
    public bool IsDocked(int mapId)
    {
        lock (_gate)
        {
            return _docked.Contains(mapId);
        }
    }

    /// <summary>Java <c>EventScriptManager.init</c>：各事件 init()。只執行一次。</summary>
    public async Task StartAsync(DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        await BoatsScheduleNewAsync(now, ct);
        await DockRouteScheduleNewAsync(Trains, now, ct);
        await DockRouteScheduleNewAsync(Geenie, now, ct);
        await DockRouteScheduleNewAsync(Flight, now, ct);
        AirPlaneScheduleNew(now);
        await SubwayScheduleNewAsync(now, ct);
    }

    /// <summary>執行到期的排程（依到期時間、加入順序）。</summary>
    public async Task TickAsync(DateTimeOffset now, CancellationToken ct)
    {
        while (true)
        {
            Func<DateTimeOffset, CancellationToken, Task>? action;
            lock (_gate)
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

                action = _schedule[index].Action;
                _schedule.RemoveAt(index);
            }

            await action(now, ct);
        }
    }

    private void Schedule(DateTimeOffset now, int delayMs, Func<DateTimeOffset, CancellationToken, Task> action)
    {
        lock (_gate)
        {
            _schedule.Add((now.AddMilliseconds(delayMs), _seq++, action));
        }
    }

    private void SetDocked(int mapId, bool docked)
    {
        lock (_gate)
        {
            if (docked)
            {
                _docked.Add(mapId);
            }
            else
            {
                _docked.Remove(mapId);
            }
        }
    }

    // ── Boats.js（魔法森林 ↔ 天空之城；船艙、巴洛古入侵）───────────────────────────────────

    private const int OrbisBtf = 200000112, ElliniaBtf = 101000301, BoatToOrbis = 200090010, BoatToEllinia = 200090000;
    private const int OrbisBoatCabin = 200090011, ElliniaBoatCabin = 200090001, OrbisDocked = 200000100, ElliniaDocked = 101000300;
    private const int OrbisStation = 200000111;

    private async Task BoatsScheduleNewAsync(DateTimeOffset now, CancellationToken ct)
    {
        SetDocked(ElliniaDocked, true);
        SetDocked(OrbisStation, true);
        await _effects.BoatPacketAsync(ElliniaDocked, true, ct);
        await _effects.BoatPacketAsync(OrbisStation, true, ct);
        SetProperty("Boats", "docked", "true");
        SetProperty("Boats", "entry", "true");
        SetProperty("Boats", "haveBalrog", "false");
        SetProperty("Boats", "haveBalrog1", "false");
        Schedule(now, 120_000, (_, _) => { SetProperty("Boats", "entry", "false"); return Task.CompletedTask; }); // stopentry（resetReactors 未移植）
        Schedule(now, 120_000, BoatsTakeoffAsync);
    }

    private async Task BoatsTakeoffAsync(DateTimeOffset now, CancellationToken ct)
    {
        SetProperty("Boats", "docked", "false");
        _transfer.WarpAll(OrbisBtf, BoatToEllinia, 0);
        _transfer.WarpAll(ElliniaBtf, BoatToOrbis, 0);
        SetDocked(ElliniaDocked, false);
        SetDocked(OrbisStation, false);
        await _effects.BoatPacketAsync(ElliniaDocked, false, ct);
        await _effects.BoatPacketAsync(OrbisStation, false, ct);
        Schedule(now, 60_000, BoatsInvasionAsync);
        Schedule(now, 600_000, BoatsArrivedAsync);
    }

    private async Task BoatsArrivedAsync(DateTimeOffset now, CancellationToken ct)
    {
        _transfer.WarpAll(BoatToOrbis, OrbisDocked, 0);
        _transfer.WarpAll(OrbisBoatCabin, OrbisDocked, 0);
        _transfer.WarpAll(BoatToEllinia, ElliniaDocked, 0);
        _transfer.WarpAll(ElliniaBoatCabin, ElliniaDocked, 0);
        await _effects.KillAllMonstersAsync(BoatToOrbis, ct);
        await _effects.KillAllMonstersAsync(BoatToEllinia, ct);
        // Java invasion 把船圖設為 docked 後沒有重設（照抄：之後進船圖仍收到 boatPacket(true)）。
        await BoatsScheduleNewAsync(now, ct);
    }

    /// <summary>Java <c>invasion()</c>：chance = floor(random×10)；≤4 無、5 → 1（只換音樂）、其餘 → 2（兩艘船各生 2 隻 8150000）。</summary>
    private async Task BoatsInvasionAsync(DateTimeOffset now, CancellationToken ct)
    {
        var chance = (int)Math.Floor(_random.NextDouble() * 10);
        var numSpawn = chance <= 4 ? 0 : chance == 5 ? 1 : 2;
        if (numSpawn == 0)
        {
            return;
        }

        if (numSpawn == 2)
        {
            for (var i = 0; i < numSpawn; i++)
            {
                await _effects.SpawnMonsterAsync(BoatToOrbis, 8150000, 485, -221, ct);
                await _effects.SpawnMonsterAsync(BoatToEllinia, 8150000, -590, -221, ct);
            }
        }

        SetDocked(BoatToOrbis, true);
        SetDocked(BoatToEllinia, true);
        foreach (var map in new[] { BoatToOrbis, BoatToEllinia })
        {
            await _effects.BoatEffectAsync(map, 1034, ct);
        }

        foreach (var map in new[] { BoatToOrbis, BoatToEllinia })
        {
            await _effects.MusicAsync(map, "Bgm04/ArabPirate", ct);
        }

        SetProperty("Boats", numSpawn == 2 ? "haveBalrog" : "haveBalrog1", "true");
    }

    // ── Trains.js／Geenie.js／Flight.js（同一模板：240 秒停止入場、300 秒出發、600 秒到站）──────────

    private sealed record DockRoute(string Name, int[] Stations, (int From, int To)[] Departures, (int From, int To)[] Arrivals);

    /// <summary>天空之城 ↔ 玩具城。</summary>
    private static readonly DockRoute Trains = new("Trains", [220000110, 200000121], [(200000122, 200090100), (220000111, 200090110)], [(200090110, 200000100), (200090100, 220000100)]);

    /// <summary>天空之城 ↔ 納希沙漠（Geenie_Station 與 Geenie_docked 同為 260000100）。</summary>
    private static readonly DockRoute Geenie = new("Geenie", [260000100, 200000151], [(200000152, 200090400), (260000110, 200090410)], [(200090410, 200000100), (200090400, 260000100)]);

    /// <summary>天空之城 ↔ 神木村。Java 把往天空之城的船設成 200090110（與列車同圖，原碼如此，照抄）。</summary>
    private static readonly DockRoute Flight = new("Flight", [240000110, 200000131], [(200000132, 200090200), (240000111, 200090110)], [(200090110, 200000100), (200090200, 240000100)]);

    private async Task DockRouteScheduleNewAsync(DockRoute route, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var station in route.Stations)
        {
            SetDocked(station, true);
        }

        foreach (var station in route.Stations)
        {
            await _effects.BoatPacketAsync(station, true, ct);
        }

        SetProperty(route.Name, "docked", "true");
        SetProperty(route.Name, "entry", "true");
        Schedule(now, 240_000, (_, _) => { SetProperty(route.Name, "entry", "false"); return Task.CompletedTask; });
        Schedule(now, 300_000, (t, c) => DockRouteTakeoffAsync(route, t, c));
    }

    private async Task DockRouteTakeoffAsync(DockRoute route, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var station in route.Stations)
        {
            SetDocked(station, false);
        }

        foreach (var station in route.Stations)
        {
            await _effects.BoatPacketAsync(station, false, ct);
        }

        SetProperty(route.Name, "docked", "false");
        foreach (var (from, to) in route.Departures)
        {
            _transfer.WarpAll(from, to, 0);
        }

        Schedule(now, 600_000, async (t, c) =>
        {
            foreach (var (from, to) in route.Arrivals)
            {
                _transfer.WarpAll(from, to, 0);
            }

            await DockRouteScheduleNewAsync(route, t, c);
        });
    }

    // ── AirPlane.js（墮落城市 ↔ 新加坡；沒有 docked，entry 一開始未設定）──────────────────────────

    private void AirPlaneScheduleNew(DateTimeOffset now)
    {
        Schedule(now, 240_000, (_, _) => { SetProperty("AirPlane", "entry", "false"); return Task.CompletedTask; });
        Schedule(now, 300_000, (t, _) =>
        {
            SetProperty("AirPlane", "entry", "true");
            _transfer.WarpAll(540010100, 540010002, 0); // KC_bfd → Plane_to_KC
            _transfer.WarpAll(540010001, 540010101, 0); // CBD_bfd → Plane_to_CBD
            Schedule(t, 60_000, (_, _) =>
            {
                _transfer.WarpAll(540010101, 103000000, 0); // Plane_to_CBD → CBD_docked
                _transfer.WarpAll(540010002, 540010000, 0); // Plane_to_KC → KC_docked
                return Task.CompletedTask;
            });
            AirPlaneScheduleNew(t);
            return Task.CompletedTask;
        });
    }

    // ── Subway.js（墮落城市 ↔ 新葉城；120 秒停止入場／出發、60 秒到站，含時鐘與到離站公告）───────────

    private async Task SubwayScheduleNewAsync(DateTimeOffset now, CancellationToken ct)
    {
        SetProperty("Subway", "docked", "true");
        SetProperty("Subway", "entry", "true");
        await _effects.NoticeAsync(103000100, 6, "地鐵列車到站了。", ct);
        await _effects.NoticeAsync(600010001, 6, "地鐵列車到站了。", ct);
        Schedule(now, 120_000, (_, _) => { SetProperty("Subway", "entry", "false"); return Task.CompletedTask; });
        Schedule(now, 120_000, SubwayTakeoffAsync);
    }

    private async Task SubwayTakeoffAsync(DateTimeOffset now, CancellationToken ct)
    {
        SetProperty("Subway", "docked", "false");
        _transfer.WarpAll(600010004, 600010005, 0); // KC_Waiting → Subway_to_NLC
        _transfer.WarpAll(600010002, 600010003, 0); // NLC_Waiting → Subway_to_KC
        await _effects.NoticeAsync(103000100, 6, "地鐵列車已經離開了.", ct);
        await _effects.NoticeAsync(600010001, 6, "地鐵列車已經離開了.", ct);
        await _effects.ClockAsync(600010003, 60, ct);
        await _effects.ClockAsync(600010005, 60, ct);
        Schedule(now, 60_000, async (t, c) =>
        {
            _transfer.WarpAll(600010003, 103000100, 0); // Subway_to_KC → KC_docked
            _transfer.WarpAll(600010005, 600010001, 0); // Subway_to_NLC → NLC_docked
            await SubwayScheduleNewAsync(t, c);
        });
    }
}
