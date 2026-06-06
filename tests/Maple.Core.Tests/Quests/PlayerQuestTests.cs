using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Quests;
using Maple.Core.World;

namespace Maple.Core.Tests.Quests;

public sealed class PlayerQuestTests
{
    [Fact]
    public void ForceStartQuest_WritesMapleQuestStatusFields()
    {
        var player = NewPlayer();
        var mobs = new Dictionary<int, int> { [100100] = 3 };

        var quest = player.ForceStartQuest(1000, npc: 1012000, customData: "000", mobs);

        Assert.Equal(1000, quest.QuestId);
        Assert.Equal((byte)QuestStatus.Started, quest.Status);
        Assert.Equal(1012000, quest.Npc);
        Assert.Equal("000", quest.CustomData);
        var kill = Assert.Single(quest.MobKills);
        Assert.Equal(100100, kill.MobId);
        Assert.Equal(0, kill.Count);
    }

    [Fact]
    public void ForfeitQuest_OnlyStartedQuest_IncrementsForfeit()
    {
        var player = NewPlayer();
        player.ForceStartQuest(1000, npc: 1012000, customData: null, relevantMobs: null);

        var quest = player.ForfeitQuest(1000);

        Assert.NotNull(quest);
        Assert.Equal((byte)QuestStatus.NotStarted, quest!.Status);
        Assert.Equal(1, quest.Forfeited);
        Assert.Empty(quest.MobKills);
    }

    [Fact]
    public void TryTakeItemById_RemovesAcrossStacksAndReportsMutations()
    {
        var player = NewPlayer(
            Item(4000000, slot: 1, quantity: 2),
            Item(4000000, slot: 2, quantity: 3));

        var ok = player.TryTakeItemById(InventoryType.Etc, 4000000, 4, out var mutations);

        Assert.True(ok);
        Assert.Null(player.Inventory.By(InventoryType.Etc).Get(1));
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Etc).Get(2)!.Quantity);
        Assert.Collection(
            mutations,
            m => Assert.Equal((short)0, m.NewQuantity),
            m => Assert.Equal((short)1, m.NewQuantity));
    }

    private static Player NewPlayer(params ItemRecord[] items)
        => new(
            new Character
            {
                Id = 1,
                Name = "QuestCore",
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private static ItemRecord Item(int itemId, short slot, short quantity) => new()
    {
        Type = (byte)InventoryType.Etc,
        ItemId = itemId,
        Slot = slot,
        Quantity = quantity,
    };
}
