using Maple.Application.Combat;
using Maple.Application.Drops;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Drops;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Drops;

/// <summary>P144：calcDropPos 與神偷偷錢（對照 Java MapleMap.calcDropPos、dropFromMonster、DamageParse.handlePickPocket）。</summary>
public sealed class PickPocketAndDropPosTests
{
    private static readonly IReadOnlyList<MapFoothold> Floor = [new MapFoothold { Id = 7, X1 = -100, Y1 = 30, X2 = 100, Y2 = 30 }];

    [Fact]
    public void CalcDropPos_SnapsToFootholdBelow_FromFiftyAbove()
    {
        Assert.Equal((10, 30, 7), FootholdMath.CalcDropPos(Floor, 10, 0));
        Assert.Equal((10, 30, 7), FootholdMath.CalcDropPos(Floor, 10, 75)); // y - 50 = 25 仍在地板之上
        Assert.Null(FootholdMath.CalcDropPos(Floor, 10, 100));               // y - 50 = 50 已在地板之下
        Assert.Null(FootholdMath.CalcDropPos(Floor, 500, 0));
    }

    [Fact]
    public void DropXRange_IncludesZeroLikeJava()
    {
        Assert.Equal((-100, 100), FootholdMath.DropXRange(Floor));
        Assert.Equal((0, 300), FootholdMath.DropXRange([new MapFoothold { X1 = 100, X2 = 300 }]));
    }

    [Fact]
    public void PlayerMesoDrop_UsesCalcDropPos()
    {
        var (drops, field, _) = Setup();
        var player = new Player(new Character { Id = 1, Name = "P", Meso = 1000 }, new Position(20, -40, 0, 0));
        field.Add(player);

        var drop = drops.TryDropMeso(field, player, 100).Drop!;

        Assert.Equal((short)20, drop.Position.X);
        Assert.Equal((short)30, drop.Position.Y);
    }

    [Fact]
    public void PickPocket_DropsPerLineWithJavaAmount()
    {
        var (drops, field, mob) = Setup(new SequenceRandom(0, 50));
        var player = new Player(new Character { Id = 1, Name = "Thief" }, new Position(0, 0, 0, 0));

        var spawned = drops.OnPickPocket(field, player, mob, [40_000, 100], maxMeso: 100, prop: 100);

        Assert.Equal([100, 1], spawned.Select(static d => d.Meso)); // min(max(2*100,1),100)、max(0.5,1)=1
        Assert.All(spawned, d => Assert.Equal(1, d.OwnerId));
        Assert.All(spawned, d => Assert.Equal((short)30, d.Position.Y));
        Assert.Equal(2, field.Objects.OfType<MapDrop>().Count());
    }

    [Fact]
    public void PickPocket_FailedChance_DropsNothing()
    {
        var (drops, field, mob) = Setup(new SequenceRandom(98));
        var player = new Player(new Character { Id = 1, Name = "Thief" }, new Position(0, 0, 0, 0));

        Assert.Empty(drops.OnPickPocket(field, player, mob, [40_000], maxMeso: 100, prop: 50));
    }

    [Fact]
    public void CombatService_PickPocketBuff_ReportsDrops_OnlyForListedSkills()
    {
        var (drops, field, mob) = Setup();
        var combat = new CombatService(new MapService(new FloorProvider()), mobKillHandler: drops);
        var player = new Player(new Character { Id = 1, Name = "Thief", Job = 421, Level = 70 }, new Position(0, 0, 0, 0));
        player.ApplyBuffStats(
            new MapleStatEffect
            {
                SourceId = 4211003,
                Level = 1,
                IsOverTime = true,
                DurationMilliseconds = 60_000,
                Statups = [new BuffStatValue(MapleBuffStat.PICKPOCKET, 100)],
            },
            DateTimeOffset.UnixEpoch);
        field.Add(player);

        var normal = Assert.Single(combat.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [10, 10])])).Hits);
        var other = Assert.Single(combat.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [10])], null, 4001344)).Hits);

        Assert.Equal(2, normal.PickPocketDrops!.Count);
        Assert.Null(other.PickPocketDrops);
    }

    [Fact]
    public void MobMesoDrop_ClampedToDropXRange()
    {
        var (drops, field, _) = Setup();
        var edgeMob = new Mob(
            new MapMonster { MonsterId = 100100, X = 90, Y = 30, Fh = 7 },
            new MobStats(100100, 10, MaxMp: 0, Level: 1, Exp: 1),
            100002);
        field.Add(edgeMob);
        var killer = new Player(new Character { Id = 1, Name = "K" }, new Position(0, 0, 0, 0));

        var meso = Assert.Single(drops.OnMobKilled(field, killer, edgeMob).SpawnedDrops, static d => d.Meso > 0);

        // min(max(90 - 25 * (1 / 2), -100 + 25), 100 - 1 * 25) = 75
        Assert.Equal((short)75, meso.Position.X);
        Assert.Equal((short)30, meso.Position.Y);
    }

    private static (DropService Drops, FieldInstance Field, Mob Mob) Setup(Random? random = null)
    {
        var field = new FieldInstance(100000000);
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 30, Fh = 7 },
            new MobStats(100100, 100_000, MaxMp: 0, Level: 1, Exp: 1),
            100001);
        field.Add(mob);
        return (new DropService(new EmptyDropCatalog(), random: random, maps: new MapService(new FloorProvider())), field, mob);
    }

    private sealed class SequenceRandom(params int[] values) : Random
    {
        private int _index;

        public override int Next(int maxValue) => Math.Min(values[Math.Min(_index++, values.Length - 1)], maxValue - 1);
    }

    private sealed class EmptyDropCatalog : IMonsterDropCatalog
    {
        public IReadOnlyList<MonsterDropEntry> RetrieveDrop(int monsterId) => [];
    }

    private sealed class FloorProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => (fileName, path) switch
        {
            ("Map", "Map/Map1/100000000.img") => new Node("100000000.img", children: new Dictionary<string, IDataNode>
            {
                ["foothold"] = new Node("foothold", children: new Dictionary<string, IDataNode>
                {
                    ["0"] = new Node("0", children: new Dictionary<string, IDataNode>
                    {
                        ["1"] = new Node("1", children: new Dictionary<string, IDataNode>
                        {
                            ["7"] = new Node("7", children: new Dictionary<string, IDataNode>
                            {
                                ["x1"] = new Node("x1", -100),
                                ["y1"] = new Node("y1", 30),
                                ["x2"] = new Node("x2", 100),
                                ["y2"] = new Node("y2", 30),
                            }),
                        }),
                    }),
                }),
            }),
            _ => null,
        };
    }

    private sealed class Node(string name, object? value = null, IReadOnlyDictionary<string, IDataNode>? children = null) : IDataNode
    {
        public string Name { get; } = name;

        public object? Value { get; } = value;

        public IReadOnlyDictionary<string, IDataNode> Children { get; } = children ?? new Dictionary<string, IDataNode>();

        public IDataNode? this[string key] => Children.TryGetValue(key, out var child) ? child : null;
    }
}
