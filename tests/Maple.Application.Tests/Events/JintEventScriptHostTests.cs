using Maple.Application.Events;
using Maple.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple.Application.Tests.Events;

/// <summary>P177：事件腳本主機（對照 Java EventScriptManager／EventManager：init、setProperty、schedule、地圖操作）。</summary>
public sealed class JintEventScriptHostTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mf-event-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Init_SetsProperty_AndSchedulesFunction()
    {
        var (host, world, _) = Create("""
            importPackage(Packages.tools);
            function init() { em.setProperty("state", 0); em.schedule("tick", 5000); }
            function tick() {
                em.setProperty("state", parseInt(em.getProperty("state")) + 1);
                em.getMapFactory().getMap(100000000).broadcastMessage(MaplePacketCreator.serverNotice(6, "hi"));
                em.schedule("tick", 5000);
            }
            """);

        host.Start(T0);
        Assert.Equal("0", Api(host).getProperty("state"));

        host.Tick(T0.AddSeconds(4));
        Assert.Equal("0", Api(host).getProperty("state"));
        host.Tick(T0.AddSeconds(5));
        Assert.Equal("1", Api(host).getProperty("state"));
        Assert.Equal([(100000000, 6, "hi")], world.Notices);
        host.Tick(T0.AddSeconds(10));
        Assert.Equal("2", Api(host).getProperty("state"));
    }

    [Fact]
    public void CancelledSchedule_DoesNotRun()
    {
        var (host, _, _) = Create("""
            var task;
            function init() { em.setProperty("ran", "no"); task = em.schedule("run", 1000); task.cancel(true); }
            function run() { em.setProperty("ran", "yes"); }
            """);

        host.Start(T0);
        host.Tick(T0.AddSeconds(2));

        Assert.Equal("no", Api(host).getProperty("ran"));
    }

    [Fact]
    public void MapOps_WarpCharacters_SetPortalScript_ResetAndSpawn()
    {
        var (host, world, portals) = Create("""
            function init() {
                var map = em.getChannelServer().getMapFactory().getMap(103000800);
                map.getPortal("next00").setScriptName("kpq1");
                map.resetFully();
                map.spawnMonsterOnGroundBelow(em.getMonster(9300003), new java.awt.Point(10, 20));
                var it = em.getMapFactory().getMap(100000000).getCharacters().iterator();
                while (it.hasNext()) {
                    var chr = it.next();
                    chr.changeMap(map, map.getPortal(0));
                    chr.dropMessage(5, chr.getName());
                }
                em.setProperty("mobs", em.getMapFactory().getMap(103000800).countMonsterById(9300003));
            }
            """);
        world.OnMap[100000000] = [7];
        world.Characters[7] = new EventCharacterInfo(7, "Hero", 30, 100, 100000000);

        host.Start(T0);

        Assert.Equal("kpq1", portals.Get(103000800, "next00"));
        Assert.Equal([103000800], world.Resets);
        Assert.Equal([(103000800, 9300003, 10, 20)], world.Spawns);
        Assert.Equal([(7, 103000800, 0)], world.Warps);
        Assert.Equal([(7, 5, "Hero")], world.CharacterNotices);
        Assert.Equal("1", Api(host).getProperty("mobs"));
    }

    [Fact]
    public void ScriptError_IsSwallowed_AndMissingEventIgnored()
    {
        var (host, _, _) = Create("function init() { undefinedThing.call(); }", extraEvents: ["Missing"]);

        host.Start(T0);

        Assert.True(host.HasEvent("Test"));
        Assert.False(host.HasEvent("Missing"));
        Assert.Null(host.GetEventManager("Missing"));
    }

    [Fact]
    public void PortalOverride_FallsBackToTransport()
    {
        var registry = new PortalScriptOverrideRegistry();

        Assert.Equal(TransportService.GetPortalScriptOverride(200090000, "out00"), registry.Get(200090000, "out00"));
        registry.Set(200090000, "out00", "custom");
        Assert.Equal("custom", registry.Get(200090000, "out00"));
    }

    private static EventManagerApi Api(JintEventScriptHost host) => Assert.IsType<EventManagerApi>(host.GetEventManager("Test"));

    private (JintEventScriptHost, FakeWorld, PortalScriptOverrideRegistry) Create(string source, string[]? extraEvents = null)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "event"));
        File.WriteAllText(Path.Combine(_dir, "event", "Test.js"), source);
        var world = new FakeWorld();
        var portals = new PortalScriptOverrideRegistry();
        var options = new EventScriptOptions { ScriptsDirectory = _dir, Events = ["Test", .. extraEvents ?? []] };
        return (new JintEventScriptHost(options, world, portals, NullLogger<JintEventScriptHost>.Instance), world, portals);
    }

    private sealed class FakeWorld : IEventWorld
    {
        private readonly List<int> _mobs = [];

        public Dictionary<int, int[]> OnMap { get; } = [];

        public Dictionary<int, EventCharacterInfo> Characters { get; } = [];

        public List<(int, int, string)> Notices { get; } = [];

        public List<(int, int, string)> CharacterNotices { get; } = [];

        public List<(int, int, int)> Warps { get; } = [];

        public List<int> Resets { get; } = [];

        public List<(int, int, int, int)> Spawns { get; } = [];

        public IReadOnlyList<int> GetCharacterIdsOnMap(int mapId) => OnMap.TryGetValue(mapId, out var ids) ? ids : [];

        public EventCharacterInfo? GetCharacter(int characterId) => Characters.GetValueOrDefault(characterId);

        public void Warp(int characterId, int mapId, int portal) => Warps.Add((characterId, mapId, portal));

        public void SendNotice(int characterId, int type, string message) => CharacterNotices.Add((characterId, type, message));

        public void SendClock(int characterId, int seconds) { }

        public void BroadcastNotice(int mapId, int type, string message) => Notices.Add((mapId, type, message));

        public void BroadcastEnvironment(int mapId, int mode, string name) { }

        public void BroadcastClock(int mapId, int seconds) { }

        public void ResetMap(int mapId) => Resets.Add(mapId);

        public void KillAllMonsters(int mapId, bool animate) => _mobs.Clear();

        public int SpawnMonster(int mapId, int monsterId, int x, int y)
        {
            Spawns.Add((mapId, monsterId, x, y));
            _mobs.Add(monsterId);
            return _mobs.Count;
        }

        public IReadOnlyList<int> GetMonsterIds(int mapId) => _mobs;
    }
}
