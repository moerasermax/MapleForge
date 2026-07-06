using Maple.Application.Items;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Items;

public sealed class ItemMakerServiceTests
{
    [Fact]
    public void CreateGem_ConsumesMaterialsMesoAndGrantsWeightedReward()
    {
        var catalog = new FakeItemMakeCatalog();
        catalog.Gems[4250000] = new ItemMakeGemRecipe(
            4250000,
            Cost: 500,
            RequiredLevel: 0,
            RequiredMakerLevel: 1,
            RewardQuantity: 1,
            Ingredients: new[] { new ItemMakeIngredient(4000000, 2) },
            RandomRewards: new[] { new ItemMakeRandomReward(4250001, 1), new ItemMakeRandomReward(4250002, 1) });
        var player = MakePlayer(meso: 1000, skills: new CharacterSkillRecord { SkillId = 1007, Level = 1, MasterLevel = 3 });
        Put(player, InventoryType.Etc, 1, 4000000, 2);
        var service = new ItemMakerService(catalog, new SequenceRandom(ints: new[] { 1 }));

        var result = service.Handle(player, ItemMakerRequest.CreateItem(4250000, false, Array.Empty<int>()));

        Assert.Equal(ItemMakerStatus.Success, result.Status);
        Assert.True(result.CharacterMutated);
        Assert.Equal(500, player.Character.Meso);
        Assert.Equal(0, player.Inventory.By(InventoryType.Etc).CountById(4000000));
        Assert.Equal(1, player.Inventory.By(InventoryType.Etc).CountById(4250002));
        Assert.Equal(2, result.Mutations[0].OldQuantity);
        Assert.Equal(0, result.Mutations[0].NewQuantity);
    }

    [Fact]
    public void CreateGem_RejectsMissingMakerSkillWithoutMutation()
    {
        var catalog = new FakeItemMakeCatalog();
        catalog.Gems[4250000] = new ItemMakeGemRecipe(
            4250000,
            500,
            0,
            RequiredMakerLevel: 2,
            1,
            new[] { new ItemMakeIngredient(4000000, 1) },
            new[] { new ItemMakeRandomReward(4250001, 1) });
        var player = MakePlayer(meso: 1000, skills: new CharacterSkillRecord { SkillId = 1007, Level = 1, MasterLevel = 3 });
        Put(player, InventoryType.Etc, 1, 4000000, 1);
        var service = new ItemMakerService(catalog, new SequenceRandom());

        var result = service.Handle(player, ItemMakerRequest.CreateItem(4250000, false, Array.Empty<int>()));

        Assert.Equal(ItemMakerStatus.SkillLevelTooLow, result.Status);
        Assert.Equal(1000, player.Character.Meso);
        Assert.Equal(1, player.Inventory.By(InventoryType.Etc).CountById(4000000));
    }

    [Fact]
    public void CreateEquip_AppliesStimulatorAndEnchanterWithDeterministicRandom()
    {
        var catalog = new FakeItemMakeCatalog();
        catalog.Creates[1302000] = new ItemMakeCreateRecipe(
            1302000,
            Cost: 100,
            RequiredLevel: 0,
            RequiredMakerLevel: 1,
            RewardQuantity: 1,
            UpgradeSlots: 2,
            StimulatorItemId: 4130000,
            Ingredients: new[] { new ItemMakeIngredient(4000001, 1) });
        catalog.Equips[1302000] = new Equip { ItemId = 1302000, Quantity = 1, Watk = 10, Str = 10, UpgradeSlots = 7 };
        catalog.EnhanceStats[4250000] = new ItemMakeEnhanceStats(
            Watk: 2,
            Matk: 0,
            Acc: 0,
            Avoid: 0,
            Speed: 0,
            Jump: 0,
            Hp: 0,
            Mp: 0,
            Str: 0,
            Dex: 0,
            Int: 0,
            Luk: 0,
            RandomOption: 0,
            RandomStat: 0);
        var player = MakePlayer(meso: 1000, skills: new CharacterSkillRecord { SkillId = 1007, Level = 1, MasterLevel = 3 });
        Put(player, InventoryType.Etc, 1, 4000001, 1);
        Put(player, InventoryType.Etc, 2, 4130000, 1);
        Put(player, InventoryType.Etc, 3, 4250000, 1);
        var service = new ItemMakerService(catalog, new SequenceRandom(ints: new[] { 2, 2 }));

        var result = service.Handle(player, ItemMakerRequest.CreateItem(1302000, true, new[] { 4250000 }));

        Assert.Equal(ItemMakerStatus.Success, result.Status);
        Assert.Equal(900, player.Character.Meso);
        Assert.Equal(3, result.Mutations.Count);
        var equip = Assert.IsType<Equip>(result.CreatedItem);
        Assert.Equal(11, equip.Str);  // stimulator rolls +1.
        Assert.Equal(13, equip.Watk); // stimulator rolls +1, incPAD +2.
        Assert.Equal(0, player.Inventory.By(InventoryType.Etc).CountById(4130000));
        Assert.Equal(0, player.Inventory.By(InventoryType.Etc).CountById(4250000));
    }

