using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Inventory;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

public sealed class RangedMagicCombatServiceTests
{
    [Fact]
    public void ApplyRangedAttack_ConsumesProjectileAndAppliesDamage()
    {
        var service = CreateService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer([UseItem(2060000, slot: 1, quantity: 10)]);
        var mob = MakeMob(objectId: 100001, hp: 50);
        field.Add(player);
        field.Add(mob);

        var result = service.ApplyRangedAttack(
            field,
            player,
            new CombatRangedAttack(
                new CombatAttack([new CombatAttackTarget(mob.ObjectId, [10, 5])]),
                SkillId: 0,
                ProjectileSlot: 1,
                CashProjectileSlot: 0,
                AreaOfEffect: 0x29),
            new RangedAttackConsumableOptions(BulletCount: 2));

        Assert.True(result.Applied);
        Assert.Equal(RangedAttackApplyStatus.Applied, result.Status);
        Assert.Equal(2060000, result.ProjectileItemId);
        Assert.Equal(2060000, result.VisualProjectileItemId);
        var mutation = Assert.Single(result.InventoryMutations);
        Assert.Equal(8, mutation.NewQuantity);
        Assert.Equal(35, mob.Hp);
        var stack = player.Inventory.By(InventoryType.Use).Get(1);
        Assert.NotNull(stack);
        Assert.Equal(8, stack.Quantity);
    }

    [Fact]
    public void ApplyRangedAttack_FailsWithoutProjectileAndDoesNotDamage()
    {
        var service = CreateService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer([]);
        var mob = MakeMob(objectId: 100001, hp: 50);
        field.Add(player);
        field.Add(mob);

        var result = service.ApplyRangedAttack(
            field,
            player,
            new CombatRangedAttack(
                new CombatAttack([new CombatAttackTarget(mob.ObjectId, [20])]),
                SkillId: 0,
                ProjectileSlot: 1,
                CashProjectileSlot: 0,
                AreaOfEffect: 0x29));

        Assert.Equal(RangedAttackApplyStatus.ProjectileMissing, result.Status);
        Assert.Empty(result.Combat.Hits);
        Assert.Equal(50, mob.Hp);
    }

    [Fact]
    public void ApplyMagicAttack_UsesCombatServiceDamagePath()
    {
        var service = CreateService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer([]);
        var mob = MakeMob(objectId: 100001, hp: 12);
        field.Add(player);
        field.Add(mob);

        var result = service.ApplyMagicAttack(
            field,
            player,
            new CombatAttack([new CombatAttackTarget(mob.ObjectId, [7, 7])]));

        var hit = Assert.Single(result.Hits);
        Assert.True(hit.Killed);
        Assert.Null(field.Get(mob.ObjectId));
    }

    private static RangedMagicCombatService CreateService()
        => new(new CombatService(new MapService(new EmptyDataProvider())));

    private static Player MakePlayer(IEnumerable<ItemRecord> items)
    {
        var chr = new Character
        {
            Id = 1,
            Name = "Tester",
            Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = 5, MaxMp = 5 },
            Items = items.ToList(),
        };
        return new Player(chr, new Position(0, 0, 0, 0));
    }

    private static Mob MakeMob(int objectId, int hp)
    {
        var def = new MapMonster { MonsterId = 100100, X = 30, Y = 40, Fh = 7 };
        var stats = new MobStats(100100, hp, MaxMp: 10, Level: 1, Exp: 1);
        return new Mob(def, stats, objectId);
    }

    private static ItemRecord UseItem(int itemId, short slot, short quantity) => new()
    {
        Type = (byte)InventoryType.Use,
        ItemId = itemId,
        Slot = slot,
        Quantity = quantity,
    };

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => null;
    }

    private sealed class Node : IDataNode
    {
        public Node(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, IDataNode> Children { get; } = new Dictionary<string, IDataNode>();

        public object? Value => null;

        public IDataNode? this[string name] => null;
    }
}
