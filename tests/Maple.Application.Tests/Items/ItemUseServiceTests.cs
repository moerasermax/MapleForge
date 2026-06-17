using Maple.Application.Items;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Items;

public sealed class ItemUseServiceTests
{
    [Fact]
    public void SpawnSummonBagMonsters_AddsLoadedMobsAtPlayerPosition()
    {
        var service = new ItemUseService(new MapService(new MonsterDataProvider()));
        var field = new FieldInstance(100000000);
        var player = NewPlayer(new Position(12, 34, 5, 7));
        field.Add(player);
        field.Add(NewMob(objectId: 100005));

        var spawned = service.SpawnSummonBagMonsters(field, player, [100100, 999999]);

        var mob = Assert.Single(spawned);
        Assert.Equal(100006, mob.ObjectId);
        Assert.Equal(100100, mob.Definition.MonsterId);
        Assert.Equal((short)12, mob.Position.X);
        Assert.Equal((short)34, mob.Position.Y);
        Assert.Equal((short)7, mob.Position.Foothold);
        Assert.Equal(42, mob.Hp);
        Assert.Same(mob, field.Get(100006));
    }

    [Fact]
    public void RemoveCaughtMob_RemovesOnlyMonsterObjects()
    {
        var service = new ItemUseService(new MapService(new MonsterDataProvider()));
        var field = new FieldInstance(100000000);
        var player = NewPlayer(new Position(0, 0, 0, 0));
        var mob = NewMob(objectId: 100001);
        field.Add(player);
        field.Add(mob);

        Assert.True(service.RemoveCaughtMob(field, 100001));
        Assert.Null(field.Get(100001));
        Assert.False(service.RemoveCaughtMob(field, player.ObjectId));
        Assert.Same(player, field.Get(player.ObjectId));
    }

    private static Player NewPlayer(Position position)
    {
        var character = new Character
        {
            Id = 1,
            Name = "ItemUseUser",
            Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = 10, MaxMp = 10 },
        };
        return new Player(character, position);
    }

    private static Mob NewMob(int objectId)
    {
        var definition = new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 };
        var stats = new MobStats(100100, MaxHp: 20, MaxMp: 10, Level: 1, Exp: 1);
        return new Mob(definition, stats, objectId);
    }

    private sealed class MonsterDataProvider : IDataProvider
    {
        private readonly Dictionary<string, IDataNode> _nodes = new()
        {
            ["Mob|0100100.img"] = new Node("0100100.img", children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                {
                    ["maxHP"] = new Node("maxHP", 42),
                    ["maxMP"] = new Node("maxMP", 7),
                    ["level"] = new Node("level", 2),
                    ["exp"] = new Node("exp", 12),
                }),
                ["move"] = new Node("move"),
            }),
        };

        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) =>
            _nodes.TryGetValue($"{fileName}|{path}", out var node) ? node : null;
    }

    private sealed class Node : IDataNode
    {
        public Node(string name, object? value = null, IReadOnlyDictionary<string, IDataNode>? children = null)
        {
            Name = name;
            Value = value;
            Children = children ?? new Dictionary<string, IDataNode>();
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, IDataNode> Children { get; }

        public object? Value { get; }

        public IDataNode? this[string name] => Children.TryGetValue(name, out var child) ? child : null;
    }
}
