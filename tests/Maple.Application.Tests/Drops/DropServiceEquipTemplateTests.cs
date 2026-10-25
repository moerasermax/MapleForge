using Maple.Application.Drops;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Drops;

/// <summary>P102：怪物掉落的裝備帶資料檔預設數值（Java 此處另有 randomizeStats，留待 P103）。</summary>
public sealed class DropServiceEquipTemplateTests
{
    private const int Axe = 1312004;

    [Fact]
    public void OnMobKilled_EquipDrop_UsesTemplateStats()
    {
        var service = MakeService(new FakeTemplates());

        var equip = Assert.IsType<Equip>(KillAndGetDropItem(service));

        Assert.Equal(Axe, equip.ItemId);
        Assert.Equal((short)17, equip.Watk);
        Assert.Equal((byte)7, equip.UpgradeSlots);
    }

    [Fact]
    public void OnMobKilled_EquipDrop_WithoutTemplates_IsBlankEquip()
    {
        var service = MakeService(null);

        var equip = Assert.IsType<Equip>(KillAndGetDropItem(service));

        Assert.Equal(Axe, equip.ItemId);
        Assert.Equal((short)0, equip.Watk);
    }

    private static Item? KillAndGetDropItem(DropService service)
    {
        var field = new FieldInstance(100000100);
        var player = new Player(
            new Character { Id = 1, Name = "Tester", Stats = new CharacterStats { Hp = 50, MaxHp = 50, Mp = 5, MaxMp = 5 } },
            new Position(0, 0, 0, 0));
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 30, Y = 40, Fh = 7 },
            new MobStats(100100, 20, MaxMp: 10, Level: 1, Exp: 1),
            objectId: 100001);
        field.Add(player);
        field.Add(mob);

        var rewards = service.OnMobKilled(field, player, mob);
        return rewards.SpawnedDrops.First(d => !d.IsMeso).Item;
    }

    private static DropService MakeService(IEquipTemplateProvider? templates)
    {
        var catalog = new InMemoryMonsterDropCatalog(new Dictionary<int, IReadOnlyList<MonsterDropEntry>>
        {
            [100100] = [new MonsterDropEntry(Axe, 999_999, 1, 1)],
        });
        return new DropService(catalog, equipTemplates: templates);
    }

    private sealed class FakeTemplates : IEquipTemplateProvider
    {
        public Equip Create(int itemId)
            => new() { ItemId = itemId, Quantity = 1, Watk = 17, UpgradeSlots = 7 };
    }
}
