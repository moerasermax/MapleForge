using Maple.Core.CashShop;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.World;

namespace Maple.Core.Tests.Items;

/// <summary>P102：對照 Java getEquipById，Player 產生裝備時帶資料檔預設數值。</summary>
public sealed class PlayerEquipTemplateTests
{
    private const int Axe = 1312004;

    [Fact]
    public void GainItem_Equip_UsesTemplateStats()
    {
        var player = NewPlayer(new FakeTemplates());

        var item = player.GainItem(InventoryType.Equip, Axe);

        var equip = Assert.IsType<Equip>(item);
        Assert.Equal((short)17, equip.Watk);
        Assert.Equal((byte)7, equip.UpgradeSlots);
        Assert.Equal((short)1, equip.Quantity);
    }

    [Fact]
    public void GainItem_Equip_WithoutTemplates_FallsBackToBlankEquip()
    {
        var player = NewPlayer(null);

        var equip = Assert.IsType<Equip>(player.GainItem(InventoryType.Equip, Axe));

        Assert.Equal(Axe, equip.ItemId);
        Assert.Equal((short)0, equip.Watk);
        Assert.Equal((byte)0, equip.UpgradeSlots);
    }

    [Fact]
    public void GainItem_TwoEquips_AreIndependentInstances()
    {
        var player = NewPlayer(new FakeTemplates());

        var first = Assert.IsType<Equip>(player.GainItem(InventoryType.Equip, Axe));
        var second = Assert.IsType<Equip>(player.GainItem(InventoryType.Equip, Axe));
        first.Watk = 99;

        Assert.NotSame(first, second);
        Assert.Equal((short)17, second.Watk);
    }

    [Fact]
    public void GainItem_NonEquip_DoesNotConsultTemplates()
    {
        var templates = new FakeTemplates();
        var player = NewPlayer(templates);

        player.GainItem(InventoryType.Use, 2000000, 5);

        Assert.Empty(templates.Requested);
    }

    [Fact]
    public void GainCashShopItem_Equip_KeepsTemplateStatsAndStampsExpirationAndUniqueId()
    {
        var player = NewPlayer(new FakeTemplates());
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000L);
        var cashItem = new CashItemDefinition(10100004, Axe, 10, 199, 30, 2, -1, true);

        var equip = Assert.IsType<Equip>(player.GainCashShopItem(cashItem, now));

        Assert.Equal((short)17, equip.Watk);
        Assert.Equal(now.AddDays(30).ToUnixTimeMilliseconds(), equip.Expiration);
        Assert.Equal(1, equip.UniqueId);
    }

    private static Player NewPlayer(IEquipTemplateProvider? templates)
        => new(new Character { Id = 1, Name = "EquipTemplate", Gender = 0 }, new Position(0, 0, 0, 0), templates);

    private sealed class FakeTemplates : IEquipTemplateProvider
    {
        public List<int> Requested { get; } = new();

        public Equip Create(int itemId)
        {
            Requested.Add(itemId);
            return new Equip { ItemId = itemId, Quantity = 1, Watk = 17, UpgradeSlots = 7 };
        }
    }
}
