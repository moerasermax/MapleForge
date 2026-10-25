using Maple.Application.Items;
using Maple.Core.Data;

namespace Maple.Application.Tests.Items;

public sealed class WzItemUseCatalogTests
{
    [Fact]
    public void GetReturnScrollDestinationMapId_ReadsSpecMoveTo()
    {
        var catalog = new WzItemUseCatalog(new ItemDataProvider());

        Assert.Equal(100000001, catalog.GetReturnScrollDestinationMapId(2030000));
    }

    [Fact]
    public void GetSummonBagMobs_ReadsMobEntriesInWzOrder()
    {
        var catalog = new WzItemUseCatalog(new ItemDataProvider());

        var entries = catalog.GetSummonBagMobs(2100000);

        Assert.Collection(
            entries!,
            entry =>
            {
                Assert.Equal(100100, entry.MobId);
                Assert.Equal(50, entry.Probability);
            },
            entry =>
            {
                Assert.Equal(100101, entry.MobId);
                Assert.Equal(10, entry.Probability);
            });
    }

    [Fact]
    public void GetEffect_ReadsSpecHpMpAndRates()
    {
        var catalog = new WzItemUseCatalog(new ItemDataProvider());

        var effect = catalog.GetEffect(2000019)!;

        Assert.Equal(2000019, effect.ItemId);
        Assert.Equal(400, effect.Hp);
        Assert.Equal(0, effect.Mp);
        Assert.Equal(0, effect.HpRate);
        Assert.Equal(30, effect.MpRate);
        Assert.Equal(new[] { Maple.Core.World.PlayerDisease.Poison, Maple.Core.World.PlayerDisease.Curse }, effect.Cures);
    }

    [Fact]
    public void GetBuffEffect_ReadsTimedStatupsInJavaOrder()
    {
        // P164：loadFromData：time > -1 → overTime；statups WATK、SPEED、MESO_RATE(×200)；SourceId = -itemId。
        var catalog = new WzItemUseCatalog(new ItemDataProvider());

        var effect = catalog.GetBuffEffect(2022109)!;

        Assert.Equal(-2022109, effect.SourceId);
        Assert.False(effect.IsSkill);
        Assert.Equal(180000, effect.DurationMilliseconds);
        Assert.Equal(
            [
                new Maple.Core.Skills.BuffStatValue(Maple.Core.Skills.MapleBuffStat.WATK, 5),
                new Maple.Core.Skills.BuffStatValue(Maple.Core.Skills.MapleBuffStat.SPEED, 10),
                new Maple.Core.Skills.BuffStatValue(Maple.Core.Skills.MapleBuffStat.MESO_RATE, 200),
            ],
            effect.Statups);
        Assert.Null(catalog.GetBuffEffect(2000019)); // 無 time
    }

    [Fact]
    public void GetEffect_UnknownItem_ReturnsNull()
    {
        Assert.Null(new WzItemUseCatalog(new ItemDataProvider()).GetEffect(2000999));
    }

    [Fact]
    public void GetEffect_ItemWithoutSpec_ReturnsZeroEffect()
    {
        var effect = new WzItemUseCatalog(new ItemDataProvider()).GetEffect(2100000)!;

        Assert.Equal(0, effect.Hp + effect.Mp + effect.HpRate + effect.MpRate);
    }

    [Fact]
    public void GetScroll_ReadsSuccessCurseAndIncStats()
    {
        var scroll = new WzItemUseCatalog(new ItemDataProvider()).GetScroll(2044002)!;

        Assert.Equal(10, scroll.SuccessRate);
        Assert.Equal(100, scroll.CurseRate);
        Assert.Equal((short)5, scroll.Watk);
        Assert.Equal((short)3, scroll.Str);
        Assert.Equal((short)1, scroll.Acc);
    }

    [Fact]
    public void GetScroll_NonScrollItem_ReturnsNull()
    {
        Assert.Null(new WzItemUseCatalog(new ItemDataProvider()).GetScroll(2000019));
    }

    [Fact]
    public void GetRewardItems_ReadsRewardEntries_TotalProb_AndPeriodFloor()
    {
        var table = new WzItemUseCatalog(new ItemDataProvider()).GetRewardItems(2022336)!;

        Assert.Equal(11, table.TotalProb);
        Assert.Equal(
            [
                new Maple.Core.Items.RewardItemEntry(4001126, 1, 500, 0, "Effect/BasicEff/FindPrize/Success", null),
                new Maple.Core.Items.RewardItemEntry(1122017, 10, 1, 7200, string.Empty, "lucky"),
            ],
            table.Entries);
        Assert.Null(new WzItemUseCatalog(new ItemDataProvider()).GetRewardItems(2000019));
    }

