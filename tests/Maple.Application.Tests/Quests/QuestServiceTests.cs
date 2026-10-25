using Maple.Application.Quests;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Quests;
using Maple.Core.World;

namespace Maple.Application.Tests.Quests;

public sealed class QuestServiceTests
{
    [Fact]
    public void StartQuest_AppliesStartRewardsAndPersistsStartedStatus()
    {
        var service = new QuestService(new FakeQuestCatalog(Quest(
            1000,
            startActions:
            [
                new QuestAction(QuestActionKind.Money, IntValue: 50),
                new QuestAction(QuestActionKind.Item, Items: [new QuestItemAction(2000000, 2, 0, 2, 0, 0, -2)]),
            ])));
        var player = NewPlayer(meso: 100);

        var result = service.StartQuest(player, 1000, npc: 1012000);

        Assert.Equal(QuestTransactionStatus.Success, result.Status);
        Assert.Equal((byte)QuestStatus.Started, result.Quest?.Status);
        Assert.Equal(150, player.Character.Meso);
        Assert.Equal(2, player.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Single(result.GainedItems);
        Assert.True(result.MesoChanged);
    }

    [Fact]
    public void CompleteQuest_RemovesRequiredItemsAndGrantsRewards()
    {
        var service = new QuestService(new FakeQuestCatalog(Quest(
            1000,
            completeRequirements:
            [
                new QuestRequirement(QuestRequirementKind.Item, Values: [new QuestIntPair(4000000, 2)]),
            ],
            completeActions:
            [
                new QuestAction(QuestActionKind.Item, Items:
                [
                    new QuestItemAction(4000000, -2, 0, 2, 0, 0, -2),
                    new QuestItemAction(2000001, 1, 0, 2, 0, 0, -2),
                ]),
                new QuestAction(QuestActionKind.Money, IntValue: 25),
            ])));
        var player = NewPlayer(meso: 100, Item(4000000, 1, 2));
        player.ForceStartQuest(1000, npc: 1012000, customData: null, relevantMobs: null);

        var result = service.CompleteQuest(player, 1000, npc: 1012000);

        Assert.Equal(QuestTransactionStatus.Success, result.Status);
        Assert.Equal((byte)QuestStatus.Completed, result.Quest?.Status);
        Assert.Equal(125, player.Character.Meso);
        Assert.Equal(0, player.Inventory.By(InventoryType.Etc).CountById(4000000));
        Assert.Equal(1, player.Inventory.By(InventoryType.Use).CountById(2000001));
        Assert.Equal(1000, result.ShowQuestCompletionId);
        Assert.Single(result.InventoryMutations);
        Assert.Single(result.GainedItems);
    }

    [Fact]
    public void CompleteQuest_MissingRequiredItem_ReturnsCannotComplete()
    {
        var service = new QuestService(new FakeQuestCatalog(Quest(
            1000,
            completeRequirements:
            [
                new QuestRequirement(QuestRequirementKind.Item, Values: [new QuestIntPair(4000000, 1)]),
            ])));
        var player = NewPlayer(meso: 100);
        player.ForceStartQuest(1000, npc: 1012000, customData: null, relevantMobs: null);

        var result = service.CompleteQuest(player, 1000, npc: 1012000);

        Assert.Equal(QuestTransactionStatus.CannotComplete, result.Status);
        Assert.Equal((byte)QuestStatus.Started, player.GetQuestStatus(1000));
    }

    [Fact]
    public void CompleteQuest_GrantsExpFameSkillAndSp()
    {
        // P163：Java runEnd exp／pop／skill／sp。
        var service = new QuestService(new FakeQuestCatalog(Quest(
            1000,
            completeActions:
            [
                new QuestAction(QuestActionKind.Exp, IntValue: 10),
                new QuestAction(QuestActionKind.Pop, IntValue: 2),
                new QuestAction(QuestActionKind.Skill, Skills:
                [
                    new QuestSkillAction(1003, 1, 1, []), // 初心者技能：不看職業
                    new QuestSkillAction(1001004, 5, 10, [200]), // 職業不符
                ]),
                new QuestAction(QuestActionKind.Sp, IntValue: 3),
                new QuestAction(QuestActionKind.BuffItemId, IntValue: 2022109),
            ])));
        var player = NewPlayer(meso: 0);
        player.ForceStartQuest(1000, npc: 1012000, customData: null, relevantMobs: null);

        var result = service.CompleteQuest(player, 1000, npc: 1012000);

        Assert.Equal(QuestTransactionStatus.Success, result.Status);
        Assert.Equal(10, player.Character.Exp);
        Assert.Equal(10, result.ExpGained);
        Assert.Equal(2, player.Character.Fame);
        Assert.Equal(2, result.FameGained);
        Assert.Equal(1, player.GetSkillLevel(1003));
        Assert.Equal(0, player.GetSkillLevel(1001004));
        Assert.Equal([(1003, (byte)1, (byte)1)], result.SkillChanges);
        Assert.Equal(3, player.Character.RemainingSp);
        Assert.Equal(2022109, result.BuffItemId);
        Assert.Contains(new PlayerStatUpdate(PlayerStatKind.Fame, 2), result.StatUpdates);
    }

    [Fact]
    public void StartQuest_Forfeited_SkipsExpMoneyAndFame()
    {
        var service = new QuestService(new FakeQuestCatalog(Quest(
            1000,
            startActions:
            [
                new QuestAction(QuestActionKind.Exp, IntValue: 10),
                new QuestAction(QuestActionKind.Money, IntValue: 50),
                new QuestAction(QuestActionKind.Pop, IntValue: 1),
            ])));
        var player = NewPlayer(meso: 100);
        player.GetOrAddQuest(1000).Forfeited = 1;

        var result = service.StartQuest(player, 1000, npc: 1012000);

        Assert.Equal(QuestTransactionStatus.Success, result.Status);
        Assert.Equal(0, player.Character.Exp);
        Assert.Equal(100, player.Character.Meso);
        Assert.Equal(0, player.Character.Fame);
    }

    private static Player NewPlayer(int meso, params ItemRecord[] items)
        => new(
            new Character
            {
                Id = 1,
                Name = "QuestApp",
                Meso = meso,
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private static ItemRecord Item(int itemId, short slot, short quantity) => new()
    {
        Type = (byte)Player.InventoryTypeOf(itemId),
        ItemId = itemId,
        Slot = slot,
        Quantity = quantity,
    };

    private static QuestDefinition Quest(
        int id,
        IReadOnlyList<QuestRequirement>? startRequirements = null,
        IReadOnlyList<QuestRequirement>? completeRequirements = null,
        IReadOnlyList<QuestAction>? startActions = null,
        IReadOnlyList<QuestAction>? completeActions = null)
        => new(
            id,
            "test",
            AutoStart: false,
            AutoPreComplete: false,
            AutoAccept: false,
            AutoComplete: false,
            Repeatable: false,
            ScriptedStart: false,
            ScriptedEnd: false,
            Blocked: false,
            ViewMedalItem: 0,
            SelectedSkillId: 0,
            StartRequirements: startRequirements ?? Array.Empty<QuestRequirement>(),
            CompleteRequirements: completeRequirements ?? Array.Empty<QuestRequirement>(),
            StartActions: startActions ?? Array.Empty<QuestAction>(),
            CompleteActions: completeActions ?? Array.Empty<QuestAction>(),
            RelevantMobs: new Dictionary<int, int>());

    private sealed class FakeQuestCatalog : IQuestCatalog
    {
        private readonly QuestDefinition _quest;

        public FakeQuestCatalog(QuestDefinition quest)
        {
            _quest = quest;
        }

        public QuestDefinition? GetQuest(int questId) => questId == _quest.Id ? _quest : null;
    }
}
