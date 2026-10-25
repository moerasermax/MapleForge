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
    public void Use_Elixir_RateAndCapIncludeEquipHp()
    {
        // P115：Java calcHPChange 用 getCurrentMaxHp（基礎 200 ＋ 裝備 100 = 300）；Elixir 50%。
        var player = NewPlayer(hp: 50, maxHp: 200, mp: 20, maxMp: 100, UseItem(2001000, 1, 1));
        player.Character.Equips.Add(new EquipEntry { Position = -5, ItemId = 1040002, Hp = 100 });
        var service = NewService();

        var result = service.Use(player, slot: 1, itemId: 2001000);

        Assert.True(result.Success);
        Assert.Equal((short)200, player.Hp);
    }

    [Fact]
    public void Use_NightLordWithAlchemist_BoostsFlatRecovery()
    {
        // P120：Java alchemistModifyVal：紅藥水 50 HP × 藥劑精通 X=150 / 100 = 75。
        var player = NewPlayer(hp: 1, maxHp: 500, mp: 10, maxMp: 50, UseItem(2000000, 1, 1));
        player.Character.Job = 411;
        player.ChangeSkillLevel(4110000, level: 1, masterLevel: 0);
        var skills = new Maple.Application.Skills.InMemorySkillCatalog(new[]
        {
            new Maple.Core.Skills.MapleSkill
            {
                Id = 4110000,
                Effects = [new Maple.Core.Skills.MapleStatEffect { SourceId = 4110000, Level = 1, X = 150 }],
            },
        });
        var service = new UseItemService(new HardcodedItemEffectCatalog(), skills);

        service.Use(player, slot: 1, itemId: 2000000);

        Assert.Equal((short)76, player.Hp);
    }

    [Fact]
    public void Use_CureItem_DispelsListedDiseases()
    {
        var player = NewPlayer(hp: 50, maxHp: 100, mp: 10, maxMp: 50, UseItem(2050004, 1, 1));
        player.TryGiveDisease(Maple.Core.World.PlayerDisease.Poison, 1, 5000, 125, 1, DateTimeOffset.UnixEpoch);
        player.TryGiveDisease(Maple.Core.World.PlayerDisease.Stun, 1, 5000, 123, 1, DateTimeOffset.UnixEpoch);
        var service = new UseItemService(new CureCatalog());

        var result = service.Use(player, slot: 1, itemId: 2050004);

        Assert.Equal(new[] { Maple.Core.World.PlayerDisease.Poison }, result.CuredDiseases);
        Assert.True(player.HasDisease(Maple.Core.World.PlayerDisease.Stun));
    }

    private sealed class CureCatalog : Maple.Core.Items.IItemEffectCatalog
    {
        public Maple.Core.Items.ItemEffect? GetEffect(int itemId)
            => new(itemId, 0, 0, 0, 0, [Maple.Core.World.PlayerDisease.Poison, Maple.Core.World.PlayerDisease.Seal]);
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
