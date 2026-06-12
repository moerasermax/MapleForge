using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Reactors;

public sealed record ReactorInteractionResult(
    ReactorInteractionStatus Status,
    Reactor? Reactor = null,
    ReactorHitResult? Hit = null,
    bool ScriptInvoked = false,
    ReactorScriptContext? ScriptContext = null)
{
    public bool Success => Status == ReactorInteractionStatus.Success;
}

public enum ReactorInteractionStatus
{
    NotFound,
    NotAlive,
    Ignored,
    Success,
}

/// <summary>Reactor use case：從 WZ 載入 map reactors / Reactor.wz stats，並套用 hit/touch 觸發。</summary>
public sealed class ReactorService
{
    public const int DefaultReactorObjectIdBase = 200_000;

    private readonly IDataProvider _data;
    private readonly IReactorScriptFactory? _scripts;
    private readonly object _gate = new();
    private readonly Dictionary<int, ReactorStats> _statsCache = new();

    public ReactorService(IDataProvider data, IReactorScriptFactory? scripts = null)
    {
        _data = data;
        _scripts = scripts;
    }

    public IReadOnlyList<Reactor> SpawnMapReactors(FieldInstance field, int mapId, int firstObjectId = DefaultReactorObjectIdBase + 1)
    {
        ArgumentNullException.ThrowIfNull(field);

        var existing = field.Objects.OfType<Reactor>().ToList();
        if (existing.Count > 0)
        {
            return existing;
        }

        var reactors = new List<Reactor>();
        var objectId = Math.Max(firstObjectId, field.Objects.Select(static o => o.ObjectId).DefaultIfEmpty(0).Max() + 1);
        foreach (var def in LoadMapReactors(mapId))
        {
            while (field.Get(objectId) is not null)
            {
                objectId++;
            }

            var reactor = new Reactor(def, LoadReactorStats(def.ReactorId), objectId++);
            field.Add(reactor);
            reactors.Add(reactor);
        }

        return reactors;
    }

    public IReadOnlyList<MapReactor> LoadMapReactors(int mapId)
    {
        var mapImg = _data.GetAt("Map", GetMapImagePath(mapId));
        var reactorNode = mapImg?["reactor"];
        if (reactorNode is null)
        {
            return Array.Empty<MapReactor>();
        }

        var reactors = new List<MapReactor>();
        foreach (var entry in OrderedNumberedChildren(reactorNode))
        {
            if (!int.TryParse(GetString(entry, "id"), out var reactorId))
            {
                continue;
            }

            reactors.Add(new MapReactor
            {
                ReactorId = reactorId,
                X = GetInt(entry, "x", 0),
                Y = GetInt(entry, "y", 0),
                F = GetInt(entry, "f", 0),
                ReactorTimeMs = Math.Max(0, GetInt(entry, "reactorTime", 0)) * 1000,
                Name = GetString(entry, "name"),
            });
        }

        return reactors;
    }

    public ReactorStats LoadReactorStats(int reactorId)
    {
        lock (_gate)
        {
            if (_statsCache.TryGetValue(reactorId, out var cached))
            {
                return cached;
            }

            var stats = LoadReactorStatsUncached(reactorId);
            _statsCache[reactorId] = stats;
            return stats;
        }
    }

    public ReactorInteractionResult HitReactor(FieldInstance field, Player player, int objectId, int charPosition, short stance)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(player);

        if (field.Get(objectId) is not Reactor reactor)
        {
            return new ReactorInteractionResult(ReactorInteractionStatus.NotFound);
        }

        if (!reactor.IsAlive)
        {
            return new ReactorInteractionResult(ReactorInteractionStatus.NotAlive, reactor);
        }

        var hit = reactor.Hit(charPosition, stance);
        if (!hit.Applied)
        {
            return new ReactorInteractionResult(ReactorInteractionStatus.Ignored, reactor, hit);
        }

        var (scriptInvoked, ctx) = hit.ShouldInvokeScript
            ? TryAct(player, reactor)
            : (false, null);

