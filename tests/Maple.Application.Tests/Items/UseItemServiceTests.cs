using Maple.Application.Items;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.Items;

public sealed class UseItemServiceTests
{
    [Fact]
    public void Use_RedPotion_HealsHp()
    {
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50, UseItem(2000000, 1, 2));
        var service = NewService();

        var result = service.Use(player, slot: 1, itemId: 2000000);

        Assert.True(result.Success);
        Assert.Equal((short)75, player.Hp);
        Assert.Contains(result.StatUpdates, update => update.Kind == PlayerStatKind.Hp && update.Value == 75);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
    }

    [Fact]
    public void Use_Elixir_HealsByRate()
    {
        var player = NewPlayer(hp: 50, maxHp: 200, mp: 20, maxMp: 100, UseItem(2001000, 1, 1));
        var service = NewService();

        var result = service.Use(player, slot: 1, itemId: 2001000);

        Assert.True(result.Success);
        Assert.Equal((short)150, player.Hp);
        Assert.Equal((short)70, player.Mp);
        Assert.Contains(result.StatUpdates, update => update.Kind == PlayerStatKind.Hp && update.Value == 150);
        Assert.Contains(result.StatUpdates, update => update.Kind == PlayerStatKind.Mp && update.Value == 70);
    }

    [Fact]
    public void Use_ClampsHpToMaxHp()
    {
        var player = NewPlayer(hp: 90, maxHp: 100, mp: 10, maxMp: 50, UseItem(2000002, 1, 1));
        var service = NewService();

        var result = service.Use(player, slot: 1, itemId: 2000002);

        Assert.True(result.Success);
        Assert.Equal((short)100, player.Hp);
        Assert.Contains(result.StatUpdates, update => update.Kind == PlayerStatKind.Hp && update.Value == 100);
    }

    [Fact]
    public void Use_ConsumesOneItem()
    {
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50, UseItem(2000000, 1, 1));
        var service = NewService();

        var result = service.Use(player, slot: 1, itemId: 2000000);

        Assert.True(result.Success);
        Assert.NotNull(result.InventoryMutation);
        Assert.True(result.InventoryMutation.Removed);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
        Assert.DoesNotContain(player.Character.Items, item => item.ItemId == 2000000);
    }

    [Fact]
    public void Use_DeadPlayer_Fails()
    {
        var player = NewPlayer(hp: 0, maxHp: 100, mp: 10, maxMp: 50, UseItem(2000000, 1, 1));
        var service = NewService();

        var result = service.Use(player, slot: 1, itemId: 2000000);

        Assert.False(result.Success);
        Assert.Null(result.InventoryMutation);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
    }

    [Fact]
    public void Use_UnknownItem_Fails()
    {
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50, UseItem(2100000, 1, 1));
        var service = NewService();

        var result = service.Use(player, slot: 1, itemId: 2100000);

        Assert.False(result.Success);
        Assert.Null(result.InventoryMutation);
        Assert.Equal((short)25, player.Hp);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
    }

    [Fact]
    public void Use_MissingItem_Fails()
    {
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50);
        var service = NewService();

        var result = service.Use(player, slot: 1, itemId: 2000000);

        Assert.False(result.Success);
        Assert.Null(result.InventoryMutation);
        Assert.Equal((short)25, player.Hp);
    }

    private static UseItemService NewService() => new(new HardcodedItemEffectCatalog());

    private static Player NewPlayer(short hp, short maxHp, short mp, short maxMp, params ItemRecord[] items)
        => new(
            new Character
            {
                Id = 1,
                Name = "UseItemUser",
                Stats = new CharacterStats
                {
                    Hp = hp,
                    MaxHp = maxHp,
                    Mp = mp,
                    MaxMp = maxMp,
                },
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private static ItemRecord UseItem(int itemId, short slot, short quantity)
        => new()
        {
            Type = (byte)InventoryType.Use,
            ItemId = itemId,
            Slot = slot,
            Quantity = quantity,
            Expiration = -1,
        };
}
