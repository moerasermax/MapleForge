using Maple.Adapters.V113.Channel;
using Maple.Application.Items;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelRewardItemHandlerTests
{
    [Fact]
    public void ShowExpChair_ParseReadsChairIdAndEnableActionsFixture()
    {
        var request = V113RewardItemHandler.ParseShowExpChair(Reader(w => w.WriteInt(3010000)));

        Assert.Equal(3010000, request.ChairId);
        Assert.Equal(new byte[] { 0x1D, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 }, V113StatsPackets.EnableActions());
    }

    [Fact]
    public void ThrowGrenade_JavaHandlerIsEmpty_ParserPreservesPayloadAndEnableActionsFixture()
    {
        var request = V113RewardItemHandler.ParseThrowGrenade(Reader(w =>
            w.WriteInt(100)
                .WriteInt(200)
                .WriteInt(4211004)));

        Assert.Equal(new byte[] { 100, 0, 0, 0, 200, 0, 0, 0, 0x3C, 0x41, 0x40, 0 }, request.Payload);
        Assert.Equal(V113StatsPackets.EnableActions(), new byte[] { 0x1D, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 });
    }

    [Fact]
    public void RewardItem_ParseReadsSlotAndItemId()
    {
        var request = V113RewardItemHandler.ParseRewardItem(Reader(w => w.WriteShort(3).WriteInt(5530000)));

        Assert.Equal(3, request.Slot);
        Assert.Equal(5530000, request.ItemId);
    }

    [Fact]
    public void RewardItem_ConsumesContainerAndGrantsDeterministicReward_UnverifiedAnimationFixture()
    {
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Cash,
            ItemId = 5530000,
            Slot = 3,
            Quantity = 1,
        });

        var result = V113RewardItemHandler.HandleRewardItem(
            Reader(w => w.WriteShort(3).WriteInt(5530000)),
            player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(4, result.SelfPackets.Count);
        Assert.Equal(new byte[] { 0x1B, 0x00, 0, 1, 3, 5, 3, 0 }, result.SelfPackets[0]);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.SelfPackets[1], 0));
        Assert.Equal(new byte[] { 0xC7, 0x00, 0x0B, 0x80, 0x84, 0x1E, 0x00, 0 }, result.SelfPackets[2]);
        Assert.Equal(V113StatsPackets.EnableActions(), result.SelfPackets[3]);

        // server-to-client reward animation is Java-source candidate/unverified until live client smoke.
        Assert.Single(result.BroadcastPackets);
        Assert.Equal(new byte[] { 0xBF, 0x00, 0x2A, 0, 0, 0, 0x0B, 0x80, 0x84, 0x1E, 0x00, 0 }, result.BroadcastPackets[0]);
        Assert.Equal(1, player.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Contains(player.Character.Items, item => item is { Type: (byte)InventoryType.Use, ItemId: 2000000, Quantity: 1 });
    }

    [Fact]
    public void RewardItem_MissingContainerOnlyEnablesActions()
    {
        var result = V113RewardItemHandler.HandleRewardItem(
            Reader(w => w.WriteShort(3).WriteInt(5530000)),
            PlayerWithItems());

        Assert.False(result.CharacterMutated);
        Assert.Single(result.SelfPackets);
        Assert.Equal(V113StatsPackets.EnableActions(), result.SelfPackets[0]);
        Assert.Empty(result.BroadcastPackets);
    }

    [Fact]
    public void TreasureChest_ParseReadsSlotAndItemId()
    {
        var request = V113RewardItemHandler.ParseTreasureChest(Reader(w => w.WriteShort(2).WriteInt(4280000)));

        Assert.Equal(2, request.Slot);
        Assert.Equal(4280000, request.ItemId);
    }

    [Fact]
    public void TreasureChest_GoldConsumesChestAndKeyAndGrantsWeightedReward()
    {
        var player = PlayerWithItems(
            new ItemRecord
            {
                Type = (byte)InventoryType.Etc,
                ItemId = 4280000,
                Slot = 2,
                Quantity = 1,
            },
            new ItemRecord
            {
                Type = (byte)InventoryType.Cash,
                ItemId = 5490000,
                Slot = 1,
                Quantity = 1,
            });

        // index 0 of the compiled gold table always lands on the first entry (1302059, 龍泉劍),
        // matching Java GameConstants.goldrewards[0] — see RandomRewardsCatalogTests for full-table coverage.
        var result = V113RewardItemHandler.HandleTreasureChest(
            Reader(w => w.WriteShort(2).WriteInt(4280000)),
            player,
            new RandomRewardsCatalog(new FixedIndexRandom(0)));

        Assert.True(result.CharacterMutated);
        Assert.Equal(5, result.SelfPackets.Count);
        Assert.Equal(new byte[] { 0x1B, 0x00, 0, 1, 3, 4, 2, 0 }, result.SelfPackets[0]);
        Assert.Equal(new byte[] { 0x1B, 0x00, 0, 1, 3, 5, 1, 0 }, result.SelfPackets[1]);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.SelfPackets[2], 0));
        Assert.Equal(new byte[] { 0xC7, 0x00, 0x0B, 0x2B, 0xDE, 0x13, 0x00, 0 }, result.SelfPackets[3]);
        Assert.Equal(V113StatsPackets.EnableActions(), result.SelfPackets[4]);
        Assert.Equal(1, player.Inventory.By(InventoryType.Equip).CountById(1302059));
    }

    [Fact]
    public void TreasureChest_SuperPotionReward_GrantsJavaSpecialQuantity()
    {
        var player = PlayerWithItems(
            new ItemRecord { Type = (byte)InventoryType.Etc, ItemId = 4280000, Slot = 2, Quantity = 1 },
            new ItemRecord { Type = (byte)InventoryType.Cash, ItemId = 5490000, Slot = 1, Quantity = 1 });

        // Index 89 of the compiled gold table lands on itemId 2000005 (超級藥水) — see
        // RandomRewardsCatalogTests.GoldTable_IndexNinetyNine_IsSuperPotion for the derivation.
        var result = V113RewardItemHandler.HandleTreasureChest(
            Reader(w => w.WriteShort(2).WriteInt(4280000)),
            player,
            new RandomRewardsCatalog(new FixedIndexRandom(89)));

        Assert.True(result.CharacterMutated);
        Assert.Equal(100, player.Inventory.By(InventoryType.Use).CountById(2000005));
    }

    [Fact]
    public void TreasureChest_MissingKeyDoesNotConsumeChest()
    {
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Etc,
            ItemId = 4280000,
            Slot = 2,
            Quantity = 1,
        });

        var result = V113RewardItemHandler.HandleTreasureChest(
            Reader(w => w.WriteShort(2).WriteInt(4280000)),
            player,
            new RandomRewardsCatalog(new FixedIndexRandom(0)));

        Assert.False(result.CharacterMutated);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Etc).Get(2)!.Quantity);
        Assert.Single(result.SelfPackets);
        Assert.Equal(V113StatsPackets.EnableActions(), result.SelfPackets[0]);
    }

    /// <summary>Test double：<see cref="Random.Next(int)"/> 永遠回傳固定索引，讓加權抽獎在測試中可預期。</summary>
    private sealed class FixedIndexRandom(int index) : Random
    {
        public override int Next(int maxValue) => index;
    }

    [Fact]
    public void ShowRewardItemAnimation_UnverifiedSelfFixtureWritesJavaSourceCandidateLayout()
    {
        var packet = V113RewardItemHandler.ShowRewardItemAnimation(2000000, "Effect/Reward");

        Assert.Equal(0xC7, BitConverter.ToInt16(packet, 0));
        Assert.Equal(0x0B, packet[2]);
        Assert.Equal(2000000, BitConverter.ToInt32(packet, 3));
        Assert.Equal(1, packet[7]);
    }

    private static PacketReader Reader(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return new PacketReader(writer.ToArray());
    }

    private static Player PlayerWithItems(params ItemRecord[] items)
        => new(
            new Character
            {
                Id = 42,
                Name = "RewardUser",
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));
}
