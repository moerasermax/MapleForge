using Maple.Application.Text;
using Maple.Core.Data;

namespace Maple.Application.Tests.Text;

/// <summary>P193：String.wz 道具名與地圖名。</summary>
public sealed class WzStringCatalogTests
{
    [Fact]
    public void ItemAndMapNames_ByCategory()
    {
        var catalog = new WzStringCatalog(new Provider());

        Assert.Equal("紅色藥水", catalog.GetItemName(2000000));
        Assert.Equal("藍寶殼", catalog.GetItemName(4000000));
        Assert.Equal("劍", catalog.GetItemName(1302000));
        Assert.Null(catalog.GetItemName(2000001));
        Assert.Equal(("維多利亞", "弓箭手村"), catalog.GetMapName(100000000));
        Assert.Null(catalog.GetMapName(1));
    }

    private sealed class Provider : IDataProvider
    {
        private readonly Dictionary<string, IDataNode> _nodes = new()
        {
            ["Consume.img/2000000/name"] = new Node("name", "紅色藥水"),
            ["Etc.img/Etc/4000000/name"] = new Node("name", "藍寶殼"),
            ["Eqp.img/Eqp"] = new Node("Eqp", children: [new Node("Weapon", children: [new Node("1302000", children: [new Node("name", "劍")])])]),
            ["Map.img"] = new Node("Map.img", children: [new Node("victoria", children: [new Node("100000000", children: [new Node("streetName", "維多利亞"), new Node("mapName", "弓箭手村")])])]),
        };

        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => fileName == "String" ? _nodes.GetValueOrDefault(path) : null;
    }

    private sealed class Node(string name, object? value = null, Node[]? children = null) : IDataNode
    {
        public string Name { get; } = name;

        public object? Value { get; } = value;

        public IReadOnlyDictionary<string, IDataNode> Children { get; } = (children ?? []).ToDictionary(static c => c.Name, static c => (IDataNode)c);

        public IDataNode? this[string key] => Children.GetValueOrDefault(key);
    }
}
