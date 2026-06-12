using Maple.Application.Reactors;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.World;

namespace Maple.Application.Tests.Reactors;

public sealed class ReactorServiceTests
{
    [Fact]
    public void SpawnMapReactors_LoadsMapAndReactorStats()
    {
        var service = new ReactorService(CreateProvider(reactorId: 1002008));
        var field = new FieldInstance(100000000);

        var spawned = service.SpawnMapReactors(field, 100000000);

        var reactor = Assert.Single(spawned);
        Assert.Same(reactor, field.Get(200001));
        Assert.Equal(1002008, reactor.ReactorId);
        Assert.Equal("box", reactor.Name);
        Assert.Equal(0, reactor.Stats.GetType(0));
        Assert.Equal(1, reactor.Stats.GetNextState(0));
        Assert.Equal(999, reactor.Stats.GetType(1));
    }

    [Fact]
    public void HitReactor_AdvancesStateAndInvokesScriptAtTriggerPoint()
    {
        var scripts = new FakeReactorScriptFactory();
        var service = new ReactorService(CreateProvider(reactorId: 1002008), scripts);
        var field = new FieldInstance(100000000);
        var reactor = Assert.Single(service.SpawnMapReactors(field, 100000000));
        var player = CreatePlayer();

        var result = service.HitReactor(field, player, reactor.ObjectId, charPosition: 1, stance: 7);

        Assert.True(result.Success);
        Assert.True(result.ScriptInvoked);
        Assert.Equal(1, scripts.ActCount);
        Assert.Equal((byte)1, reactor.State);
        Assert.Contains(result.ScriptContext!.Calls, c => c.Name == nameof(IReactorScriptContext.DropItems));
    }

    [Fact]
    public void TouchReactor_OnlyTouchRangeInvokesScript()
    {
        var scripts = new FakeReactorScriptFactory();
        var service = new ReactorService(CreateProvider(reactorId: 6109013), scripts);
        var field = new FieldInstance(610030200);
        var reactor = Assert.Single(service.SpawnMapReactors(field, 610030200));
        var player = CreatePlayer(mapId: 610030200);

        var result = service.TouchReactor(field, player, reactor.ObjectId, touched: true);

        Assert.True(result.Success);
        Assert.True(result.ScriptInvoked);
        Assert.Equal(1, scripts.ActCount);
    }

    private static Player CreatePlayer(int mapId = 100000000) => new(
        new Character { Id = 1, Name = "ReactorUser", MapId = mapId },
        new Position(0, 0, 0, 0));

    private static IDataProvider CreateProvider(int reactorId)
    {
        var mapPath = reactorId == 6109013
            ? "Map/Map6/610030200.img"
            : "Map/Map1/100000000.img";

        var map = Node("map", children: new[]
        {
            Node("reactor", children: new[]
            {
                Node("0", children: new[]
                {
                    Leaf("id", reactorId.ToString()),
                    Leaf("x", 100),
                    Leaf("y", 200),
                    Leaf("f", 1),
                    Leaf("reactorTime", 0),
                    Leaf("name", "box"),
                }),
            }),
        });

        var reactor = Node($"{reactorId:D11}.img", children: new[]
        {
            Node("0", children: new[]
            {
                Node("event", children: new[]
                {
                    Node("0", children: new[]
                    {
                        Leaf("type", 0),
                        Leaf("state", 1),
                    }),
                }),
            }),
            Node("1"),
        });

        return new FakeDataProvider(new Dictionary<(string File, string Path), IDataNode>
        {
            [("Map", mapPath)] = map,
            [("Reactor", $"{reactorId:D11}.img")] = reactor,
        });
    }

    private static FakeDataNode Node(string name, object? value = null, IEnumerable<IDataNode>? children = null)
        => new(name, value, children?.ToDictionary(static c => c.Name) ?? new Dictionary<string, IDataNode>());

    private static FakeDataNode Leaf(string name, object value) => Node(name, value);

    private sealed class FakeDataProvider : IDataProvider
    {
        private readonly Dictionary<(string File, string Path), IDataNode> _nodes;

        public FakeDataProvider(Dictionary<(string File, string Path), IDataNode> nodes) => _nodes = nodes;

        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path)
            => _nodes.TryGetValue((fileName, path), out var node) ? node : null;
    }

    private sealed class FakeDataNode : IDataNode
    {
        public FakeDataNode(string name, object? value, IReadOnlyDictionary<string, IDataNode> children)
        {
            Name = name;
            Value = value;
            Children = children;
        }

        public string Name { get; }
        public IReadOnlyDictionary<string, IDataNode> Children { get; }
        public object? Value { get; }
        public IDataNode? this[string name] => Children.TryGetValue(name, out var node) ? node : null;
    }

    private sealed class FakeReactorScriptFactory : IReactorScriptFactory
    {
        public int ActCount { get; private set; }

        public IReactorScript? TryCreate(int reactorId, IReactorScriptContext rm)
            => new FakeReactorScript(() =>
            {
                ActCount++;
                rm.DropItems();
            });
    }

    private sealed class FakeReactorScript : IReactorScript
    {
        private readonly Action _act;

        public FakeReactorScript(Action act) => _act = act;

        public void Act() => _act();
    }
}
