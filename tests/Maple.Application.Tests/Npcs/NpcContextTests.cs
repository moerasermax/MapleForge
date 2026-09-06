using Maple.Application.Npcs;
using Maple.Application.Quests;
using Maple.Core.Characters;
using Maple.Core.Quests;
using Maple.Core.World;

namespace Maple.Application.Tests.Npcs;

/// <summary>
/// cm.getBuddyCapacity / cm.updateBuddyCapacity（P021）。
/// 對照 Java NPCConversationManager.updateBuddyCapacity → MapleCharacter.setBuddyCapacity
/// → client.sendPacket(MaplePacketCreator.updateBuddyCapacity)：即時委派 + 送包，非僅記錄待送。
/// cm.getPlayerStat（P022）：對照 Java AbstractPlayerInteraction.getPlayerStat 逐 key 核對。
/// </summary>
public sealed class NpcContextTests
{
    [Fact]
    public void GetBuddyCapacity_ReturnsPlayerBuddyListCapacity()
    {
        var player = NewPlayer();
        player.BuddyList.Capacity = 30;
        var ctx = new NpcContext(1002003, player, NewQuestService());

        Assert.Equal(30, ctx.GetBuddyCapacity());
    }

    [Fact]
    public void UpdateBuddyCapacity_WritesPlayerBuddyListCapacityImmediately()
    {
        var player = NewPlayer();
        var ctx = new NpcContext(1002003, player, NewQuestService());

        ctx.UpdateBuddyCapacity(25);

        Assert.Equal(25, player.BuddyList.Capacity);
    }

    [Fact]
    public async Task NpcConversation_FlushesBuddyCapacityUpdate_WhenScriptCallsIt()
    {
        var player = NewPlayer();
        var ctx = new NpcContext(1002003, player, NewQuestService());
        var sent = new List<int>();

        var convo = new NpcConversation(
            1002003,
            new UpdateBuddyCapacityScript(ctx, newCapacity: 25),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            sendBuddyCapacity: (capacity, _) =>
            {
                sent.Add(capacity);
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal([25], sent);
        Assert.Equal(25, player.BuddyList.Capacity);
    }

    [Fact]
    public async Task NpcConversation_DoesNotFlushBuddyCapacity_WhenScriptDoesNotCallIt()
    {
        var player = NewPlayer();
        var ctx = new NpcContext(1002003, player, NewQuestService());
        var sendBuddyCapacityCalled = false;

        var convo = new NpcConversation(
            1002003,
            new NoOpScript(),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            sendBuddyCapacity: (_, _) =>
            {
                sendBuddyCapacityCalled = true;
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.False(sendBuddyCapacityCalled);
    }

    [Fact]
    public async Task NpcConversation_DoesNotResendBuddyCapacity_OnFollowUpTurnWithoutNewCall()
    {
        var player = NewPlayer();
        var ctx = new NpcContext(1002003, player, NewQuestService());
        var sent = new List<int>();
        var script = new UpdateBuddyCapacityOnStartOnlyScript(ctx, newCapacity: 25);

        var convo = new NpcConversation(
            1002003,
            script,
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            sendBuddyCapacity: (capacity, _) =>
            {
                sent.Add(capacity);
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);
        await convo.ContinueAsync(1, 0, -1, CancellationToken.None);

        Assert.Equal([25], sent);
    }

    [Theory]
    [InlineData("LVL", 30)]
    [InlineData("STR", 12)]
    [InlineData("DEX", 5)]
    [InlineData("INT", 4)]
    [InlineData("LUK", 4)]
    [InlineData("HP", 50)]
    [InlineData("MP", 5)]
    [InlineData("MAXHP", 50)]
    [InlineData("MAXMP", 5)]
    [InlineData("GID", 0)]
    [InlineData("GRANK", 5)]
    [InlineData("ARANK", 5)]
    [InlineData("GM", 0)]
    [InlineData("ADMIN", 0)]
    [InlineData("GENDER", 0)]
    [InlineData("UNKNOWN_KEY", -1)]
    public void GetPlayerStat_MatchesJavaAbstractPlayerInteractionMapping(string type, int expected)
    {
        var player = NewPlayer();
        var ctx = new NpcContext(1002003, player, NewQuestService());

        Assert.Equal(expected, ctx.GetPlayerStat(type));
    }

    [Fact]
    public void GetPlayerStat_RemainingApSp_ReadsCharacterFields()
    {
        var player = NewPlayer();
        player.Character.RemainingAp = 7;
        player.Character.RemainingSp = 3;
        var ctx = new NpcContext(1002003, player, NewQuestService());

        Assert.Equal(7, ctx.GetPlayerStat("RAP"));
        Assert.Equal(3, ctx.GetPlayerStat("RSP"));
    }

    [Fact]
    public void GetPlayerStat_Gid_ReflectsCharacterGuildMembership()
    {
        var player = NewPlayer();
        player.Character.GuildId = 42;
        player.Character.GuildRank = 1;
        var ctx = new NpcContext(1002003, player, NewQuestService());

        Assert.Equal(42, ctx.GetPlayerStat("GID"));
        Assert.Equal(1, ctx.GetPlayerStat("GRANK"));
    }

    private static Player NewPlayer()
        => new(
            new Character { Id = 1, Name = "NpcApp", Level = 30 },
            new Position(0, 0, 0, 0));

    private static QuestService NewQuestService() => new(new EmptyQuestCatalog());

    private sealed class EmptyQuestCatalog : IQuestCatalog
    {
        public QuestDefinition? GetQuest(int questId) => null;
    }

    private sealed class UpdateBuddyCapacityScript : INpcScript
    {
        private readonly NpcContext _ctx;
        private readonly int _newCapacity;

        public UpdateBuddyCapacityScript(NpcContext ctx, int newCapacity)
        {
            _ctx = ctx;
            _newCapacity = newCapacity;
        }

        public void Start()
        {
            _ctx.UpdateBuddyCapacity(_newCapacity);
            _ctx.Dispose();
        }

        public void Resume(int mode, int type, int selection) { }
    }

    private sealed class NoOpScript : INpcScript
    {
        public void Start() { }

        public void Resume(int mode, int type, int selection) { }
    }

    private sealed class UpdateBuddyCapacityOnStartOnlyScript : INpcScript
    {
        private readonly NpcContext _ctx;
        private readonly int _newCapacity;

        public UpdateBuddyCapacityOnStartOnlyScript(NpcContext ctx, int newCapacity)
        {
            _ctx = ctx;
            _newCapacity = newCapacity;
        }

        public void Start() => _ctx.UpdateBuddyCapacity(_newCapacity);

        public void Resume(int mode, int type, int selection) { }
    }
}
