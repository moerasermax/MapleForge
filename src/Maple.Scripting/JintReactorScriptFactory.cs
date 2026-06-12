using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jint;
using Maple.Application.Reactors;
using Maple.Core.World;
using Microsoft.Extensions.Logging;

namespace Maple.Scripting;

/// <summary>
/// 用 Jint 跑既有 OdinMS reactor .js 腳本。MVP 對常見 rm API 採記錄/no-op，
/// 先確保 act() 可執行並把完整副作用列入技術債。
/// </summary>
public sealed class JintReactorScriptFactory : IReactorScriptFactory
{
    private readonly string _reactorDir;
    private readonly ILogger<JintReactorScriptFactory> _log;
    private readonly ConcurrentDictionary<int, string?> _sourceCache = new();

    private static readonly Regex LoadCall = new(@"load\s*\([^)]*\)\s*;?", RegexOptions.Compiled);
    private static readonly Regex ImportPackageCall = new(@"importPackage\s*\([^)]*\)\s*;?", RegexOptions.Compiled);

    public JintReactorScriptFactory(ReactorScriptOptions options, ILogger<JintReactorScriptFactory> log)
    {
        _reactorDir = Path.Combine(options.ScriptsDirectory, "reactor");
        _log = log;
    }

    public IReactorScript? TryCreate(int reactorId, IReactorScriptContext rm)
    {
        var source = _sourceCache.GetOrAdd(reactorId, LoadSource);
        if (source is null)
        {
            return null;
        }

        try
        {
            var engine = BuildEngine(rm);
            engine.Execute(source);
            return new JintReactorScript(engine);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[ReactorScript] reactorId={Id} 腳本載入/執行失敗：{Msg}", reactorId, ex.Message);
            return null;
        }
    }

    private string? LoadSource(int reactorId)
    {
        var path = Path.Combine(_reactorDir, $"{reactorId}.js");
        if (!File.Exists(path))
        {
            return null;
        }

        return Preprocess(File.ReadAllText(path));
    }

    private static string Preprocess(string raw)
    {
        raw = LoadCall.Replace(raw, string.Empty);
        raw = ImportPackageCall.Replace(raw, string.Empty);
        return raw;
    }

    private static Engine BuildEngine(IReactorScriptContext rm)
    {
        var engine = new Engine(o => o
            .LimitRecursion(64)
            .MaxStatements(200_000)
            .TimeoutInterval(TimeSpan.FromSeconds(2)));

        engine.SetValue("load", new Action<object?>(_ => { }));
        engine.SetValue("importPackage", new Action<object?>(_ => { }));
        engine.SetValue("Packages", new { });
        engine.SetValue("java", new { awt = new { Point = typeof(PointShim) } });
        engine.SetValue("rm", new RmFacade(rm));
        return engine;
    }

    private sealed class RmFacade
    {
        private readonly IReactorScriptContext _rm;

        public RmFacade(IReactorScriptContext rm) => _rm = rm;

