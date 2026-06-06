using Maple.Core.Characters;

namespace Maple.Core.Tests.Characters;

public sealed class BuddyListTests
{
    [Fact]
    public void Put_ReplacesExistingBuddyWithoutChangingCapacity()
    {
        var list = new BuddyList { Capacity = 2 };
        list.Put(new BuddyEntry { CharacterId = 10, Name = "Alpha", Group = "A", Visible = false });
        list.Put(new BuddyEntry { CharacterId = 10, Name = "Alpha", Group = "B", Visible = true });

        var entry = Assert.Single(list.Entries);
        Assert.Equal("B", entry.Group);
        Assert.True(entry.Visible);
        Assert.False(list.IsFull());
    }

    [Fact]
    public void TakeNextPendingRequest_MarksRequestPrompted()
    {
        var list = new BuddyList();
        list.Put(new BuddyEntry
        {
            CharacterId = 20,
            Name = "Bravo",
            PendingRequest = true,
            Visible = false,
        });

        var pending = list.TakeNextPendingRequest();

        Assert.NotNull(pending);
        Assert.Equal(20, pending.CharacterId);
        Assert.True(pending.RequestPrompted);
        Assert.Null(list.TakeNextPendingRequest());
    }

    [Fact]
    public void ResetRuntimeState_ClearsChannelsAndPromptedFlags()
    {
        var list = new BuddyList();
        list.Put(new BuddyEntry
        {
            CharacterId = 30,
            Name = "Charlie",
            Channel = 2,
            PendingRequest = true,
            RequestPrompted = true,
        });

        list.ResetRuntimeState();

        var entry = Assert.Single(list.Entries);
        Assert.Equal(-1, entry.Channel);
        Assert.False(entry.RequestPrompted);
    }
}
