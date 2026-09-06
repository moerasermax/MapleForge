using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Items;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelItemUseHandlerTests
{
    [Fact]
    public void HandleUseMountFood_ConsumesItemUpdatesMountAndReturnsBroadcastIntent()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2260000, Quantity = 2 });
        player.SetMount(new PlayerMountState(itemId: 1902000, skillId: 1004, level: 1, exp: 0, fatigue: 40));
        var handler = NewHandler(randomValues: new[] { 0 });

        var result = handler.HandleUseMountFood(new V113UseItemRequest(1234, 1, 2260000), player);

        Assert.True(result.Applied);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Equal(10, player.Mount!.Fatigue);
        Assert.Equal(15, player.Mount.Exp);
        Assert.Single(result.InventoryMutations);
        Assert.Equal(2, result.SelfPackets.Count);
        Assert.Single(result.BroadcastPackets);
        Assert.Equal(0x2D, new PacketReader(result.BroadcastPackets[0]).ReadShort());
    }

    [Fact]
    public void HandleUseSummonBag_ConsumesItemAndReturnsSpawnMonsterIdsByJavaProbability()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2100000, Quantity = 1 });
        var catalog = new FakeItemUseCatalog();
        catalog.SummonBags[2100000] = new List<SummonBagMobEntry>
        {
            new(100100, 50),
            new(100101, 10),
            new(100102, 100),
        };
        var handler = NewHandler(catalog, randomValues: new[] { 50, 11, 98 });

        var result = handler.HandleUseSummonBag(
            new V113UseItemRequest(1234, 1, 2100000),
            player,
            new V113ItemUseContext { CanUseSummonBag = true });

        Assert.True(result.Applied);
        Assert.Equal(new[] { 100100, 100102 }, result.SpawnMonsterIds);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
        Assert.Single(result.InventoryMutations);
        Assert.Equal(2, result.SelfPackets.Count);
    }

    [Fact]
    public void HandleUseSummonBag_FieldLimitBlocked_ConsumesItemButSpawnsNothing()
    {
        // 對照 Java InventoryHandler：removeFromSlot 在 FieldLimitType.SummoningBag 檢查之前，
        // 場地限制只擋「召喚」，道具照樣被消耗掉（照抄這個看似不利玩家的 Java 行為，不修正）。
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2100000, Quantity = 1 });
        var catalog = new FakeItemUseCatalog();
        catalog.SummonBags[2100000] = new List<SummonBagMobEntry> { new(100100, 100) };
        var handler = NewHandler(catalog, randomValues: new[] { 0 });

        var result = handler.HandleUseSummonBag(
            new V113UseItemRequest(1234, 1, 2100000),
            player,
            new V113ItemUseContext { CanUseSummonBag = false });

        Assert.True(result.Applied);
        Assert.Empty(result.SpawnMonsterIds);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
    }

    [Fact]
    public void HandleUseSummonBag_GmBypassesFieldLimit()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2100000, Quantity = 1 });
        var catalog = new FakeItemUseCatalog();
        catalog.SummonBags[2100000] = new List<SummonBagMobEntry> { new(100100, 100) };
        var handler = NewHandler(catalog, randomValues: new[] { 0 });

        var result = handler.HandleUseSummonBag(
            new V113UseItemRequest(1234, 1, 2100000),
            player,
            new V113ItemUseContext { CanUseSummonBag = false, IsGm = true });

        Assert.Equal(new[] { 100100 }, result.SpawnMonsterIds);
    }

    [Fact]
    public void HandleUseReturnScroll_FieldLimitBlocked_DoesNotConsumeItem()
    {
        // 對照 Java InventoryHandler.UseReturnScroll：FieldLimitType.PotionUse 檢查包住整個
        // apply+consume 區塊，被擋時道具完全不消耗（跟 SummonBag 的行為不同，各自照抄）。
        var player = NewPlayer(mapId: 100000000);
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2030000, Quantity = 1 });
        var catalog = new FakeItemUseCatalog();
        catalog.ReturnScrollDestinations[2030000] = V113ItemUseHandler.ReturnMapSentinel;
        var handler = NewHandler(catalog);

        var result = handler.HandleUseReturnScroll(
            new V113UseItemRequest(1234, 1, 2030000),
            player,
            new V113ItemUseContext { ReturnMapId = 100000001, CanUseReturnScroll = false });

        Assert.False(result.Applied);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
    }

    [Fact]
    public void HandleUseReturnScroll_ConsumesItemAndReturnsWarpMapIntent()
    {
        var player = NewPlayer(mapId: 100000000);
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2030000, Quantity = 1 });
        var catalog = new FakeItemUseCatalog();
        catalog.ReturnScrollDestinations[2030000] = V113ItemUseHandler.ReturnMapSentinel;
        var handler = NewHandler(catalog);

        var result = handler.HandleUseReturnScroll(
            new V113UseItemRequest(1234, 1, 2030000),
            player,
            new V113ItemUseContext { ReturnMapId = 100000001, CanUseReturnScroll = true });

        Assert.True(result.Applied);
        Assert.Equal(100000001, result.WarpMapId);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
        Assert.Single(result.InventoryMutations);
        Assert.Single(result.SelfPackets);
        Assert.Equal(0x1B, new PacketReader(result.SelfPackets[0]).ReadShort());
    }

    [Fact]
    public void HandleUseCatchItem_SuccessRemovesMobAndGrantsRewardIntent()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2270004, Quantity = 1 });
        var handler = NewHandler();
        var target = new V113ItemUseTargetMob(ObjectId: 100001, MonsterId: 910709, Hp: 50, MaxHp: 100);

        var result = handler.HandleUseCatchItem(
            new V113UseCatchItemRequest(1234, 1, 2270004, 100001),
            player,
            target);

        Assert.True(result.Applied);
        Assert.Equal(100001, result.RemoveMonsterObjectId);
        Assert.Single(result.GainItems);
        Assert.Equal(4001169, result.GainItems[0].ItemId);
        Assert.Equal(1, player.Inventory.By(InventoryType.Etc).CountById(4001169));
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
        Assert.Single(result.BroadcastPackets);
        var catchPacket = new PacketReader(result.BroadcastPackets[0]);
        Assert.Equal(unchecked((short)0xF5), catchPacket.ReadShort());
        Assert.Equal(910709, catchPacket.ReadInt());
        Assert.Equal(2270004, catchPacket.ReadInt());
        Assert.Equal(1, catchPacket.ReadByte());
    }

    [Fact]
    public void HandleUseCatchItem_HighHpFailureDoesNotConsumeItem()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2270004, Quantity = 1 });
        var handler = NewHandler();
        var target = new V113ItemUseTargetMob(ObjectId: 100001, MonsterId: 910709, Hp: 51, MaxHp: 100);

        var result = handler.HandleUseCatchItem(
            new V113UseCatchItemRequest(1234, 1, 2270004, 100001),
            player,
            target);

        Assert.False(result.Applied);
        Assert.Null(result.RemoveMonsterObjectId);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Single(result.BroadcastPackets);
        Assert.Single(result.SelfMessages);
        var catchPacket = new PacketReader(result.BroadcastPackets[0]);
        catchPacket.Skip(10);
        Assert.Equal(0, catchPacket.ReadByte());
    }

    private static V113ItemUseHandler NewHandler(
        FakeItemUseCatalog? catalog = null,
        IReadOnlyList<int>? randomValues = null) =>
        new(catalog ?? new FakeItemUseCatalog(), new SequenceRandom(randomValues ?? Array.Empty<int>()));

    private static Player NewPlayer(int mapId = 100000000) =>
        new(
            new Character { Id = 1, Name = "ItemUseUser", MapId = mapId },
            new Position(0, 0, 0, 0));

    private sealed class FakeItemUseCatalog : IItemUseCatalog
    {
        public Dictionary<int, int> ReturnScrollDestinations { get; } = new();

        public Dictionary<int, IReadOnlyList<SummonBagMobEntry>> SummonBags { get; } = new();

        public int? GetReturnScrollDestinationMapId(int itemId) =>
            ReturnScrollDestinations.TryGetValue(itemId, out var destination) ? destination : null;

        public IReadOnlyList<SummonBagMobEntry>? GetSummonBagMobs(int itemId) =>
            SummonBags.TryGetValue(itemId, out var mobs) ? mobs : null;
    }

    private sealed class SequenceRandom : IV113ItemUseRandomSource
    {
        private readonly Queue<int> _values;

        public SequenceRandom(IReadOnlyList<int> values)
        {
            _values = new Queue<int>(values);
        }

        public int NextInt(int exclusiveMax)
        {
            if (_values.Count == 0)
            {
                return 0;
            }

            var value = _values.Dequeue();
            Assert.InRange(value, 0, exclusiveMax - 1);
            return value;
        }
    }
}
