using Maple.Application.Combat;
using Maple.Application.Drops;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Inventory;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Drops;

public sealed class DropServiceTests
{
    [Fact]
    public void OnMobKilled_GrantsExp_AndSpawnsItemAndMesoDrops()
    {
        var service = MakeDropService(new MonsterDropEntry(4000000, 999_999, 3, 3));
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var mob = MakeMob(level: 1, exp: 7);
        field.Add(player);
        field.Add(mob);

        var rewards = service.OnMobKilled(field, player, mob);

        Assert.Equal(7, rewards.ExpGained);
        Assert.Equal(7, player.Character.Exp);
        Assert.Equal(2, rewards.SpawnedDrops.Count);

        var itemDrop = rewards.SpawnedDrops[0];
        Assert.Equal(1_000_000, itemDrop.ObjectId);
        Assert.False(itemDrop.IsMeso);
        Assert.Equal(4000000, itemDrop.ItemId);
        Assert.Equal(3, itemDrop.Item!.Quantity);
        Assert.Equal(player.Character.Id, itemDrop.OwnerId);
        Assert.Equal((byte)0, itemDrop.DropType);
        Assert.Equal((short)30, itemDrop.Position.X);
        Assert.Equal((short)40, itemDrop.Position.Y);
        Assert.Same(itemDrop, field.Get(itemDrop.ObjectId));

        var mesoDrop = rewards.SpawnedDrops[1];
        Assert.True(mesoDrop.IsMeso);
        Assert.Equal(1, mesoDrop.Meso);
        Assert.Equal((short)5, mesoDrop.Position.X);
        Assert.Same(mesoDrop, field.Get(mesoDrop.ObjectId));
    }

    [Fact]
    public void TryPickup_Item_AddsToInventoryAndRemovesDrop()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var drop = MapDrop.ForItem(
            1_000_000,
            new Item { ItemId = 4000000, Quantity = 2 },
            new Position(10, 20, 0, 7),
            new Position(10, 20, 0, 7),
            100001,
            player.Character.Id,
            dropType: 0);
        field.Add(player);
        field.Add(drop);

        var result = service.TryPickup(field, player, drop.ObjectId);

        Assert.True(result.Success);
        Assert.Equal(DropPickupStatus.Success, result.Status);
        Assert.Equal(InventoryType.Etc, result.InventoryType);
        Assert.NotNull(result.GainedItem);
        Assert.Equal(4000000, result.GainedItem!.ItemId);
        Assert.Equal(2, player.Inventory.By(InventoryType.Etc).Get(1)!.Quantity);
        Assert.Null(field.Get(drop.ObjectId));
    }

    [Fact]
    public void TryPickup_Meso_AddsMesoAndRemovesDrop()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var drop = MapDrop.ForMeso(
            1_000_000,
            50,
            new Position(10, 20, 0, 7),
            new Position(10, 20, 0, 7),
            100001,
            player.Character.Id,
            dropType: 0);
        field.Add(player);
        field.Add(drop);

        var result = service.TryPickup(field, player, drop.ObjectId);

        Assert.True(result.Success);
        Assert.Equal(50, result.GainedMeso);
        Assert.Equal(50, player.Character.Meso);
        Assert.Null(field.Get(drop.ObjectId));
    }

    [Fact]
    public void TryPickup_RejectsSoloDropForDifferentOwner()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer(id: 1);
        var drop = MapDrop.ForItem(
            1_000_000,
            new Item { ItemId = 4000000, Quantity = 1 },
            new Position(10, 20, 0, 7),
            new Position(10, 20, 0, 7),
            100001,
            ownerId: 2,
            dropType: 0);
        field.Add(player);
        field.Add(drop);

        var result = service.TryPickup(field, player, drop.ObjectId);

        Assert.Equal(DropPickupStatus.NotAllowed, result.Status);
        Assert.Same(drop, field.Get(drop.ObjectId));
        Assert.Empty(player.Inventory.By(InventoryType.Etc).Items);
    }

    [Fact]
    public void CombatService_KillHook_AttachesRewardsBeforeRemovingMob()
    {
        var dropService = MakeDropService(new MonsterDropEntry(4000000, 999_999, 1, 1));
        var combat = new CombatService(new MapService(new EmptyDataProvider()), dropService);
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var mob = MakeMob(hp: 10, level: 1, exp: 5);
        field.Add(player);
        field.Add(mob);

        var result = combat.ApplyAttack(field, player, new CombatAttack([
            new CombatAttackTarget(mob.ObjectId, [10]),
        ]));

        var hit = Assert.Single(result.Hits);
        Assert.True(hit.Killed);
        Assert.NotNull(hit.Rewards);
        Assert.Equal(5, hit.Rewards!.ExpGained);
        Assert.Equal(2, hit.Rewards.SpawnedDrops.Count);
        Assert.Null(field.Get(mob.ObjectId));
        Assert.IsType<MapDrop>(field.Get(1_000_000));
    }

    private static DropService MakeDropService(params MonsterDropEntry[] entries)
    {
        var catalog = new InMemoryMonsterDropCatalog(new Dictionary<int, IReadOnlyList<MonsterDropEntry>>
        {
            [100100] = entries,
        });
        return new DropService(catalog);
    }

    private static Player MakePlayer(int id = 1)
    {
        var chr = new Character
        {
            Id = id,
            Name = "Tester",
            Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = 5, MaxMp = 5 },
        };
        return new Player(chr, new Position(0, 0, 0, 0));
    }

    private static Mob MakeMob(int hp = 20, short level = 1, int exp = 1)
    {
        var def = new MapMonster { MonsterId = 100100, X = 30, Y = 40, Fh = 7 };
        var stats = new MobStats(100100, hp, MaxMp: 10, Level: level, Exp: exp);
        return new Mob(def, stats, objectId: 100001);
    }

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
            Children = new Dictionary<string, IDataNode>();
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, IDataNode> Children { get; }

        public object? Value => null;

        public IDataNode? this[string name] => null;
    }
}
