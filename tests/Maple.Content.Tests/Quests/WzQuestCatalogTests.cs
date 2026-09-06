using Maple.Content.Quests;
using Maple.Content.Wz;
using Maple.Core.Data;
using Maple.Core.Quests;

namespace Maple.Content.Tests.Quests;

public sealed class WzQuestCatalogTests
{
    private const string WzDir = @"E:\WorkSpace_離線資料\02_遊戲素材_game-assets\MapleStory\v113_Client";

    [Fact]
    public void GetQuest_ParsesQuestInfoCheckAndActNodes()
    {
        var provider = new FakeDataProvider();
        provider.Add("QuestInfo.img", Node.Image("QuestInfo.img")
            .With("1000", Node.Image("1000")
                .WithValue("name", "Test Quest")
                .WithValue("autoStart", 1)));
        provider.Add("Check.img", Node.Image("Check.img")
            .With("1000", Node.Image("1000")
                .With("0", Node.Image("0")
                    .WithValue("lvmin", 5)
                    .WithValue("normalAutoStart", 1))
                .With("1", Node.Image("1")
                    .With("item", Node.Image("item")
                        .With("0", Node.Image("0")
                            .WithValue("id", 4000000)
                            .WithValue("count", 2))))));
        provider.Add("Act.img", Node.Image("Act.img")
            .With("1000", Node.Image("1000")
                .With("1", Node.Image("1")
                    .WithValue("money", 50)
                    .With("item", Node.Image("item")
                        .With("0", Node.Image("0")
                            .WithValue("id", 2000000)
                            .WithValue("count", 1))))));

        var quest = new WzQuestCatalog(provider).GetQuest(1000);

        Assert.NotNull(quest);
        Assert.Equal("Test Quest", quest!.Name);
        Assert.True(quest.AutoStart);
        Assert.True(quest.Repeatable);
        Assert.Contains(quest.StartRequirements, r => r.Kind == QuestRequirementKind.LevelMin && r.IntValue == 5);
        Assert.Contains(quest.CompleteRequirements, r => r.Kind == QuestRequirementKind.Item);
        Assert.Contains(quest.CompleteActions, a => a.Kind == QuestActionKind.Money && a.IntValue == 50);
        var itemAction = Assert.Single(quest.CompleteActions, a => a.Kind == QuestActionKind.Item);
        Assert.Equal(2000000, Assert.Single(itemAction.Items!).ItemId);
    }

    [Fact]
    public void GetQuest_LoadsQuest1000FromRealQuestWz()
    {
        using var provider = new WzDataProvider(WzDir);
        var quest = new WzQuestCatalog(provider).GetQuest(1000);

        Assert.NotNull(quest);
        Assert.Equal(1000, quest.Id);
        Assert.NotEmpty(quest.Name);
    }

    private sealed class FakeDataProvider : IDataProvider
    {
        private readonly Dictionary<string, IDataNode> _roots = new(StringComparer.Ordinal);

        public void Add(string path, IDataNode node) => _roots[path] = node;

        public IDataNode GetRoot(string fileName) => Node.Image(fileName);

        public IDataNode? GetAt(string fileName, string path) => _roots.GetValueOrDefault(path);
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
