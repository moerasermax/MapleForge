using Maple.Application.NpcItemServices;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.NpcItemServices;
using Maple.Core.World;

namespace Maple.Application.Tests.NpcItemServices;

public sealed class EquipRepairServiceTests
{
    [Fact]
    public void Repair_SingleEquip_DeductsMesoAndRestoresDurability()
    {
        var player = CreatePlayer(meso: 1_000, BagEquip(1302000, 2));
        player.TrackEquipDurability(2, 500);
        var service = new EquipRepairService(new FakeRepairCatalog(
            new EquipRepairDefinition(1302000, MaxDurability: 1_000, Price: 1_000, RequiredLevel: 30)));

        var result = service.Repair(player, 2);

        Assert.Equal(EquipRepairStatus.Success, result.Status);
        Assert.Equal(500, result.Price);
        Assert.Equal(500, player.Character.Meso);
        Assert.Single(result.Mutations);
        Assert.True(player.TryGetEquipForRepair(2, out var repaired));
        Assert.Equal(1_000, repaired.CurrentDurability);
    }

    [Fact]
    public void RepairAll_IncludesBagAndEquippedItems()
    {
        var player = CreatePlayer(
            meso: 20_000,
            BagEquip(1302000, 2),
            equipped: new EquipEntry { Position = -11, ItemId = 1402000 });
        player.TrackEquipDurability(2, 500);
        player.TrackEquipDurability(-11, 300);
        var service = new EquipRepairService(new FakeRepairCatalog(
            new EquipRepairDefinition(1302000, 1_000, 1_000, 30),
            new EquipRepairDefinition(1402000, 600, 100, 105)));

        var result = service.RepairAll(player);

        Assert.Equal(EquipRepairStatus.Success, result.Status);
        Assert.Equal(5_500, result.Price);
        Assert.Equal(14_500, player.Character.Meso);
        Assert.Equal(2, result.Mutations.Count);
        Assert.True(player.TryGetEquipForRepair(2, out var bag));
        Assert.True(player.TryGetEquipForRepair(-11, out var equipped));
        Assert.Equal(1_000, bag.CurrentDurability);
        Assert.Equal(600, equipped.CurrentDurability);
    }

    [Fact]
    public void Repair_OutsideLeafre_DoesNotMutate()
    {
        var player = CreatePlayer(meso: 1_000, BagEquip(1302000, 2));
        player.Character.MapId = 100000000;
        player.TrackEquipDurability(2, 500);
        var service = new EquipRepairService(new FakeRepairCatalog(
            new EquipRepairDefinition(1302000, 1_000, 1_000, 30)));

        var result = service.Repair(player, 2);

        Assert.Equal(EquipRepairStatus.NotInRepairMap, result.Status);
        Assert.Equal(1_000, player.Character.Meso);
        Assert.True(player.TryGetEquipForRepair(2, out var state));
        Assert.Equal(500, state.CurrentDurability);
    }

    private static Player CreatePlayer(int meso, ItemRecord bagEquip, EquipEntry? equipped = null)
    {
        var character = new Character
        {
            Id = 1,
            Name = "RepairUser",
            MapId = EquipRepairService.RepairMapId,
            Meso = meso,
            Items = new List<ItemRecord> { bagEquip },
        };

        if (equipped is not null)
        {
            character.Equips.Add(equipped);
        }

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static ItemRecord BagEquip(int itemId, short slot) => new()
    {
        Type = (byte)InventoryType.Equip,
        IsEquip = true,
        ItemId = itemId,
        Slot = slot,
        Quantity = 1,
        Expiration = -1,
    };

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
