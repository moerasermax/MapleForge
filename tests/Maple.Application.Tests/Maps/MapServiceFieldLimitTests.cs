using Maple.Application.Maps;
using Maple.Core.Data;
using Maple.Core.Maps;

namespace Maple.Application.Tests.Maps;

/// <summary>
/// MapData.FieldLimit（P029）：對照 Java MapleMapFactory 讀 <c>info/fieldLimit</c>（預設 0）。
/// 用合成 IDataProvider（不依賴真實 WZ）鎖定讀取行為，跟 <see cref="MapServiceTests"/>（吃真 WZ）分開。
/// </summary>
public sealed class MapServiceFieldLimitTests
{
    [Fact]
    public void LoadMap_ReadsFieldLimitFromInfoNode()
    {
        var provider = new FakeMapDataProvider(fieldLimit: 0x40); // VipRock
        var service = new MapService(provider);

        var map = service.LoadMap(100000000);

        Assert.Equal(0x40, map.FieldLimit);
    }

    [Fact]
    public void LoadMap_WithoutFieldLimitNode_DefaultsToZero()
    {
        var provider = new FakeMapDataProvider(fieldLimit: null);
        var service = new MapService(provider);

        var map = service.LoadMap(100000000);

        Assert.Equal(0, map.FieldLimit);
    }

    [Fact]
    public void FieldLimitType_VipRock_ChecksBitCorrectly()
    {
        Assert.True(FieldLimitType.VipRock.Check(0x40));
        Assert.True(FieldLimitType.VipRock.Check(0x40 | 0x08)); // 與其他旗標並存
        Assert.False(FieldLimitType.VipRock.Check(0x08));
        Assert.False(FieldLimitType.VipRock.Check(0));
    }

    private sealed class FakeMapDataProvider : IDataProvider
    {
        private readonly IDataNode _mapImg;

        public FakeMapDataProvider(long? fieldLimit)
        {
            var infoChildren = new Dictionary<string, IDataNode>
            {
                ["returnMap"] = new Node("returnMap", 100000000),
                ["town"] = new Node("town", 1),
            };
            if (fieldLimit is { } value)
            {
                infoChildren["fieldLimit"] = new Node("fieldLimit", (int)value);
            }

            _mapImg = new Node("100000000.img", children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: infoChildren),
                ["portal"] = new Node("portal"),
                ["foothold"] = new Node("foothold"),
                ["life"] = new Node("life"),
            });
        }

        public IDataNode GetRoot(string fileName) => _mapImg;

        public IDataNode? GetAt(string fileName, string path) => _mapImg;
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
