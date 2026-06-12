using Maple.Adapters.V113.Channel;
using Maple.Application.NpcItemServices;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.NpcItemServices;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelNpcItemServiceHandlerTests
{
    [Fact]
    public void RepairHandler_Success_ProducesMesoAndInventoryPackets()
    {
        var player = CreateRepairPlayer();
        player.TrackEquipDurability(2, 500);
        var handler = new V113RepairHandler(new EquipRepairService(new FakeRepairCatalog(
            new EquipRepairDefinition(1302000, 1_000, 1_000, 30))));
        var body = new PacketWriter().WriteInt(2).ToArray();

        var result = handler.HandleRepair(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(500, player.Character.Meso);
        Assert.Equal(2, result.Packets.Count);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.Packets[1], 0));
    }

    [Fact]
    public void OwlHandler_MinervaWithEmptyCatalog_SendsEmptySearchAndEnableActions()
    {
        var player = CreateOwlPlayer(910000000);
        var handler = new V113OwlHandler(new OwlService(new EmptyOwlSearchCatalog()));
        var body = new PacketWriter()
            .WriteShort(2)
            .WriteInt(OwlService.MinervaOwlItemId)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.HandleMinerva(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Null(result.WarpMapId);
        Assert.Equal(2, result.Packets.Count);
        Assert.Equal(V113OwlPackets.SendShopScannerResult, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[1], 0));
    }

    [Fact]
    public void OwlHandler_WarpInFreeMarket_ReturnsWarpMap()
    {
        var player = CreateOwlPlayer(910000000);
        var handler = new V113OwlHandler(new OwlService(new EmptyOwlSearchCatalog()));
        var body = new PacketWriter()
            .WriteInt(9001)
            .WriteInt(910000001)
            .ToArray();

        var result = handler.HandleWarp(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.Equal(910000001, result.WarpMapId);
        Assert.Single(result.Packets);
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.Packets[0], 0));
    }

    private static Player CreateRepairPlayer()
    {
        var character = new Character
        {
            Id = 1,
            Name = "RepairAdapter",
            MapId = EquipRepairService.RepairMapId,
            Meso = 1_000,
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Equip,
                    IsEquip = true,
                    ItemId = 1302000,
                    Slot = 2,
                    Quantity = 1,
                    Expiration = -1,
                },
            ],
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static Player CreateOwlPlayer(int mapId)
    {
        var character = new Character
        {
            Id = 1,
            Name = "OwlAdapter",
            MapId = mapId,
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Use,
                    ItemId = OwlService.MinervaOwlItemId,
                    Slot = 2,
                    Quantity = 1,
                    Expiration = -1,
                },
            ],
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private sealed class FakeRepairCatalog : IEquipRepairCatalog
    {
        private readonly Dictionary<int, EquipRepairDefinition> _definitions;

        public FakeRepairCatalog(params EquipRepairDefinition[] definitions)
        {
            _definitions = definitions.ToDictionary(static d => d.ItemId);
        }

        public EquipRepairDefinition? GetRepairDefinition(int itemId)
            => _definitions.GetValueOrDefault(itemId);
    }
}
