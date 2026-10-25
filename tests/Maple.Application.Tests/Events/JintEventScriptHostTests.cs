using Maple.Application.Events;
using Maple.Application.Npcs;
using Maple.Application.Parties;
using Maple.Application.Quests;
using Maple.Core.Characters;
using Maple.Core.Parties;
using Maple.Core.Quests;
using Maple.Core.World;
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
    public void ResetReactors_OnlyResetsReactorState_NotMonsters()
    {
        // P185：Java resetReactors() = setReactorState(0)；setReactorState() = 1（elevator 事件）。
        var (host, world, _) = Create("""
            function init() {
                em.getChannelServer().getMapFactory().getMap(222020100).resetReactors();
                em.getChannelServer().getMapFactory().getMap(222020100).setReactorState();
                em.warpAllPlayer(222020210, 222020211);
            }
            """);
        world.OnMap[222020210] = [5];

        host.Start(T0);

        Assert.Equal([(222020100, (byte)0), (222020100, (byte)1)], world.ReactorStates);
        Assert.Empty(world.Resets);
        Assert.Equal([(5, 222020211, 0)], world.Warps);
    }

    [Fact]
    public void CalendarAndTimestampShim_LikeAutomatedEvent()
    {
        // P201：AutomatedEvent.init 用 java.util.Calendar 算下一個整點，scheduleSystemEvent 未移植回 false。
        var (host, _, _) = Create("""
            function init() {
                var cal = java.util.Calendar.getInstance();
                cal.set(java.util.Calendar.HOUR_OF_DAY, 4);
                cal.set(java.util.Calendar.MINUTE, 0);
                cal.set(java.util.Calendar.SECOND, 0);
                var next = cal.getTimeInMillis();
                var ts = java.sql.Timestamp.valueOf("2026-10-25 16:30:00.0").getTime();
                var d = new Date(ts);
                em.setProperty("result", cal.get(java.util.Calendar.HOUR_OF_DAY) + "/" + cal.get(java.util.Calendar.MINUTE)
                    + "/" + d.getHours() + ":" + d.getMinutes() + "/" + (next > 0) + "/" + em.scheduleSystemEvent()
                    + "/" + (cal.get(java.util.Calendar.DAY_OF_WEEK) >= 1 && cal.get(java.util.Calendar.DAY_OF_WEEK) <= 7));
            }
            """);

        host.Start(T0);

        Assert.Equal("4/0/16:30/true/false/true", Api(host).getProperty("result"));
    }

    [Fact]
    public void RealAutomatedEventScript_InitsWithoutError()
    {
        const string path = "/home/user/yc_maplestroy_tsm113/scripts/event/AutomatedEvent.js";
        if (!File.Exists(path))
        {
            return; // 雲端以外沒有舊伺服器腳本：略過
        }

        Directory.CreateDirectory(Path.Combine(_dir, "event"));
        File.WriteAllText(Path.Combine(_dir, "event", "AutomatedEvent.js"), File.ReadAllText(path));
        var log = new WarningLog();
        var host = new JintEventScriptHost(new EventScriptOptions { ScriptsDirectory = _dir, Events = ["AutomatedEvent"] }, new FakeWorld(), new PortalScriptOverrideRegistry(), log, new ManualTime(T0));

        host.Start(T0);
        host.Tick(T0.AddHours(5)); // 到點跑 setup（scheduleSystemEvent → false）

        Assert.True(host.HasEvent("AutomatedEvent"));
        Assert.Empty(log.Warnings);
    }

    private sealed class WarningLog : Microsoft.Extensions.Logging.ILogger<JintEventScriptHost>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
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

    // ── P178：事件實例（eim）───────────────────────────────────────────────────────────

    private const string KerningLike = """
        function init() { em.setProperty("state", "0"); }
        function monsterValue(eim, mobId) { return 1; }
        function setup() {
            em.setProperty("state", "1");
            var eim = em.newInstance("KerningPQ");
            var map = eim.setInstanceMap(103000800);
            map.resetFully();
            map.getPortal("next00").setScriptName("kpq1");
            eim.setInstanceMap(103000801);
            eim.startEventTimer(1800000);
            return eim;
        }
        function playerEntry(eim, player) {
            var map = eim.getMapInstance(0);
            player.changeMap(map, map.getPortal(0));
        }
        function changedMap(eim, player, mapid) {
            eim.unregisterPlayer(player);
            if (eim.disposeIfPlayerBelow(0, 0)) { em.setProperty("state", "0"); }
        }
        function scheduledTimeout(eim) {
            var players = eim.getPlayers();
            var exit = eim.getMapFactory().getMap(103000890);
            for (var i = 0; i < players.size(); i++) {
                var player = players.get(i);
                eim.unregisterPlayer(player);
                player.changeMap(exit, exit.getPortal(0));
            }
            em.setProperty("timedOut", "yes");
        }
        function allMonstersDead(eim) { eim.setProperty("cleared", "true"); }
        function playerDisconnected(eim, player) { return -2; }
        function leftParty(eim, player) {
            if (eim.disposeIfPlayerBelow(3, 103000890)) { em.setProperty("started", "false"); } else { playerExit(eim, player); }
        }
        function disbandParty(eim) { eim.disposeIfPlayerBelow(100, 103000890); em.setProperty("state", "0"); }
        function playerExit(eim, player) {
            eim.unregisterPlayer(player);
            var exit = eim.getMapFactory().getMap(103000890);
            player.changeMap(exit, exit.getPortal(0));
        }
        function playerDead(eim, player) { eim.setProperty("dead", "" + player.getId()); }
        """;

    [Fact]
    public void StartInstance_PartyOnMap_RegistersAndWarps_TimerTimesOut()
    {
        var time = new ManualTime(T0);
        var (host, world, portals) = Create(KerningLike, time: time);
        world.OnMap[103000000] = [7, 8];
        host.Start(T0);

        Api(host).startInstance(new PartyRef(1, [7, 8, 9]), new MapRef(103000000));

        Assert.Equal("1", Api(host).getProperty("state"));
        Assert.Equal([(7, 103000800, 0), (8, 103000800, 0)], world.Warps);
        Assert.Equal([103000800], world.Resets);
        Assert.Equal("kpq1", portals.Get(103000800, "next00"));
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(7));
        Assert.Same(eim, host.GetEventInstance(8));
        Assert.Null(host.GetEventInstance(9)); // 不在地圖上的隊員不登記
        Assert.Equal(2, eim.getPlayerCount());
        Assert.Same(eim, Api(host).getInstance("KerningPQ"));

        host.Tick(T0.AddMinutes(29));
        Assert.Null(Api(host).getProperty("timedOut"));
        host.Tick(T0.AddMinutes(30));
        Assert.Equal("yes", Api(host).getProperty("timedOut"));
        Assert.Equal([(7, 103000890, 0), (8, 103000890, 0)], world.Warps.Skip(2));
        Assert.Null(host.GetEventInstance(7));
    }

    [Fact]
    public void DisposeIfPlayerBelow_DisposesInstance_ResetsState_AndCancelsSchedules()
    {
        var (host, world, _) = Create(KerningLike);
        world.OnMap[103000000] = [7];
        host.Start(T0);
        Api(host).startInstance(new PartyRef(1, [7]), new MapRef(103000000));
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(7));
        eim.schedule("scheduledTimeout", 1000);

        Assert.False(eim.disposeIfPlayerBelow(0, 0));
        eim.unregisterPlayer(new MapRefChar(7));
        Assert.True(eim.disposeIfPlayerBelow(0, 103000890));

        Assert.True(eim.Disposed);
        Assert.Equal("0", Api(host).getProperty("state"));
        Assert.Null(Api(host).getInstance("KerningPQ"));
        Assert.Equal(string.Empty, eim.getProperty("anything"));
        host.Tick(T0.AddHours(1));
        Assert.Null(Api(host).getProperty("timedOut")); // 計時與 eim.schedule 已取消
    }

    [Fact]
    public void DisposeIfPlayerBelow_WarpsRemainingPlayers()
    {
        var (host, world, _) = Create(KerningLike);
        world.OnMap[103000000] = [7, 8];
        host.Start(T0);
        Api(host).startInstance(new PartyRef(1, [7, 8]), new MapRef(103000000));
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(7));
        world.Warps.Clear();

        Assert.True(eim.disposeIfPlayerBelow(100, 103000890));

        Assert.Equal([(7, 103000890, 0), (8, 103000890, 0)], world.Warps);
        Assert.Null(host.GetEventInstance(8));
    }

    [Fact]
    public void RegisteredMonsters_AllDead_InvokesAllMonstersDead()
    {
        var (host, _, _) = Create(KerningLike);
        host.Start(T0);
        Api(host).startInstance();
        var eim = Api(host).getInstance("KerningPQ")!;
        var a = new MonsterTemplate(9300001);
        var b = new MonsterTemplate(9300001);
        eim.registerMonster(a);
        eim.registerMonster(b);

        eim.unregisterMonster(a);
        Assert.Null(eim.getProperty("cleared"));
        eim.unregisterMonster(b);
        Assert.Equal("true", eim.getProperty("cleared"));
    }

    [Fact]
    public void RestartEventTimer_SendsClockToMembers()
    {
        var (host, world, _) = Create(KerningLike);
        world.OnMap[103000000] = [7];
        host.Start(T0);
        Api(host).startInstance(new PartyRef(1, [7]), new MapRef(103000000));
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(7));

        eim.restartEventTimer(60000);
        eim.broadcastPlayerMsg(6, "go");

        Assert.Equal([(7, 60)], world.Clocks);
        Assert.Contains((7, 6, "go"), world.CharacterNotices);
        Assert.True(eim.isTimerStarted());
    }

    [Fact]
    public async Task NpcScript_StartsInstance_ThroughCmGetEventManager()
    {
        var (host, world, _) = Create(KerningLike);
        host.Start(T0);
        var npcDir = Path.Combine(_dir, "npc");
        Directory.CreateDirectory(npcDir);
        File.WriteAllText(Path.Combine(npcDir, "9020000.js"), """
            function start() {
                var em = cm.getEventManager("Test");
                if (em.getProperty("state").equals("0")) {
                    em.startInstance(cm.getParty(), cm.getMap());
                }
                cm.sendOk(cm.getEventInstance().getName() + "/" + cm.getPlayer().getEventInstance().getPlayerCount());
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var parties = new InMemoryPartyRegistry();
        var player = new Player(new Character { Id = 7, Name = "Leader", Level = 30, MapId = 103000000 }, new Position(0, 0, 0, 0));
        parties.CreateParty(PartyMember.FromCharacter(player.Character, channelIndex: 1));
        world.OnMap[103000000] = [7];
        var ctx = new NpcContext(9020000, player, new QuestService(new EmptyQuestCatalog()), parties: parties);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _dir }, NullLogger<JintNpcScriptFactory>.Instance, host);
        var dialogs = new List<string>();
        var convo = new NpcConversation(9020000, factory.TryCreate(9020000, ctx)!, ctx, (d, _) => { dialogs.Add(d.Text); return Task.CompletedTask; }, (_, _) => Task.CompletedTask);

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal(["KerningPQ/1"], dialogs);
        Assert.Equal([(7, 103000800, 0)], world.Warps);
    }

    // ── P179：遊戲流程掛勾 ─────────────────────────────────────────────────────────────

    [Fact]
    public void OnChangedMap_OutsidePq_UnregistersAndDisposes()
    {
        var (host, world, _) = Started(KerningLike, [7]);
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(7));

        host.OnChangedMap(7, 103000890);

        Assert.True(eim.Disposed);
        Assert.Null(host.GetEventInstance(7));
        Assert.Equal("0", Api(host).getProperty("state"));
    }

    [Fact]
    public void OnPlayerDisconnected_LeaderWithMinusTwo_ExitsOthersAndDisposes()
    {
        var (host, world, _) = Started(KerningLike, [7, 8, 9]);
        world.Parties[7] = new EventPartyInfo(1, 7, [7, 8, 9]);
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(8));
        world.Warps.Clear();

        host.OnPlayerDisconnected(7);

        Assert.True(eim.Disposed);
        Assert.Equal([(8, 103000890, 0), (9, 103000890, 0)], world.Warps);
    }

    [Fact]
    public void OnPlayerDisconnected_NonLeader_StaysWhileEnoughPlayers()
    {
        var (host, world, _) = Started(KerningLike, [7, 8, 9]);
        world.Parties[8] = new EventPartyInfo(1, 7, [7, 8, 9]);
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(8));

        host.OnPlayerDisconnected(8);

        Assert.False(eim.Disposed);
        Assert.Equal(2, eim.getPlayerCount());
        Assert.True(eim.isDisconnected(new MapRefChar(8)));
    }

    [Fact]
    public void OnLeftParty_BelowThree_WarpsAllAndDisposes_DisbandAlso()
    {
        var (host, world, _) = Started(KerningLike, [7, 8]);
        world.Warps.Clear();

        host.OnLeftParty(8);

        Assert.Equal([(7, 103000890, 0), (8, 103000890, 0)], world.Warps);
        Assert.Equal("false", Api(host).getProperty("started"));
        Assert.Null(host.GetEventInstance(7));
        host.OnDisbandParty(7); // 已無實例：無事
    }

    [Fact]
    public void OnPlayerDead_InvokesPlayerDead_AndClockOnMapEntry()
    {
        var (host, _, _) = Started(KerningLike, [7]);
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(7));

        host.OnPlayerDead(7);

        Assert.Equal("7", eim.getProperty("dead"));
        Assert.Equal(1800, host.GetTimeLeftSeconds(7));
        Assert.Null(host.GetTimeLeftSeconds(8));
        Assert.True(host.OnPlayerRevive(7)); // 腳本無 playerRevive → Java 預設 true
    }

    [Fact]
    public void OnMonsterKilled_CountsKill_AndAllMonstersDead()
    {
        var (host, world, _) = Create("""
            function init() {}
            function monsterValue(eim, mobId) { return mobId == 9300003 ? 2 : 1; }
            function setup() {
                var eim = em.newInstance("Boss");
                var map = eim.setInstanceMap(100000000);
                var mob = em.getMonster(9300003);
                eim.registerMonster(mob);
                map.spawnMonsterOnGroundBelow(mob, new java.awt.Point(0, 0));
                return eim;
            }
            function playerEntry(eim, player) {}
            function allMonstersDead(eim) { eim.setProperty("cleared", "true"); }
            """);
        world.OnMap[100000000] = [7];
        host.Start(T0);
        Api(host).startInstance(new PartyRef(1, [7]), new MapRef(100000000));
        var eim = Assert.IsType<EventInstanceApi>(host.GetEventInstance(7));

        host.OnMonsterKilled(100000000, 999, 100100, 7); // 不是實例怪：算進玩家的實例
        Assert.Equal(1, eim.getKillCount(new MapRefChar(7)));
        Assert.Null(eim.getProperty("cleared"));

        host.OnMonsterKilled(100000000, 1, 9300003, 7);
        Assert.Equal(3, eim.getKillCount(new MapRefChar(7)));
        Assert.Equal("true", eim.getProperty("cleared"));
    }

    private (JintEventScriptHost, FakeWorld, PortalScriptOverrideRegistry) Started(string source, int[] members)
    {
        var (host, world, portals) = Create(source);
        world.OnMap[103000000] = members;
        host.Start(T0);
        Api(host).startInstance(new PartyRef(1, members), new MapRef(103000000));
        return (host, world, portals);
    }

    // ── P190：遠征隊 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Squad_RegisterJoin_ThenEventStartsInstanceForMembersOnMap()
    {
        var (host, world, _) = Create("""
            function init() { em.setProperty("state", "0"); }
            function setup(leaderName) {
                em.setProperty("leader", leaderName);
                var eim = em.newInstance("Zak" + leaderName);
                eim.setInstanceMap(280030000);
                return eim;
            }
            function playerEntry(eim, player) {
                var map = eim.getMapInstance(0);
                player.changeMap(map, map.getPortal(0));
            }
            """);
        host.Start(T0);
        world.Characters[7] = new EventCharacterInfo(7, "Lead", 120, 112, 211042400);
        world.Characters[8] = new EventCharacterInfo(8, "Mem", 120, 212, 211042400);
        world.Characters[9] = new EventCharacterInfo(9, "Far", 120, 312, 100000000);
        var online = world.Characters.Values.ToDictionary(c => c.Name, c => new Maple.Application.Squads.SquadCharacter(c.Id, c.Name, c.Job, c.MapId, 0));
        var squads = new Maple.Application.Squads.SquadRegistry(n => online.GetValueOrDefault(n));
        var npcDir = Path.Combine(_dir, "npc");
        Directory.CreateDirectory(npcDir);
        File.WriteAllText(Path.Combine(npcDir, "2030013.js"), """
            function start() {
                var who = cm.getPlayer().getName();
                if (who == "Lead") {
                    cm.registerSquad("ZAK", 5, " 開團");
                    cm.sendOk("leader=" + cm.isSquadLeader("ZAK"));
                } else if (who == "Mem") {
                    cm.sendOk("join=" + cm.addMember("ZAK", true) + " member=" + cm.isSquadMember("ZAK"));
                } else {
                    cm.getEventManager("Test").startInstance(cm.getSquad("ZAK"), cm.getMap(), 160101);
                    cm.sendOk("avail=" + cm.getSquadAvailability("ZAK"));
                }
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _dir }, NullLogger<JintNpcScriptFactory>.Instance, host);

        async Task<(List<string> Dialogs, List<Maple.Application.Squads.SquadNotice> Notices)> Talk(int id, string name)
        {
            var player = new Player(new Character { Id = id, Name = name, Level = 120, MapId = 211042400 }, new Position(0, 0, 0, 0));
            var ctx = new NpcContext(2030013, player, new QuestService(new EmptyQuestCatalog()), squads: squads);
            var dialogs = new List<string>();
            var notices = new List<Maple.Application.Squads.SquadNotice>();
            var convo = new NpcConversation(2030013, factory.TryCreate(2030013, ctx)!, ctx, (d, _) => { dialogs.Add(d.Text); return Task.CompletedTask; }, (_, _) => Task.CompletedTask,
                sendSquadNotice: (n, _) => { notices.Add(n); return Task.CompletedTask; });
            await convo.StartAsync(CancellationToken.None);
            return (dialogs, notices);
        }

        var lead = await Talk(7, "Lead");
        Assert.Equal(["leader=1"], lead.Dialogs);
        Assert.Contains(new Maple.Application.Squads.SquadMapNotice(211042400, "Lead 開團"), lead.Notices);

        var mem = await Talk(8, "Mem");
        Assert.Equal(["join=1 member=1"], mem.Dialogs);
        Assert.Equal(new Maple.Application.Squads.SquadPlayerNotice("Lead", 6, "Mem (大魔導士(火,毒)) 加入了遠征隊!"), Assert.Single(mem.Notices));

        squads.Get("ZAK")!.AddMember("Far", 312, true, false); // 不在開始地圖：不會被登記
        var start = await Talk(10, "Starter");
        Assert.Equal(["avail=2"], start.Dialogs);
        Assert.Equal("Lead", Api(host).getProperty("leader"));
        Assert.Equal([(7, 280030000, 0), (8, 280030000, 0)], world.Warps);
        Assert.Equal([7, 8], world.QuestData.Select(static q => q.Item1));
        Assert.All(world.QuestData, q => Assert.Equal(160101, q.Item2));
        Assert.Equal([211042400], world.StopClocks);
        Assert.NotNull(host.GetEventInstance(8));
        Assert.Null(host.GetEventInstance(9));
    }

    private static EventManagerApi Api(JintEventScriptHost host) => Assert.IsType<EventManagerApi>(host.GetEventManager("Test"));

    private (JintEventScriptHost, FakeWorld, PortalScriptOverrideRegistry) Create(string source, string[]? extraEvents = null, TimeProvider? time = null)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "event"));
        File.WriteAllText(Path.Combine(_dir, "event", "Test.js"), source);
        var world = new FakeWorld();
        var portals = new PortalScriptOverrideRegistry();
        var options = new EventScriptOptions { ScriptsDirectory = _dir, Events = ["Test", .. extraEvents ?? []] };
        return (new JintEventScriptHost(options, world, portals, NullLogger<JintEventScriptHost>.Instance, time ?? new ManualTime(T0)), world, portals);
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

        public List<(int, int)> Clocks { get; } = [];

        public void SendClock(int characterId, int seconds) => Clocks.Add((characterId, seconds));

        public void SendEnvironment(int characterId, int mode, string name) { }

        public Dictionary<int, EventPartyInfo> Parties { get; } = [];

        public List<int> StopClocks { get; } = [];

        public List<(int, int, string)> QuestData { get; } = [];

        public void BroadcastStopClock(int mapId) => StopClocks.Add(mapId);

        public EventCharacterInfo? FindCharacterByName(string name) => Characters.Values.FirstOrDefault(c => c.Name == name);

        public void SetQuestCustomData(int characterId, int questId, string data) => QuestData.Add((characterId, questId, data));

        public EventPartyInfo? GetParty(int characterId) => Parties.GetValueOrDefault(characterId);

        public void BroadcastNotice(int mapId, int type, string message) => Notices.Add((mapId, type, message));

        public void BroadcastEnvironment(int mapId, int mode, string name) { }

        public void BroadcastClock(int mapId, int seconds) { }

        public void ResetMap(int mapId) => Resets.Add(mapId);

        public List<(int, byte)> ReactorStates { get; } = [];

        public void SetReactorState(int mapId, byte state) => ReactorStates.Add((mapId, state));

        public void KillAllMonsters(int mapId, bool animate) => _mobs.Clear();

        public int SpawnMonster(int mapId, int monsterId, int x, int y)
        {
            Spawns.Add((mapId, monsterId, x, y));
            _mobs.Add(monsterId);
            return _mobs.Count;
        }

        public IReadOnlyList<int> GetMonsterIds(int mapId) => _mobs;
    }

    private sealed record PartyRef(int PartyId, IReadOnlyList<int> MemberIds) : IScriptPartyRef;

    private sealed record MapRef(int MapId) : IScriptMapRef;

    private sealed record MapRefChar(int CharacterId) : IScriptCharacterRef;

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class EmptyQuestCatalog : IQuestCatalog
    {
        public QuestDefinition? GetQuest(int questId) => null;
    }
}
