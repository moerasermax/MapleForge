using Maple.Adapters.V113.Channel;
using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P109：召喚獸攻擊實際扣怪物血（對照 Java <c>SummonHandler.SummonAttack</c> 的 <c>mob.damage</c>）。</summary>
public sealed class ChannelSummonAttackTests
{
    [Fact]
    public void ToCombatAttack_DropsHitsAtOrAboveJavaCap()
    {
        var attack = V113SummonHandler.ToCombatAttack([
            new V113SummonAttackTarget(100001, 500),
            new V113SummonAttackTarget(100002, V113SummonHandler.MaxSummonHitDamage),
            new V113SummonAttackTarget(100003, V113SummonHandler.MaxSummonHitDamage - 1),
        ]);

        Assert.Equal(new[] { 100001, 100003 }, attack.Targets.Select(static t => t.ObjectId));
        Assert.Equal(500, attack.Targets[0].TotalDamage);
    }

    [Fact]
    public void ToCombatAttack_SummonDamage_KillsMobThroughCombatService()
    {
        var combat = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = new Player(
            new Character { Id = 7, Name = "Summoner", Stats = new CharacterStats { Hp = 50, MaxHp = 50 } },
            new Position(0, 0, 0, 0));
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 300, MaxMp: 0, Level: 1, Exp: 1),
            objectId: 100001);
        field.Add(player);
        field.Add(mob);

        var result = combat.ApplyAttack(field, player, V113SummonHandler.ToCombatAttack([
            new V113SummonAttackTarget(mob.ObjectId, 300),
        ]));

        var hit = Assert.Single(result.Hits);
        Assert.True(hit.Killed);
        Assert.Null(field.Get(mob.ObjectId));
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => null;
    }

    private sealed class Node(string name) : IDataNode
    {
        public string Name { get; } = name;

        public IReadOnlyDictionary<string, IDataNode> Children { get; } = new Dictionary<string, IDataNode>();

        public object? Value => null;

        public IDataNode? this[string path] => null;
    }
}
