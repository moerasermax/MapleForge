using Maple.Content.Items;
using Maple.Content.Wz;
using Maple.Core.Data;
using Maple.Core.Inventory;

namespace Maple.Content.Tests.Items;

public sealed class WzItemMakeCatalogTests
{
    private const string WzDir = @"E:\WorkSpace_離線資料\02_遊戲素材_game-assets\MapleStory\v113_Client";

    [Fact]
    public void GetRecipes_ParsesGemAndCreateEntries()
    {
        var provider = new FakeDataProvider();
        provider.AddAt("Etc", "ItemMake.img", Node.Image("ItemMake.img")
            .With("0", Node.Image("0")
                .With("4250000", Node.Image("4250000")
                    .WithValue("reqLevel", 45)
                    .WithValue("reqSkillLevel", 1)
                    .WithValue("meso", 5000)
                    .WithValue("itemNum", 1)
                    .With("recipe", Node.Image("recipe")
                        .With("0", Node.Image("0").WithValue("item", 4000000).WithValue("count", 10)))
                    .With("randomReward", Node.Image("randomReward")
                        .With("0", Node.Image("0").WithValue("item", 4250001).WithValue("prob", 7)))))
            .With("1", Node.Image("1")
                .With("1302000", Node.Image("1302000")
                    .WithValue("reqLevel", 35)
                    .WithValue("reqSkillLevel", 2)
                    .WithValue("meso", 12000)
                    .WithValue("itemNum", 1)
                    .WithValue("tuc", 2)
                    .WithValue("catalyst", 4130000)
                    .With("recipe", Node.Image("recipe")
                        .With("0", Node.Image("0").WithValue("item", 4000001).WithValue("count", 3))))));

        var catalog = new WzItemMakeCatalog(provider);

        var gem = catalog.GetGemRecipe(4250000);
        Assert.NotNull(gem);
        Assert.Equal(5000, gem!.Cost);
        Assert.Equal(1, gem.RequiredMakerLevel);
        Assert.Equal(new[] { new { ItemId = 4000000, Count = 10 } },
            gem.Ingredients.Select(i => new { i.ItemId, i.Count }));
        Assert.Equal(4250001, Assert.Single(gem.RandomRewards).ItemId);

        var create = catalog.GetCreateRecipe(1302000);
        Assert.NotNull(create);
        Assert.Equal(2, create!.RequiredMakerLevel);
        Assert.Equal(2, create.UpgradeSlots);
        Assert.Equal(4130000, create.StimulatorItemId);
    }

    [Fact]
    public void ItemMetadata_ReadsEquipTemplateEnhanceStatsAndFlags()
    {
        var provider = new FakeDataProvider();
        provider.AddRoot("Character", Node.Image("Character")
            .With("Weapon", Node.Image("Weapon")
                .With("01302000.img", Node.Image("01302000.img")
                    .With("info", Node.Image("info")
                        .WithValue("incSTR", 3)
                        .WithValue("incPAD", 22)
                        .WithValue("tuc", 7)
                        .WithValue("reqLevel", 30)
                        .WithValue("flag", 0x100)))));
        provider.AddRoot("Item", Node.Image("Item")
            .With("Etc", Node.Image("Etc")
                .With("0425.img", Node.Image("0425.img")
                    .With("04250000", Node.Image("04250000")
                        .With("info", Node.Image("info")
                            .WithValue("incPAD", 2)
                            .WithValue("randStat", 1))))
                .With("0400.img", Node.Image("0400.img")
                    .With("04000000", Node.Image("04000000")
                        .With("info", Node.Image("info")
                            .WithValue("itemMakeLevel", 65)
                            .WithValue("flag", 0x200))))));

        var catalog = new WzItemMakeCatalog(provider);

        var equip = catalog.CreateEquip(1302000);
        Assert.NotNull(equip);
        Assert.Equal(InventoryType.Equip, PlayerInventoryTypeOf(equip!.ItemId));
        Assert.Equal(3, equip.Str);
        Assert.Equal(22, equip.Watk);
        Assert.Equal(7, equip.UpgradeSlots);
        Assert.Equal(30, catalog.GetRequiredLevel(1302000));
        Assert.True(catalog.IsAccountShared(1302000));

        var enhance = catalog.GetEnhanceStats(4250000);
        Assert.NotNull(enhance);
        Assert.Equal(2, enhance!.Watk);
        Assert.Equal(1, enhance.RandomStat);
        Assert.Equal(65, catalog.GetItemMakeLevel(4000000));
        Assert.True(catalog.IsDropRestricted(4000000));
    }

    [Fact]
    public void RealItemMakeWz_LoadsRecipes()
    {
        using var provider = new WzDataProvider(WzDir);
        var catalog = new WzItemMakeCatalog(provider);

        Assert.True(catalog.GemRecipeCount > 0);
        Assert.True(catalog.CreateRecipeCount > 0);
    }

    private static InventoryType PlayerInventoryTypeOf(int itemId)
    {
        var cat = itemId / 1_000_000;
        return cat is >= 1 and <= 5 ? (InventoryType)cat : InventoryType.Etc;
    }

    private sealed class FakeDataProvider : IDataProvider
    {
        private readonly Dictionary<string, IDataNode> _roots = new(StringComparer.Ordinal);
        private readonly Dictionary<(string File, string Path), IDataNode> _nodes = new();

        public void AddRoot(string fileName, IDataNode node) => _roots[fileName] = node;

        public void AddAt(string fileName, string path, IDataNode node) => _nodes[(fileName, path)] = node;

        public IDataNode GetRoot(string fileName) => _roots.GetValueOrDefault(fileName) ?? Node.Image(fileName);

        public IDataNode? GetAt(string fileName, string path) => _nodes.GetValueOrDefault((fileName, path));
    }

    private sealed class Node : IDataNode
    {
        private readonly Dictionary<string, IDataNode> _children = new(StringComparer.Ordinal);

        private Node(string name, object? value = null)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, IDataNode> Children => _children;

        public object? Value { get; }

        public IDataNode? this[string name] => _children.GetValueOrDefault(name);

        public static Node Image(string name) => new(name);

        public Node With(string name, Node child)
        {
            _children[name] = child;
            return this;
        }

        public Node WithValue(string name, object value)
        {
            _children[name] = new Node(name, value);
            return this;
        }
    }
}
