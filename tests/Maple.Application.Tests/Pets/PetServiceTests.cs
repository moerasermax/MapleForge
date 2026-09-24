using Maple.Application.Pets;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.Pets;

/// <summary>
/// cm 之外的另一種「先驗證再交給呼叫端套用」模式：<see cref="PetService.HandleAutoPotion"/>
/// 只做存活/道具存在驗證並回傳 itemId，實際套用+消耗委派給
/// <c>V113UseConsumableHandler.HandleKnownItem</c>（P035）。
/// </summary>
public sealed class PetServiceTests
{
    [Fact]
    public void HandleAutoPotion_AliveWithItem_ReturnsSuccessAndItemId()
    {
        var player = NewPlayer(hp: 50);
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 3, ItemId = 2000003, Quantity = 2 });
        var service = new PetService();

        var result = service.HandleAutoPotion(player, slot: 3);

        Assert.True(result.Success);
        Assert.Equal(2000003, result.ItemId);
    }

    [Fact]
    public void HandleAutoPotion_PlayerDead_ReturnsUnsupported()
    {
        var player = NewPlayer(hp: 0);
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 3, ItemId = 2000003, Quantity = 2 });
        var service = new PetService();

        var result = service.HandleAutoPotion(player, slot: 3);

        Assert.Equal(PetActionStatus.Unsupported, result.Status);
    }

    [Fact]
    public void HandleAutoPotion_EmptySlot_ReturnsInvalidItem()
    {
        var player = NewPlayer(hp: 50);
        var service = new PetService();

        var result = service.HandleAutoPotion(player, slot: 3);

        Assert.Equal(PetActionStatus.InvalidItem, result.Status);
    }

    [Fact]
    public void SpawnPet_SamePetAgain_TogglesDespawn()
    {
        // P079：對照 Java spawnPet 的 pet.getSummoned() → unequipPet（收回）。
        var player = NewPlayer(hp: 50);
        player.Inventory.By(InventoryType.Cash).Put(new Item { Slot = 1, ItemId = 5000000, Quantity = 1 });
        var service = new PetService();

        var first = service.SpawnPet(player, cashSlot: 1, lead: false);
        var second = service.SpawnPet(player, cashSlot: 1, lead: false);

        Assert.True(first.Success);
        Assert.False(first.Despawned);
        Assert.True(second.Success);
        Assert.True(second.Despawned);
        Assert.Same(first.Pet, second.Pet);
        Assert.Null(service.GetActivePet(player));
    }

    [Fact]
    public void SpawnPet_DifferentPetWhileOneActive_ReportsReplacedPet()
    {
        var player = NewPlayer(hp: 50);
        player.Inventory.By(InventoryType.Cash).Put(new Item { Slot = 1, ItemId = 5000000, Quantity = 1 });
        player.Inventory.By(InventoryType.Cash).Put(new Item { Slot = 2, ItemId = 5000001, Quantity = 1 });
        var service = new PetService();

        var first = service.SpawnPet(player, cashSlot: 1, lead: false);
        var second = service.SpawnPet(player, cashSlot: 2, lead: false);

        Assert.False(second.Despawned);
        Assert.Same(first.Pet, second.ReplacedPet);
        Assert.Equal(5000001, service.GetActivePet(player)?.ItemId);
    }

    private static Player NewPlayer(short hp)

        => new(
            new Character
            {
                Id = 1,
                Name = "PetOwner",
                Stats = new CharacterStats { Hp = hp, MaxHp = 100, Mp = 10, MaxMp = 50 },
            },
            new Position(0, 0, 0, 0));
}
