using Maple.Application.Drops;
using Maple.Application.Reactors;
using Maple.Core.Drops;
using Maple.Core.Inventory;
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

    [Fact]
    public void DropItems_RollsReactorDrops_QuestGated_MesoAndMinItemsFill()
    {
        // P182：Java ReactorActionManager.dropItems——1/chance 入選、questid 需任務進行中、不足 minItems 補楓幣。
        var reactorDrops = new InMemoryReactorDropCatalog(new Dictionary<int, IReadOnlyList<ReactorDropEntry>>
        {
            [1002008] = [new ReactorDropEntry(4001000, 1, -1), new ReactorDropEntry(4001001, 1, 2000)],
        });
        var drops = new DropService(
            new InMemoryMonsterDropCatalog(new Dictionary<int, IReadOnlyList<MonsterDropEntry>>()),
            new DropServiceOptions(MesoRate: 2),
            random: new ZeroRandom());
        var scripts = new FakeReactorScriptFactory(rm => rm.DropItems(true, 1, 10, 20, 3));
        var service = new ReactorService(CreateProvider(reactorId: 1002008), scripts, drops, reactorDrops);
        var field = new FieldInstance(100000000);
        var reactor = Assert.Single(service.SpawnMapReactors(field, 100000000));

        var ctx = service.HitReactor(field, CreatePlayer(), reactor.ObjectId, charPosition: 1, stance: 7).ScriptContext!;

        // meso（1/1）＋ 4001000；4001001 任務未進行不掉；補 1 筆楓幣到 3 筆。
        Assert.Equal(3, ctx.SpawnedDrops.Count);
        Assert.True(ctx.SpawnedDrops[0].IsMeso);
        Assert.Equal(20, ctx.SpawnedDrops[0].Meso); // nextInt(10)=0 + 10 × 倍率 2
        Assert.Equal(4001000, ctx.SpawnedDrops[1].Item!.ItemId);
        Assert.Equal((byte)2, ctx.SpawnedDrops[1].DropType);
        Assert.Equal((byte)0, ctx.SpawnedDrops[0].DropType);
        Assert.Equal(reactor.Position.X - 24 + 25, ctx.SpawnedDrops[1].Position.X); // 入選 2 筆（含補的）→ 起點 −24，每筆 +25
        Assert.All(ctx.SpawnedDrops, d => Assert.Same(d, field.Get(d.ObjectId)));
    }

    [Fact]
    public void ScriptContext_MessagesGainItemHaveItemCanHold()
    {
        var player = CreatePlayer();
        player.Inventory.By(InventoryType.Etc).Put(new Item { ItemId = 4031000, Slot = 1, Quantity = 3 });
        var scripts = new FakeReactorScriptFactory(rm =>
        {
            rm.MapMessage("全圖");
            rm.PlayerMessage(6, "本人");
            rm.GainItem(4031000, 2);
            rm.GainItem(4031000, -1);
            rm.PlayerMessage(rm.HaveItem(4031000, 4) ? "有4" : "沒4");
        });
        var service = new ReactorService(CreateProvider(reactorId: 1002008), scripts);
        var field = new FieldInstance(100000000);
        var reactor = Assert.Single(service.SpawnMapReactors(field, 100000000));

        var ctx = service.HitReactor(field, player, reactor.ObjectId, charPosition: 1, stance: 7).ScriptContext!;

        Assert.Equal([(true, 5, "全圖"), (false, 6, "本人"), (false, 5, "有4")], ctx.Notices);
        Assert.Equal((short)4, player.Inventory.By(InventoryType.Etc).Get(1)!.Quantity);
        Assert.Single(Assert.Single(ctx.InventoryGains).Merged);
        Assert.Single(ctx.InventoryRemovals);
        Assert.Equal([(4031000, 2), (4031000, -1)], ctx.ItemGainNotices);
    }

    [Fact]
    public void ScriptContext_SpawnKillMusic()
    {
        // P183：spawnMonster 在反應堆 y−10 下方生、killMonster 只殺第一隻、killAll 全殺（動畫 1）、changeMusic。
        var provider = CreateProvider(reactorId: 1002008);
        var combat = new Maple.Application.Combat.CombatService(new Maple.Application.Maps.MapService(provider));
        var scripts = new FakeReactorScriptFactory(rm =>
        {
            rm.SpawnMonster(9300001, 3);
            rm.KillMonster(9300001);
            rm.ChangeMusic("Bgm00/Silence");
        });
        var service = new ReactorService(provider, scripts, combat: combat);
        var field = new FieldInstance(100000000);
        var reactor = Assert.Single(service.SpawnMapReactors(field, 100000000));

        var ctx = service.HitReactor(field, CreatePlayer(), reactor.ObjectId, charPosition: 1, stance: 7).ScriptContext!;

        Assert.Equal(3, ctx.SpawnedMobs.Count);
        Assert.All(ctx.SpawnedMobs, m => Assert.Equal((100, 190), (m.Position.X, m.Position.Y)));
        Assert.Equal([(ctx.SpawnedMobs[0].ObjectId, (byte)1)], ctx.KilledMobs);
        Assert.Equal(2, field.Objects.OfType<Mob>().Count());
        Assert.Equal(["Bgm00/Silence"], ctx.Music);

        ctx.KillAll();
        Assert.Empty(field.Objects.OfType<Mob>());
        Assert.Equal(3, ctx.KilledMobs.Count);
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
            [("Mob", "9300001.img")] = Node("9300001.img", children: new[] { Node("info", children: new[] { Leaf("maxHP", 100), Leaf("level", 10) }) }),
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

    private sealed class FakeReactorScriptFactory(Action<IReactorScriptContext>? act = null) : IReactorScriptFactory
    {
        public int ActCount { get; private set; }

        public IReactorScript? TryCreate(int reactorId, IReactorScriptContext rm)
            => new FakeReactorScript(() =>
            {
                ActCount++;
                (act ?? (static r => r.DropItems()))(rm);
            });
    }

    private sealed class ZeroRandom : Random
    {
        public override int Next(int maxValue) => 0;

        public override double NextDouble() => 0;
    }

    private sealed class FakeReactorScript : IReactorScript
    {
        private readonly Action _act;

        public FakeReactorScript(Action act) => _act = act;

        public void Act() => _act();
    }
}
