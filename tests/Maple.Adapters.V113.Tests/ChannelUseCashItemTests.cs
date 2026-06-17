using Maple.Adapters.V113.Channel;
using Maple.Application.NpcItemServices;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.NpcItemServices;
using Maple.Core.World;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelUseCashItemTests
{
    [Fact]
    public void ParseUseCashItem_ReadsSlotAndItemId()
    {
        var body = new PacketWriter()
            .WriteShort(3)          // slot
            .WriteInt(5230000)      // itemId
            .WriteInt(2000000)      // searchItemId (for Owl)
            .ToArray();

        var reader = new PacketReader(body);
        var slot = reader.ReadShort();
        var itemId = reader.ReadInt();

        Assert.Equal(3, slot);
        Assert.Equal(5230000, itemId);
    }

    [Fact]
    public void OwlRouting_WithCashOwlItem_ProducesOwlSearchedPacket()
    {
        var player = CreateCashOwlPlayer(910000000);
        var handler = CreateHandlerWithResults();
        var body = new PacketWriter()
            .WriteShort(1)          // slot
            .WriteInt(OwlService.CashOwlItemId)  // 5230000
            .WriteInt(2000000)      // searchItemId
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        // Java order: OwlSearched first, then ModifyInventory (consume), then EnableActions
        Assert.Equal(3, result.Packets.Count);
        Assert.Equal(V113OwlPackets.SendShopScannerResult, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.Packets[1], 0));
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[2], 0));
    }

    [Fact]
    public void OwlRouting_EmptyResults_ReturnsEnableActions_NoConsumption()
    {
        var player = CreateCashOwlPlayer(910000000);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        // Item should NOT be consumed when search returns empty
        var item = player.Inventory.By(InventoryType.Cash).Get(1);
        Assert.NotNull(item);
        Assert.Equal(1, item.Quantity);
    }

    [Fact]
    public void UnknownItemId_ReturnsEnableActions()
    {
        var player = CreatePlayerWithCashItem(910000000, 5999999, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(5999999)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
    }

    [Fact]
    public void MissingItem_ReturnsEnableActions()
    {
        // Player has no cash items at all
        var character = new Character
        {
            Id = 1,
            Name = "NoCashItems",
            MapId = 910000000,
            Items = [],
        };
        var player = new Player(character, new Position(0, 0, 0, 0));
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
    }

    [Fact]
    public void MismatchedItemId_ReturnsEnableActions()
    {
        // Player has a different item at the specified slot
        var player = CreatePlayerWithCashItem(910000000, 5100000, 1);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)  // claims 5230000 but slot has 5100000
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
    }

    [Fact]
    public void OwlRouting_ConsumesOneCashItem()
    {
        var player = CreateCashOwlPlayer(910000000);
        var handler = CreateHandlerWithResults();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        handler.Handle(new PacketReader(body), player);

        // Item quantity should be 0 after consumption
        var item = player.Inventory.By(InventoryType.Cash).Get(1);
        Assert.NotNull(item);
        Assert.Equal(0, item.Quantity);
    }

    [Fact]
    public void OwlRouting_NotInFreeMarket_ReturnsEnableActions()
    {
        // Player is in a normal map, not Free Market
        var player = CreateCashOwlPlayer(100000000);
        var handler = CreateHandler();
        var body = new PacketWriter()
            .WriteShort(1)
            .WriteInt(OwlService.CashOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        // Item should NOT be consumed
        var item = player.Inventory.By(InventoryType.Cash).Get(1);
        Assert.NotNull(item);
    }

    [Fact]
    public void OpcodeConstant_MatchesJavaValue()
    {
        Assert.Equal(0x49, V113ChannelRecvOp.UseCashItem);
    }

    private static V113UseCashItemHandler CreateHandler()
        => new(
            new OwlService(new EmptyOwlSearchCatalog()),
            NullLogger<V113UseCashItemHandler>.Instance);

    private static V113UseCashItemHandler CreateHandlerWithResults()
        => new(
            new OwlService(new TestOwlSearchCatalog()),
            NullLogger<V113UseCashItemHandler>.Instance);

    private sealed class TestOwlSearchCatalog : IOwlSearchCatalog
    {
        public IReadOnlyList<OwlSearchEntry> Search(int itemId)
            => [new OwlSearchEntry("TestShop", 910000000, "Test Item", 1, 1, 100, 1, 0, InventoryType.Etc)];
    }

    private static Player CreateCashOwlPlayer(int mapId)
    {
        var character = new Character
        {
            Id = 1,
            Name = "CashOwl",
            MapId = mapId,
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Cash,
                    ItemId = OwlService.CashOwlItemId,
                    Slot = 1,
                    Quantity = 1,
                    Expiration = -1,
                },
            ],
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static Player CreatePlayerWithCashItem(int mapId, int itemId, short slot)
    {
        var character = new Character
        {
            Id = 1,
            Name = "CashPlayer",
            MapId = mapId,
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Cash,
                    ItemId = itemId,
                    Slot = slot,
                    Quantity = 1,
                    Expiration = -1,
                },
            ],
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }
}
