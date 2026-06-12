using Maple.Core.CashShop;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class PlayerBuffItemsTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UseSolomonBook_ConsumesUseItemAndStoresGachaponExp()
    {
        var player = MakePlayer(level: 20);
        player.Inventory.By(InventoryType.Use).Put(new Item { ItemId = 2370005, Slot = 2, Quantity = 2 });

        var result = player.UseSolomonBook(slot: 2, itemId: 2370005, experience: 5_000);

        Assert.True(result.Success);
        Assert.Equal(5_000, player.Character.GachExp);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(2)?.Quantity);
        Assert.NotNull(result.Consume);
        Assert.False(result.Consume!.Removed);
    }

    [Fact]
    public void UseSolomonBook_RejectsWhenExperienceAlreadyPending()
    {
        var player = MakePlayer(level: 20);
        player.Character.GachExp = 10;
        player.Inventory.By(InventoryType.Use).Put(new Item { ItemId = 2370005, Slot = 2, Quantity = 1 });

        var result = player.UseSolomonBook(slot: 2, itemId: 2370005, experience: 5_000);

        Assert.Equal(SolomonBookUseStatus.GachaponExpAlreadyPending, result.Status);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(2)?.Quantity);
    }

    [Fact]
    public void ClaimGachaponExperience_ClearsPendingExperience()
    {
        var player = MakePlayer(level: 20);
        player.Character.GachExp = 1234;

        var result = player.ClaimGachaponExperience();

        Assert.True(result.Success);
        Assert.Equal(1234, result.ClaimedExperience);
        Assert.Equal(0, player.Character.GachExp);
    }

    [Fact]
    public void ApplyTransformEffect_RegistersMorphBuff()
    {
        var player = MakePlayer(level: 20);
        var effect = new MapleStatEffect
        {
            SourceId = 2210023,
            IsSkill = false,
            IsOverTime = true,
            DurationMilliseconds = 3_600_000,
            Statups = new[] { new BuffStatValue(MapleBuffStat.MORPH, 23) },
        };

        var applied = player.ApplySkillEffect(effect, FixedNow);

        Assert.Equal(PlayerSkillApplicationStatus.Applied, applied.Status);
        var buff = Assert.Single(player.ActiveBuffs);
        Assert.Equal(MapleBuffStat.MORPH, buff.Stat);
        Assert.Equal(23, buff.Value);
        Assert.Equal(2210023, buff.SourceId);
    }

    [Fact]
    public void OpenXmasSurpriseBox_ConsumesBoxAndAddsReward()
    {
        var player = MakePlayer(level: 20);
        player.Inventory.By(InventoryType.Cash).Put(new Item
        {
            ItemId = 5222000,
            Slot = 1,
            Quantity = 1,
            UniqueId = 77,
        });
        var reward = new CashItemDefinition(20300223, 5350000, 1, 0, 45, 2, -1, true);

        var result = player.OpenXmasSurpriseBox(77, 5222000, reward, FixedNow);

        Assert.True(result.Success);
        Assert.Null(player.Inventory.By(InventoryType.Cash).Get(1));
        Assert.NotNull(result.Reward);
        Assert.Equal(5350000, result.Reward!.ItemId);
        Assert.Equal(78, result.Reward.UniqueId);
    }

    private static Player MakePlayer(byte level)
        => new(
            new Character
            {
                Id = 1,
                Name = "BuffItems",
                Level = level,
                Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = 50, MaxMp = 50 },
            },
            new Position(0, 0, 0, 0));
}
