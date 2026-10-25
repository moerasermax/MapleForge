using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

/// <summary>P148：怪物死亡 revive（對照 Java MapleLifeFactory info/revive、MapleMonster.spawnRevives、MapleMap.spawnRevives）。</summary>
public sealed class MobReviveTests
{
    [Fact]
    public void LoadMobStats_ReadsReviveIdsInOrder()
    {
        var stats = new MapService(new ReviveProvider()).LoadMobStats(100100)!;

        Assert.Equal([100101, 100102], stats.Revives);
        Assert.Null(new MapService(new ReviveProvider()).LoadMobStats(100101)!.Revives);
    }

    [Fact]
    public void KillingMob_SpawnsRevivesAtDeathPosition_WithLinkAndController()
    {
        var maps = new MapService(new ReviveProvider());
        var service = new CombatService(maps);
        var field = new FieldInstance(100000100);
        var player = new Player(new Character { Id = 7, Name = "K" }, new Position(0, 0, 0, 0));
        var mob = new Mob(new MapMonster { MonsterId = 100100, X = 40, Y = 10, Fh = 3 }, maps.LoadMobStats(100100)!, 100001);
        field.Add(player);
        field.Add(mob);

        var hit = Assert.Single(service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [999])])).Hits);

        Assert.True(hit.Killed);
        Assert.Equal([100101, 100102], hit.Revived!.Select(static m => m.Definition.MonsterId));
        Assert.All(hit.Revived!, r =>
        {
            Assert.Equal(100001, r.LinkObjectId);
            Assert.Equal(7, r.ControllerId);
            Assert.Equal((short)40, r.Position.X);
            Assert.Same(r, field.Get(r.ObjectId));
        });
        Assert.Null(field.Get(100001));
        Assert.NotEqual(hit.Revived![0].ObjectId, hit.Revived![1].ObjectId);
    }

    [Fact]
    public void SelfDestruct_AlsoSpawnsRevives()
    {
        var maps = new MapService(new ReviveProvider());
        var service = new CombatService(maps);
        var field = new FieldInstance(100000100);
        var mob = new Mob(new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 }, maps.LoadMobStats(100100)!, 100001);
        field.Add(mob);

        var killed = service.KillMobWithoutRewards(field, mob.ObjectId, animation: 1, controllerId: 9);

        Assert.Equal(2, killed.Revived!.Count);
        Assert.All(killed.Revived!, r => Assert.Equal(9, r.ControllerId));
    }

    [Fact]
    public void SpongeBosses_DoNotUseDefaultReviveBranch()
    {
        var service = new CombatService(new MapService(new ReviveProvider()));
        var field = new FieldInstance(100000100);
        var sponge = new Mob(
            new MapMonster { MonsterId = 8810118, X = 0, Y = 0, Fh = 1 },
            new MobStats(8810118, 100, MaxMp: 0, Level: 1, Exp: 1, Revives: [100101]),
            100001);
        field.Add(sponge);

        Assert.Null(service.SpawnRevives(field, sponge, 1));
    }

    private sealed class ReviveProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => (fileName, path) switch
        {
            ("Mob", "0100100.img") => MobImg("0100100.img", revive: new Dictionary<string, IDataNode>
            {
                ["1"] = new Node("1", 100102),
                ["0"] = new Node("0", 100101),
            }),
            ("Mob", "0100101.img") => MobImg("0100101.img"),
            ("Mob", "0100102.img") => MobImg("0100102.img"),
            _ => null,
        };

        private static Node MobImg(string name, IReadOnlyDictionary<string, IDataNode>? revive = null)
        {
            var info = new Dictionary<string, IDataNode>
            {
                ["maxHP"] = new Node("maxHP", 50),
                ["level"] = new Node("level", 3),
            };
            if (revive is not null)
            {
                info["revive"] = new Node("revive", children: revive);
            }

            return new Node(name, children: new Dictionary<string, IDataNode> { ["info"] = new Node("info", children: info) });
        }
    }

    private sealed class Node(string name, object? value = null, IReadOnlyDictionary<string, IDataNode>? children = null) : IDataNode
    {
        public string Name { get; } = name;

        public object? Value { get; } = value;

        public IReadOnlyDictionary<string, IDataNode> Children { get; } = children ?? new Dictionary<string, IDataNode>();

        public IDataNode? this[string key] => Children.TryGetValue(key, out var child) ? child : null;
    }
}