    [Fact]
    public void CreateCrystal_ConsumesHundredEtcAndCreatesCrystalByItemMakeLevel()
    {
        var catalog = new FakeItemMakeCatalog();
        catalog.ItemMakeLevels[4000000] = 65;
        var player = MakePlayer();
        Put(player, InventoryType.Etc, 1, 4000000, 100);
        var service = new ItemMakerService(catalog, new SequenceRandom());

        var result = service.Handle(player, ItemMakerRequest.CreateCrystal(4000000));

        Assert.Equal(ItemMakerStatus.Success, result.Status);
        Assert.Equal(0, player.Inventory.By(InventoryType.Etc).CountById(4000000));
        Assert.Equal(1, player.Inventory.By(InventoryType.Etc).CountById(4260002));
    }

    [Fact]
    public void DisassembleEquip_ConsumesEquipAndCreatesCrystalByRequiredLevel()
    {
        var catalog = new FakeItemMakeCatalog();
        catalog.RequiredLevels[1302000] = 80;
        var player = MakePlayer();
        Put(player, InventoryType.Equip, 1, 1302000, 1);
        var service = new ItemMakerService(catalog, new SequenceRandom(inclusive: new[] { 6 }));

        var result = service.Handle(player, ItemMakerRequest.DisassembleEquip(1302000, tick: 123, slot: 1));

        Assert.Equal(ItemMakerStatus.Success, result.Status);
        Assert.Equal(0, player.Inventory.By(InventoryType.Equip).CountById(1302000));
        Assert.Equal(6, player.Inventory.By(InventoryType.Etc).CountById(4260003));
    }

    private static Player MakePlayer(int meso = 0, params CharacterSkillRecord[] skills)
        => new(
            new Character
            {
                Id = 77,
                Name = "Maker",
                Meso = meso,
                Skills = skills.ToList(),
            },
            new Position(0, 0, 0, 0));

    private static void Put(Player player, InventoryType type, short slot, int itemId, short quantity)
    {
        var item = type == InventoryType.Equip
            ? new Equip { ItemId = itemId, Slot = slot, Quantity = 1 }
            : new Item { ItemId = itemId, Slot = slot, Quantity = quantity };
        player.Inventory.By(type).Put(item);
        player.FlushInventory();
    }

    private sealed class SequenceRandom : IItemMakerRandomSource
    {
        private readonly Queue<int> _ints;
        private readonly Queue<int> _inclusive;
        private readonly Queue<bool> _bools;

        public SequenceRandom(
            IReadOnlyList<int>? ints = null,
            IReadOnlyList<int>? inclusive = null,
            IReadOnlyList<bool>? bools = null)
        {
            _ints = new Queue<int>(ints ?? Array.Empty<int>());
            _inclusive = new Queue<int>(inclusive ?? Array.Empty<int>());
            _bools = new Queue<bool>(bools ?? Array.Empty<bool>());
        }

        public int NextInt(int exclusiveMax)
        {
            if (_ints.Count == 0)
            {
                return 0;
            }

            var value = _ints.Dequeue();
            return Math.Clamp(value, 0, Math.Max(0, exclusiveMax - 1));
        }

        public int NextInclusive(int minInclusive, int maxInclusive)
            => _inclusive.Count == 0 ? minInclusive : _inclusive.Dequeue();

        public bool NextBool() => _bools.Count == 0 || _bools.Dequeue();
    }

    private sealed class FakeItemMakeCatalog : IItemMakeCatalog
    {
        public Dictionary<int, ItemMakeGemRecipe> Gems { get; } = new();
        public Dictionary<int, ItemMakeCreateRecipe> Creates { get; } = new();
        public Dictionary<int, int> ItemMakeLevels { get; } = new();
        public Dictionary<int, int> RequiredLevels { get; } = new();
        public Dictionary<int, Equip> Equips { get; } = new();
        public Dictionary<int, ItemMakeEnhanceStats> EnhanceStats { get; } = new();

        public ItemMakeGemRecipe? GetGemRecipe(int itemId) => Gems.GetValueOrDefault(itemId);
        public ItemMakeCreateRecipe? GetCreateRecipe(int itemId) => Creates.GetValueOrDefault(itemId);
        public int GetItemMakeLevel(int itemId) => ItemMakeLevels.GetValueOrDefault(itemId);
        public int GetRequiredLevel(int itemId) => RequiredLevels.GetValueOrDefault(itemId);
        public bool IsDropRestricted(int itemId) => false;
        public bool IsAccountShared(int itemId) => false;
        public Equip? CreateEquip(int itemId) => Equips.GetValueOrDefault(itemId)?.Copy() as Equip;
        public ItemMakeEnhanceStats? GetEnhanceStats(int itemId) => EnhanceStats.GetValueOrDefault(itemId);
        public int GemRecipeCount => Gems.Count;
        public int CreateRecipeCount => Creates.Count;
    }
}
