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

            _itemRoot = new Node("Item", children: new Dictionary<string, IDataNode>
            {
                ["Consume"] = new Node("Consume", children: new Dictionary<string, IDataNode>
                {
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