    private sealed class ItemDataProvider : IDataProvider
    {
        private readonly IDataNode _itemRoot;

        public ItemDataProvider()
        {
            var returnScroll = new Node("02030000", children: new Dictionary<string, IDataNode>
            {
                ["spec"] = new Node("spec", children: new Dictionary<string, IDataNode>
                {
                    ["moveTo"] = new Node("moveTo", 100000001),
                }),
            });

            var summonBag = new Node("02100000", children: new Dictionary<string, IDataNode>
            {
                ["mob"] = new Node("mob", children: new Dictionary<string, IDataNode>
                {
                    ["0"] = new Node("0", children: new Dictionary<string, IDataNode>
                    {
                        ["id"] = new Node("id", 100100),
                        ["prob"] = new Node("prob", 50),
                    }),
                    ["1"] = new Node("1", children: new Dictionary<string, IDataNode>
                    {
                        ["id"] = new Node("id", 100101),
                        ["prob"] = new Node("prob", 10),
                    }),
                }),
            });

            var potion = new Node("02000019", children: new Dictionary<string, IDataNode>
            {
                ["spec"] = new Node("spec", children: new Dictionary<string, IDataNode>
                {
                    ["hp"] = new Node("hp", 400),
                    ["mpR"] = new Node("mpR", 30),
                    ["poison"] = new Node("poison", 1),
                    ["curse"] = new Node("curse", 1),
                }),
            });

            var attackPotion = new Node("02022109", children: new Dictionary<string, IDataNode>
            {
                ["spec"] = new Node("spec", children: new Dictionary<string, IDataNode>
                {
                    ["pad"] = new Node("pad", 5),
                    ["speed"] = new Node("speed", 10),
                    ["mesoupbyitem"] = new Node("mesoupbyitem", 1),
                    ["time"] = new Node("time", 180000),
                }),
            });

            var rewardBox = new Node("02022336", children: new Dictionary<string, IDataNode>
            {
                ["reward"] = new Node("reward", children: new Dictionary<string, IDataNode>
                {
                    ["0"] = new Node("0", children: new Dictionary<string, IDataNode>
                    {
                        ["item"] = new Node("item", 4001126),
                        ["prob"] = new Node("prob", 1),
                        ["count"] = new Node("count", 500),
                        ["Effect"] = new Node("Effect", "Effect/BasicEff/FindPrize/Success"),
                    }),
                    ["1"] = new Node("1", children: new Dictionary<string, IDataNode>
                    {
                        ["item"] = new Node("item", 1122017),
                        ["prob"] = new Node("prob", 10),
                        ["count"] = new Node("count", 1),
                        ["period"] = new Node("period", 60),
                        ["worldMsg"] = new Node("worldMsg", "lucky"),
                    }),
                }),
            });

            _itemRoot = new Node("Item", children: new Dictionary<string, IDataNode>
            {
                ["Consume"] = new Node("Consume", children: new Dictionary<string, IDataNode>
                {
                    ["0200.img"] = new Node("0200.img", children: new Dictionary<string, IDataNode>
                    {
                        ["02000019"] = potion,
                    }),
                    ["0202.img"] = new Node("0202.img", children: new Dictionary<string, IDataNode>
                    {
                        ["02022109"] = attackPotion,
                        ["02022336"] = rewardBox,
                    }),
                    ["0204.img"] = new Node("0204.img", children: new Dictionary<string, IDataNode>
                    {
                        ["02044002"] = new Node("02044002", children: new Dictionary<string, IDataNode>
                        {
                            ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                            {
                                ["success"] = new Node("success", 10),
                                ["cursed"] = new Node("cursed", 100),
                                ["incPAD"] = new Node("incPAD", 5),
                                ["incSTR"] = new Node("incSTR", 3),
                                ["incACC"] = new Node("incACC", 1),
                            }),
                        }),
                    }),
                    ["0203.img"] = new Node("0203.img", children: new Dictionary<string, IDataNode>
                    {
                        ["02030000"] = returnScroll,
                    }),
                    ["0210.img"] = new Node("0210.img", children: new Dictionary<string, IDataNode>
                    {
                        ["02100000"] = summonBag,
                    }),
                }),
            });
        }

        public IDataNode GetRoot(string fileName) => fileName == "Item" ? _itemRoot : new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => null;
    }

    private sealed class Node : IDataNode
    {
        public Node(string name, object? value = null, IReadOnlyDictionary<string, IDataNode>? children = null)
        {
            Name = name;
            Value = value;
            Children = children ?? new Dictionary<string, IDataNode>();
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, IDataNode> Children { get; }

        public object? Value { get; }

        public IDataNode? this[string name] => Children.TryGetValue(name, out var child) ? child : null;
    }
}