        public void dropItems() => _rm.DropItems();
        public void dropItems(bool meso, int mesoChance, int minMeso, int maxMeso) =>
            _rm.DropItems(meso, mesoChance, minMeso, maxMeso);
        public void dropItems(bool meso, int mesoChance, int minMeso, int maxMeso, int minItems) =>
            _rm.DropItems(meso, mesoChance, minMeso, maxMeso, minItems);
        public void doHarvest() => _rm.DoHarvest();
        public void spawnMonster(int id) => _rm.SpawnMonster(id);
        public void spawnMonster(int id, int qty) => _rm.SpawnMonster(id, qty);
        public void spawnMob(int id) => _rm.SpawnMonster(id);
        public void spawnMob(int id, int qty) => _rm.SpawnMonster(id, qty);
        public void spawnNpc(int npcId) => _rm.SpawnNpc(npcId);
        public void mapMessage(string message) => _rm.MapMessage(message);
        public void mapMessage(int type, string message) => _rm.MapMessage(type, message);
        public void playerMessage(string message) => _rm.PlayerMessage(message);
        public void playerMessage(int type, string message) => _rm.PlayerMessage(type, message);
        public int getMapId() => _rm.MapId;
        public ReactorFacade getReactor() => new(_rm.Reactor);
        public MapFacade getMap() => new(_rm);
        public PlayerFacade getPlayer() => new(_rm);
        public void warp(int mapId) => _rm.Warp(mapId);
        public void warp(int mapId, int portal) => _rm.Warp(mapId, portal);
        public void warpS(int mapId) => _rm.WarpS(mapId);
        public void warpS(int mapId, int portal) => _rm.WarpS(mapId, portal);
        public void gainItem(int itemId, int quantity) => _rm.GainItem(itemId, quantity);
        public bool haveItem(int itemId) => _rm.HaveItem(itemId);
        public bool haveItem(int itemId, int quantity) => _rm.HaveItem(itemId, quantity);
        public bool canHold() => _rm.CanHold();
        public bool canHold(int itemId) => _rm.CanHold(itemId);
        public bool canHold(int itemId, int quantity) => _rm.CanHold(itemId, quantity);
        public void killMonster(int monsterId) => _rm.KillMonster(monsterId);
        public void killMob(int monsterId) => _rm.KillMonster(monsterId);
        public void killAll() => _rm.KillAll();
        public void killAllMob() => _rm.KillAll();
        public void changeMusic(string song) => _rm.ChangeMusic(song);
        public object? getEventManager(string name) { _rm.RecordUnsupported("getEventManager", name); return null; }
        public object? getEventInstance() { _rm.RecordUnsupported("getEventInstance"); return null; }
        public object? getClient() { _rm.RecordUnsupported("getClient"); return null; }
        public void scheduleWarp(int delay, int mapId) => _rm.RecordUnsupported("scheduleWarp", delay, mapId);
        public void givePartyNX(int amount) => _rm.RecordUnsupported("givePartyNX", amount);
        public void givePartyExp(int amount) => _rm.RecordUnsupported("givePartyExp", amount);
        public void gainGP(int amount) => _rm.RecordUnsupported("gainGP", amount);
        public void forceCompleteQuest(int questId) => _rm.RecordUnsupported("forceCompleteQuest", questId);
        public void showEffect(bool broadcast, string effect) => _rm.RecordUnsupported("showEffect", broadcast, effect);
    }

    private sealed class ReactorFacade
    {
        private readonly Reactor _reactor;

        public ReactorFacade(Reactor reactor) => _reactor = reactor;

        public int getReactorId() => _reactor.ReactorId;
        public int getState() => _reactor.State;
        public string getName() => _reactor.Name;
        public void forceHitReactor(int state) => _reactor.ForceState((byte)state);
        public void forceTrigger() { }
    }

    private sealed class MapFacade
    {
        private readonly IReactorScriptContext _rm;

        public MapFacade(IReactorScriptContext rm) => _rm = rm;

        public int getId() => _rm.MapId;
        public ReactorFacade getReactorByName(string name) => new(_rm.Reactor);
        public void setReactorState() => _rm.RecordUnsupported("map.setReactorState");
        public void startSpeedRun() => _rm.RecordUnsupported("map.startSpeedRun");
        public void spawnChaosZakum(int x, int y) => _rm.RecordUnsupported("map.spawnChaosZakum", x, y);
        public void Papfight() => _rm.RecordUnsupported("map.Papfight");
        public void changeEnvironment(string name, int state) => _rm.RecordUnsupported("map.changeEnvironment", name, state);
        public void moveEnvironment(string name, int state) => _rm.RecordUnsupported("map.moveEnvironment", name, state);
        public void toggleEnvironment(string name) => _rm.RecordUnsupported("map.toggleEnvironment", name);
        public void killAllMonsters(bool animate) => _rm.RecordUnsupported("map.killAllMonsters", animate);
        public void setSpawns(bool enabled) => _rm.RecordUnsupported("map.setSpawns", enabled);
        public void respawn(bool force) => _rm.RecordUnsupported("map.respawn", force);
        public void spawnMonsterOnGroundBelow(object? monster, object? point) => _rm.RecordUnsupported("map.spawnMonsterOnGroundBelow", monster, point);
    }

    private sealed class PlayerFacade
    {
        private readonly IReactorScriptContext _rm;

        public PlayerFacade(IReactorScriptContext rm) => _rm = rm;

        public int getId() => _rm.Player.Character.Id;
        public int getMapId() => _rm.Player.Character.MapId;
        public int getMeso() => _rm.Player.Character.Meso;
    }

    private sealed class PointShim
    {
        public int x { get; }
        public int y { get; }

        public PointShim(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }
}
