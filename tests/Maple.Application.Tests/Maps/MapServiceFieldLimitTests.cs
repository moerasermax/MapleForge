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
    public void LoadMap_ReadsHpDecayFields_AndDefaultsIntervalTo10Seconds()
    {
        var withDecay = new MapService(new FakeMapDataProvider(fieldLimit: null, decHp: 20, decHpInterval: 5000, protectItem: 1072000))
            .LoadMap(100000000);
        var withoutDecay = new MapService(new FakeMapDataProvider(fieldLimit: null)).LoadMap(100000000);

        Assert.Equal((20, 5000, 1072000), (withDecay.DecHp, withDecay.DecHpInterval, withDecay.ProtectItem));
        Assert.Equal((0, 10_000, 0), (withoutDecay.DecHp, withoutDecay.DecHpInterval, withoutDecay.ProtectItem));
    }

    [Fact]
    public void InitializeFieldEnvironment_SetsHpDecayOnlyForDecHpMaps()
    {
        var now = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        var cold = new Maple.Core.World.FieldInstance(100000000);
        var normal = new Maple.Core.World.FieldInstance(100000000);

        new MapService(new FakeMapDataProvider(fieldLimit: null, decHp: 20)).InitializeFieldEnvironment(cold, now);
        new MapService(new FakeMapDataProvider(fieldLimit: null)).InitializeFieldEnvironment(normal, now);

        Assert.Equal(20, cold.HpDecay?.DecHp);
        Assert.Equal(now, cold.HpDecay?.LastHurtAt);
        Assert.Null(normal.HpDecay);
    }

    [Fact]
    public void LoadMap_ReadsEverlast_AndInitializeFieldEnvironmentCopiesIt()
    {
        // P100：對照 Java MapleMapFactory：setEverlast(info/everlast > 0)。
        var everlastService = new MapService(new FakeMapDataProvider(fieldLimit: null, everlast: 1));
        var field = new Maple.Core.World.FieldInstance(100000000);

        everlastService.InitializeFieldEnvironment(field, DateTimeOffset.UnixEpoch);

        Assert.True(everlastService.LoadMap(100000000).Everlast);
        Assert.True(field.Everlast);
        Assert.False(new MapService(new FakeMapDataProvider(fieldLimit: null)).LoadMap(100000000).Everlast);
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

        public FakeMapDataProvider(long? fieldLimit, int? decHp = null, int? decHpInterval = null, int? protectItem = null, int? everlast = null)
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

            if (decHp is { } dec) infoChildren["decHP"] = new Node("decHP", dec);
            if (decHpInterval is { } interval) infoChildren["decHPInterval"] = new Node("decHPInterval", interval);
            if (protectItem is { } protect) infoChildren["protectItem"] = new Node("protectItem", protect);
            if (everlast is { } ever) infoChildren["everlast"] = new Node("everlast", ever);

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