        return new ReactorInteractionResult(
            ReactorInteractionStatus.Success,
            reactor,
            hit,
            scriptInvoked,
            ctx);
    }

    public ReactorInteractionResult TouchReactor(FieldInstance field, Player player, int objectId, bool touched)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(player);

        if (field.Get(objectId) is not Reactor reactor)
        {
            return new ReactorInteractionResult(ReactorInteractionStatus.NotFound);
        }

        if (!reactor.IsAlive)
        {
            return new ReactorInteractionResult(ReactorInteractionStatus.NotAlive, reactor);
        }

        if (!reactor.CanTouchTrigger(touched))
        {
            return new ReactorInteractionResult(ReactorInteractionStatus.Ignored, reactor);
        }

        var (scriptInvoked, ctx) = TryAct(player, reactor);
        return new ReactorInteractionResult(
            ReactorInteractionStatus.Success,
            reactor,
            ScriptInvoked: scriptInvoked,
            ScriptContext: ctx);
    }

    private ReactorStats LoadReactorStatsUncached(int reactorId)
    {
        var reactorData = GetReactorImage(reactorId);
        var link = GetInt(reactorData?["info"], "link", reactorId);
        if (link != reactorId)
        {
            return LoadReactorStats(link);
        }

        if (reactorData is null)
        {
            return new ReactorStats(Array.Empty<ReactorStateData>());
        }

        var states = new List<ReactorStateData>();
        var foundState = false;
        for (var i = 0; i <= byte.MaxValue; i++)
        {
            var stateNode = reactorData[$"{i}"];
            if (stateNode is null)
            {
                break;
            }

            var eventNode = stateNode["event"];
            var eventZero = eventNode?["0"];
            if (eventZero is not null)
            {
                var type = GetInt(eventZero, "type", 0);
                int? reactItemId = null;
                var reactItemQuantity = 0;
                if (type == 100)
                {
                    reactItemId = GetInt(eventZero, "0", 0);
                    reactItemQuantity = GetInt(eventZero, "1", 1);
                }

                foundState = true;
                states.Add(new ReactorStateData(
                    (byte)i,
                    type,
                    reactItemId,
                    reactItemQuantity,
                    GetInt(eventZero, "state", i + 1),
                    GetInt(eventNode, "timeOut", -1)));
            }
            else
            {
                states.Add(new ReactorStateData(
                    (byte)i,
                    999,
                    null,
                    0,
                    foundState ? -1 : i + 1,
                    0));
            }
        }

        return new ReactorStats(states);
    }

    private (bool Invoked, ReactorScriptContext? Context) TryAct(Player player, Reactor reactor)
    {
        if (_scripts is null)
        {
            return (false, null);
        }

        var ctx = new ReactorScriptContext(player, reactor);
        var script = _scripts.TryCreate(reactor.ReactorId, ctx);
        if (script is null)
        {
            return (false, ctx);
        }

        script.Act();
        return (true, ctx);
    }

    private IDataNode? GetReactorImage(int reactorId)
        => _data.GetAt("Reactor", $"{reactorId:D11}.img");

    private static string GetMapImagePath(int mapId)
    {
        var folder = $"Map{mapId / 100_000_000}";
        var file = $"{mapId:D9}.img";
        return $"Map/{folder}/{file}";
    }

    private static IEnumerable<IDataNode> OrderedNumberedChildren(IDataNode node)
        => node.Children
            .OrderBy(static kvp => int.TryParse(kvp.Key, out var n) ? n : int.MaxValue)
            .Select(static kvp => kvp.Value);

    private static int GetInt(IDataNode? node, string key, int defaultValue)
    {
        var child = node?[key];
        return child?.Value switch
        {
            int v => v,
            short v => v,
            long v when v <= int.MaxValue && v >= int.MinValue => (int)v,
            byte v => v,
            sbyte v => v,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static string GetString(IDataNode? node, string key, string defaultValue = "")
    {
        var child = node?[key];
        return child?.Value is string s ? s : defaultValue;
    }
}
