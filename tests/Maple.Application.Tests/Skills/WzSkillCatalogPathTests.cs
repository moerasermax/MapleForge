using Maple.Application.Skills;
using Maple.Core.Data;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

/// <summary>P118：WZ 技能節點 7 位數補零路徑（初心者技能 0001004）與 elemAttr。不需真實 WZ。</summary>
public sealed class WzSkillCatalogPathTests
{
    [Fact]
    public void GetSkill_BeginnerSkill_UsesSevenDigitNodeName()
    {
        var provider = new FakeProvider(("000.img/skill/0001004", SkillNode("0001004", elemAttr: null)));

        var skill = new WzSkillCatalog(provider).GetSkill(1004);

        Assert.NotNull(skill);
        Assert.Equal(1004, skill!.Id);
        Assert.Single(skill.Effects);
    }

    [Fact]
    public void GetSkill_ElemAttr_SetsSkillAndEffectElement()
    {
        var provider = new FakeProvider(("220.img/skill/2201004", SkillNode("2201004", elemAttr: "I")));

        var skill = new WzSkillCatalog(provider).GetSkill(2201004)!;

        Assert.Equal(Element.Ice, skill.Element);
        Assert.Equal(Element.Ice, skill.Effects[0].SourceElement);
        Assert.Equal(1, skill.Effects[0].MonsterStatuses[MonsterStatus.Freeze]);
    }

    [Fact]
    public void GetSkill_LtRb_ParsedIntoEffectArea()
    {
        var provider = new FakeProvider(("211.img/skill/2111003", SkillNode("2111003", elemAttr: null, withArea: true)));

        var effect = new WzSkillCatalog(provider).GetSkill(2111003)!.Effects[0];

        Assert.Equal(new MobSkillArea(-200, -150, 200, 150), effect.Area);
    }

    [Fact]
    public void GetSkill_NoLt_AreaIsNull()
    {
        var provider = new FakeProvider(("220.img/skill/2201004", SkillNode("2201004", elemAttr: null)));

        Assert.Null(new WzSkillCatalog(provider).GetSkill(2201004)!.Effects[0].Area);
    }

    private static Node SkillNode(string name, string? elemAttr, bool withArea = false)
    {
        var levelChildren = new Dictionary<string, IDataNode>
        {
            ["time"] = new Node("time", 10),
            ["x"] = new Node("x", 5),
        };
        if (withArea)
        {
            levelChildren["lt"] = new Node("lt", new DataVector(-200, -150));
            levelChildren["rb"] = new Node("rb", new DataVector(200, 150));
        }

        var level1 = new Node("1", children: levelChildren);
        var children = new Dictionary<string, IDataNode>
        {
            ["level"] = new Node("level", children: new Dictionary<string, IDataNode> { ["1"] = level1 }),
            ["hit"] = new Node("hit"),
        };
        if (elemAttr is not null)
        {
            children["elemAttr"] = new Node("elemAttr", elemAttr);
        }

        return new Node(name, children: children);
    }

    private sealed class FakeProvider(params (string Path, IDataNode Node)[] nodes) : IDataProvider
    {
        private readonly Dictionary<string, IDataNode> _nodes = nodes.ToDictionary(static n => n.Path, static n => n.Node);

        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path)
            => fileName == "Skill" && _nodes.TryGetValue(path, out var node) ? node : null;
    }

    private sealed class Node(string name, object? value = null, IReadOnlyDictionary<string, IDataNode>? children = null) : IDataNode
    {
        public string Name { get; } = name;

        public object? Value { get; } = value;

        public IReadOnlyDictionary<string, IDataNode> Children { get; } = children ?? new Dictionary<string, IDataNode>();

        public IDataNode? this[string path] => Children.TryGetValue(path, out var child) ? child : null;
    }
}
