using Maple.Core.Inventory;
using Maple.Core.Items;

namespace Maple.Core.Tests.Items;

/// <summary>P103：對照 Java <c>randomizeStats</c>／<c>getRandStat</c> 的掉落裝備數值浮動。</summary>
public sealed class EquipStatRandomizerTests
{
    [Fact]
    public void Randomize_ZeroStats_StayZero()
    {
        var equip = EquipStatRandomizer.Randomize(new Equip { ItemId = 1312004 }, new FixedRandom(0.99));

        Assert.Equal((short)0, equip.Str);
        Assert.Equal((short)0, equip.Watk);
        Assert.Equal((short)0, equip.Hp);
    }

    [Theory]
    [InlineData(0.0, (short)15)]   // 17 - ceil(1.7)=2
    [InlineData(0.5, (short)17)]   // floor(0.5*5)=2 → 15+2
    [InlineData(0.999, (short)19)] // floor(0.999*5)=4 → 15+4
    public void Randomize_VariesWithinCeilTenPercent(double roll, short expected)
    {
        var equip = EquipStatRandomizer.Randomize(new Equip { Watk = 17 }, new FixedRandom(roll));

        Assert.Equal(expected, equip.Watk);
    }

    [Fact]
    public void Randomize_MinorStats_CappedAtFive()
    {
        var equip = EquipStatRandomizer.Randomize(new Equip { Watk = 100, Str = 100 }, new FixedRandom(0.0));

        Assert.Equal((short)95, equip.Watk);
        Assert.Equal((short)95, equip.Str);
    }

    [Fact]
    public void Randomize_DefenseAndHpMp_CappedAtTen()
    {
        var equip = EquipStatRandomizer.Randomize(
            new Equip { Wdef = 200, Mdef = 200, Hp = 200, Mp = 200 },
            new FixedRandom(0.0));

        Assert.Equal((short)190, equip.Wdef);
        Assert.Equal((short)190, equip.Mdef);
        Assert.Equal((short)190, equip.Hp);
        Assert.Equal((short)190, equip.Mp);
    }

    [Fact]
    public void Randomize_LeavesNonStatFieldsUntouched()
    {
        var equip = EquipStatRandomizer.Randomize(
            new Equip { ItemId = 1312004, UpgradeSlots = 7, Watk = 17 },
            new FixedRandom(0.0));

        Assert.Equal(1312004, equip.ItemId);
        Assert.Equal((byte)7, equip.UpgradeSlots);
    }

    [Fact]
    public void Randomize_SmallStat_VariesByOne()
    {
        var low = EquipStatRandomizer.Randomize(new Equip { Speed = 3 }, new FixedRandom(0.0));
        var high = EquipStatRandomizer.Randomize(new Equip { Speed = 3 }, new FixedRandom(0.999));

        Assert.Equal((short)2, low.Speed);
        Assert.Equal((short)4, high.Speed);
    }

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }
}
