using Maple.Application.Combat;
using Maple.Application.Drops;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

public sealed class CombatServiceTests
{
    [Fact]
    public void ApplyAttack_SumsDamageLines_AndSubtractsMobHp()
    {
        var service = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var mob = MakeMob(objectId: 100001, hp: 100);
        field.Add(player);
        field.Add(mob);

        var result = service.ApplyAttack(field, player, new CombatAttack([
            new CombatAttackTarget(mob.ObjectId, [10, 15, -30]),
        ]));

        var hit = Assert.Single(result.Hits);
        Assert.Equal(25, hit.RequestedDamage);
        Assert.Equal(25, hit.AppliedDamage);
        Assert.Equal(75, hit.RemainingHp);
        Assert.False(hit.Killed);
        Assert.Equal(75, mob.Hp);
        Assert.Same(mob, field.Get(mob.ObjectId));
    }

    [Fact]
    public void ApplyAttack_KillsAndRemovesMob_WhenDamageReachesHp()
    {
        var service = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var mob = MakeMob(objectId: 100001, hp: 20);
        field.Add(player);
        field.Add(mob);

        var result = service.ApplyAttack(field, player, new CombatAttack([
            new CombatAttackTarget(mob.ObjectId, [12, 15]),
        ]));

        var hit = Assert.Single(result.Hits);
        Assert.Equal(27, hit.RequestedDamage);
        Assert.Equal(20, hit.AppliedDamage);
        Assert.Equal(0, hit.RemainingHp);
        Assert.True(hit.Killed);
        Assert.Null(field.Get(mob.ObjectId));
    }

    [Fact]
    public void ApplyAttack_KillsMobWithController_CapturesControllerIdBeforeRemoval()
    {
        // 對照 Java MapleMonster 死亡流程：controll.getClient().sendPacket(stopControllingMonster(...))
        // 只在怪物「有控制者」時才送；死亡後 mob 從 field 移除，ControllerId 要在移除前捕捉。
        var service = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var mob = MakeMob(objectId: 100001, hp: 20);
        mob.ControllerId = 99;
        field.Add(player);
        field.Add(mob);

        var result = service.ApplyAttack(field, player, new CombatAttack([
            new CombatAttackTarget(mob.ObjectId, [20]),
        ]));

        var hit = Assert.Single(result.Hits);
        Assert.True(hit.Killed);
        Assert.Equal(99, hit.ControllerId);
    }

    [Fact]
    public void ApplyAttack_KillsMobWithoutController_ControllerIdIsZero()
    {
        var service = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var mob = MakeMob(objectId: 100001, hp: 20);
        field.Add(player);
        field.Add(mob);

        var result = service.ApplyAttack(field, player, new CombatAttack([
            new CombatAttackTarget(mob.ObjectId, [20]),
        ]));

        var hit = Assert.Single(result.Hits);
        Assert.True(hit.Killed);
        Assert.Equal(0, hit.ControllerId);
    }

    [Fact]
    public void PlayerTakeDamage_ClampsAtZero()
    {
        var player = MakePlayer(hp: 30);

        var applied = player.TakeDamage(100);

        Assert.Equal(30, applied);
        Assert.Equal(0, player.Hp);
        Assert.False(player.IsAlive);
    }

    [Fact]
    public void SpawnMapMonsters_LoadsLifeAndMobStatsIntoField()
    {
        var service = new CombatService(new MapService(new MonsterDataProvider()));
        var field = new FieldInstance(100000100);

        var spawned = service.SpawnMapMonsters(field, 100000100);

        var mob = Assert.Single(spawned);
        Assert.Equal(100001, mob.ObjectId);
        Assert.Equal(100100, mob.Definition.MonsterId);
        Assert.Equal(42, mob.Hp);
        Assert.Equal(3, mob.Stats.SelfDestructAnimation);
        Assert.Equal((short)30, mob.Position.X);
        Assert.Equal((short)40, mob.Position.Y);
        Assert.Same(mob, field.Get(100001));
    }

    [Fact]
    public void KillMobWithoutRewards_RemovesMobWithoutCallingDropHook()
    {
        var killHook = new CountingKillHandler();
        var service = new CombatService(new MapService(new EmptyDataProvider()), killHook);
        var field = new FieldInstance(100000100);
        var mob = MakeMob(objectId: 100001, hp: 20);
        field.Add(mob);

        var result = service.KillMobWithoutRewards(field, mob.ObjectId, animation: 3);

        Assert.True(result.Killed);
        Assert.Equal(100001, result.ObjectId);
        Assert.Equal(3, result.Animation);
        Assert.Null(field.Get(mob.ObjectId));
        Assert.Equal(0, killHook.Calls);
    }

    private static Player MakePlayer(short hp = 50)
    {
        var chr = new Character
        {
            Id = 1,
            Name = "Tester",
            Stats = new CharacterStats { Hp = hp, MaxHp = 50, Mp = 5, MaxMp = 5 },
        };
        return new Player(chr, new Position(0, 0, 0, 0));
    }

    private static Mob MakeMob(int objectId, int hp)
    {
        var def = new MapMonster { MonsterId = 100100, X = 30, Y = 40, Fh = 7 };
        var stats = new MobStats(100100, hp, MaxMp: 10, Level: 1, Exp: 1);
        return new Mob(def, stats, objectId);
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => null;
    }

    private sealed class MonsterDataProvider : IDataProvider
    {
        private readonly Dictionary<string, IDataNode> _nodes;

        public MonsterDataProvider()
        {
            var map = new Node("100000100.img", children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                {
                    ["returnMap"] = new Node("returnMap", 100000000),
                    ["town"] = new Node("town", 0),
                }),
                ["portal"] = new Node("portal"),
                ["foothold"] = new Node("foothold"),
                ["life"] = new Node("life", children: new Dictionary<string, IDataNode>
                {
                    ["0"] = new Node("0", children: new Dictionary<string, IDataNode>
                    {
                        ["type"] = new Node("type", "m"),
                        ["id"] = new Node("id", "100100"),
                        ["x"] = new Node("x", 30),
                        ["y"] = new Node("y", 40),
                        ["cy"] = new Node("cy", 45),
                        ["f"] = new Node("f", 0),
                        ["fh"] = new Node("fh", 7),
                        ["rx0"] = new Node("rx0", 20),
                        ["rx1"] = new Node("rx1", 60),
                        ["mobTime"] = new Node("mobTime", 0),
                        ["team"] = new Node("team", -1),
                    }),
                }),
            });

            var mob = new Node("0100100.img", children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                {
                    ["maxHP"] = new Node("maxHP", 42),
                    ["maxMP"] = new Node("maxMP", 7),
                    ["level"] = new Node("level", 2),
                    ["exp"] = new Node("exp", 12),
                    ["selfDestruction"] = new Node("selfDestruction", children: new Dictionary<string, IDataNode>
                    {
                        ["action"] = new Node("action", 3),
                    }),
                }),
                ["move"] = new Node("move"),
            });

            _nodes = new Dictionary<string, IDataNode>
            {
                ["Map|Map/Map1/100000100.img"] = map,
                ["Mob|0100100.img"] = mob,
            };
        }

        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path)
            => _nodes.TryGetValue($"{fileName}|{path}", out var node) ? node : null;
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

    private sealed class CountingKillHandler : IMobKillHandler
    {
        public int Calls { get; private set; }

        public MobKillRewards OnMobKilled(FieldInstance field, Player killer, Mob mob)
        {
            Calls++;
            return MobKillRewards.Empty;
        }
    }
}
