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
        Assert.NotNull(rewards.StatsMutation);
        Assert.Contains(rewards.StatsMutation!.Updates, u => u.Kind == PlayerStatKind.Exp && u.Value == 7);
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
    public void OnMobKilled_SpawnedDrops_StampSpawnTimeFromTimeProvider()
    {
        // P061：對照 Java MapleMap.spawnFromMonster：掉落物出生時間要能查得到，供後續世界 tick
        // 判斷是否該過期（見 MapDrop.ShouldExpire）。
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var service = MakeDropService(new FakeTimeProvider(now), new MonsterDropEntry(4000000, 999_999, 3, 3));
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var mob = MakeMob(level: 1, exp: 7);
        field.Add(player);
        field.Add(mob);

        var rewards = service.OnMobKilled(field, player, mob);

        foreach (var drop in rewards.SpawnedDrops)
        {
            Assert.Equal(now, drop.SpawnedAt);
            Assert.False(drop.ShouldExpire(now + MapDrop.ExpireAfter - TimeSpan.FromSeconds(1)));
            Assert.True(drop.ShouldExpire(now + MapDrop.ExpireAfter));
        }
    }

    [Fact]
    public void OnMobKilled_UsesStatsExperienceEntryAndCanLevelUp()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        player.Character.Exp = 14;
        var mob = MakeMob(level: 1, exp: 2);
        field.Add(player);
        field.Add(mob);

        var rewards = service.OnMobKilled(field, player, mob);

        Assert.Equal(2, rewards.ExpGained);
        Assert.NotNull(rewards.StatsMutation);
        Assert.Equal(2, player.Character.Level);
        Assert.Equal(1, player.Character.Exp);
        Assert.Contains(rewards.StatsMutation!.Updates, u => u.Kind == PlayerStatKind.Level && u.Value == 2);
        Assert.Contains(rewards.StatsMutation.Updates, u => u.Kind == PlayerStatKind.Exp && u.Value == 1);
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
            dropType: 0,
            spawnedAt: DateTimeOffset.UtcNow);
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
            dropType: 0,
            spawnedAt: DateTimeOffset.UtcNow);
        field.Add(player);
        field.Add(drop);

        var result = service.TryPickup(field, player, drop.ObjectId);

        Assert.True(result.Success);
        Assert.Equal(50, result.GainedMeso);
        Assert.Equal(50, player.Character.Meso);
        Assert.Null(field.Get(drop.ObjectId));
    }

    [Fact]
    public void TryDropMeso_DeductsMesoAndSpawnsPlayerDrop()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        player.Character.Meso = 1_000;
        field.Add(player);

        var result = service.TryDropMeso(field, player, 50);

        Assert.True(result.Success);
        Assert.Equal(950, player.Character.Meso);
        Assert.NotNull(result.Drop);
        Assert.Equal(50, result.Drop!.Meso);
        Assert.True(result.Drop.PlayerDrop);
        Assert.Equal(player.ObjectId, result.Drop.SourceObjectId);
        Assert.Same(result.Drop, field.Get(result.Drop.ObjectId));
    }

    [Theory]
    [InlineData(9, MesoDropStatus.InvalidAmount)]
    [InlineData(50_001, MesoDropStatus.InvalidAmount)]
    [InlineData(2_000, MesoDropStatus.NotEnoughMeso)]
    public void TryDropMeso_RejectsInvalidAmounts(int meso, MesoDropStatus status)
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        player.Character.Meso = 1_000;

        var result = service.TryDropMeso(field, player, meso);

        Assert.Equal(status, result.Status);
        Assert.Equal(1_000, player.Character.Meso);
        Assert.Null(result.Drop);
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
            dropType: 0,
            spawnedAt: DateTimeOffset.UtcNow);
        field.Add(player);
        field.Add(drop);

        var result = service.TryPickup(field, player, drop.ObjectId);

        Assert.Equal(DropPickupStatus.NotAllowed, result.Status);
        Assert.Same(drop, field.Get(drop.ObjectId));
        Assert.Empty(player.Inventory.By(InventoryType.Etc).Items);
    }

    // ── ExpireDrops（P062，M4-2 世界 tick 第二步）───────────────────────────────

    [Fact]
    public void ExpireDrops_RemovesOnlyDropsPastThreshold()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var now = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero);
        // 剛好在門檻之前出生 → 已過期；門檻前 10 秒才出生 → 還沒過期。
        var expiredDrop = NewItemDrop(1_000_000, now - MapDrop.ExpireAfter);
        var freshDrop = NewItemDrop(1_000_001, now - MapDrop.ExpireAfter + TimeSpan.FromSeconds(10));
        field.Add(expiredDrop);
        field.Add(freshDrop);

        var expired = service.ExpireDrops(field, now);

        var removed = Assert.Single(expired);
        Assert.Same(expiredDrop, removed);
        Assert.True(expiredDrop.IsPickedUp);
        Assert.Null(field.Get(expiredDrop.ObjectId));
        Assert.Same(freshDrop, field.Get(freshDrop.ObjectId));
    }

    [Fact]
    public void ExpireDrops_AlreadyPickedUpDrop_IsIgnored()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var spawnedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var drop = NewItemDrop(1_000_000, spawnedAt);
        drop.TryMarkPickedUp();
        field.Add(drop);

        var expired = service.ExpireDrops(field, spawnedAt + MapDrop.ExpireAfter + TimeSpan.FromMinutes(1));

        Assert.Empty(expired);
        Assert.Same(drop, field.Get(drop.ObjectId));
    }

    [Fact]
    public void ExpireDrops_NoExpiredDrops_ReturnsEmptyAndLeavesFieldUnchanged()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var spawnedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var drop = NewItemDrop(1_000_000, spawnedAt);
        field.Add(drop);

        var expired = service.ExpireDrops(field, spawnedAt + MapDrop.ExpireAfter - TimeSpan.FromSeconds(1));

        Assert.Empty(expired);
        Assert.Same(drop, field.Get(drop.ObjectId));
    }

    // ── PromoteFfaDrops（P069，對照 Java World.handleMap 的 item.shouldFFA()）───────────

    [Fact]
    public void PromoteFfaDrops_PastThreshold_MarksDropTypeTwoAndReturnsIt()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var spawnedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var drop = NewItemDrop(1_000_000, spawnedAt, dropType: 1);
        field.Add(drop);

        var promoted = service.PromoteFfaDrops(field, spawnedAt + MapDrop.FfaAfter);

        var single = Assert.Single(promoted);
        Assert.Same(drop, single);
        Assert.Equal((byte)2, drop.DropType);
        Assert.Same(drop, field.Get(drop.ObjectId)); // 只是狀態轉換，掉落物依然留在場上。
    }

    [Fact]
    public void PromoteFfaDrops_BeforeThreshold_LeavesDropTypeUnchanged()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var spawnedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var drop = NewItemDrop(1_000_000, spawnedAt, dropType: 0);
        field.Add(drop);

        var promoted = service.PromoteFfaDrops(field, spawnedAt + MapDrop.FfaAfter - TimeSpan.FromSeconds(1));

        Assert.Empty(promoted);
        Assert.Equal((byte)0, drop.DropType);
    }

    [Fact]
    public void PromoteFfaDrops_AlreadyFfa_NotReturnedAgain()
    {
        var service = MakeDropService();
        var field = new FieldInstance(100000100);
        var spawnedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var drop = NewItemDrop(1_000_000, spawnedAt, dropType: 2);
        field.Add(drop);

        var promoted = service.PromoteFfaDrops(field, spawnedAt + MapDrop.FfaAfter);

        Assert.Empty(promoted);
    }

    private static MapDrop NewItemDrop(int objectId, DateTimeOffset spawnedAt, byte dropType = 0) => MapDrop.ForItem(
        objectId,
        new Item { ItemId = 4000000, Quantity = 1 },
        new Position(10, 20, 0, 7),
        new Position(10, 20, 0, 7),
        sourceObjectId: 100001,
        ownerId: 1,
        dropType: dropType,
        spawnedAt: spawnedAt);

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
        => MakeDropService(timeProvider: null, entries);

    private static DropService MakeDropService(TimeProvider? timeProvider, params MonsterDropEntry[] entries)
    {
        var catalog = new InMemoryMonsterDropCatalog(new Dictionary<int, IReadOnlyList<MonsterDropEntry>>
        {
            [100100] = entries,
        });
        return new DropService(catalog, timeProvider: timeProvider);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
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
