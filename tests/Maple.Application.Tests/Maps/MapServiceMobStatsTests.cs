using Maple.Application.Maps;
using Maple.Core.Data;

namespace Maple.Application.Tests.Maps;

/// <summary>
/// P057：<see cref="MapService.LoadMobStats"/> 的 <c>Fly</c> 欄位解析（對照 Java
/// <c>MapleLifeFactory</c> 讀到頂層 "fly" 節點就 setFly(true)+setMobile(true)）。
/// 用合成 IDataProvider（不依賴真實 WZ）鎖定讀取行為，跟依賴真實 WZ 目錄的
/// <see cref="MapServiceTests"/> 分開。
/// </summary>
public sealed class MapServiceMobStatsTests
{
    [Fact]
    public void LoadMobStats_WithFlyNode_SetsFlyAndMobileTrue()
    {
        var provider = new FakeMobDataProvider(hasFlyNode: true, hasMoveNode: false);
        var service = new MapService(provider);

        var stats = service.LoadMobStats(100100);

        Assert.NotNull(stats);
        Assert.True(stats!.Fly);
        Assert.True(stats.Mobile); // 對照 Java：fly 節點同時觸發 setMobile(true)
    }

    [Fact]
    public void LoadMobStats_WithoutFlyNode_FlyDefaultsFalse()
    {
        var provider = new FakeMobDataProvider(hasFlyNode: false, hasMoveNode: true);
        var service = new MapService(provider);

        var stats = service.LoadMobStats(100100);

        Assert.NotNull(stats);
        Assert.False(stats!.Fly);
        Assert.True(stats.Mobile); // move 節點仍讓 Mobile 為 true，只是不飛
    }

    private sealed class FakeMobDataProvider : IDataProvider
    {
        private readonly IDataNode _mobImg;

        public FakeMobDataProvider(bool hasFlyNode, bool hasMoveNode)
        {
            var children = new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                {
                    ["maxHP"] = new Node("maxHP", 100),
                    ["maxMP"] = new Node("maxMP", 50),
                    ["level"] = new Node("level", 5),
                    ["exp"] = new Node("exp", 20),
                }),
            };

            if (hasFlyNode)
            {
                children["fly"] = new Node("fly");
            }

            if (hasMoveNode)
            {
                children["move"] = new Node("move");
            }

            _mobImg = new Node("0100100.img", children: children);
        }

        public IDataNode GetRoot(string fileName) => _mobImg;

        public IDataNode? GetAt(string fileName, string path) => fileName == "Mob" ? _mobImg : null;
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
