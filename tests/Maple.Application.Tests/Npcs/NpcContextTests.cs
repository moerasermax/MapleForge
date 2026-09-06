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
/// cm.increaseGuildCapacity（P023）：驗證 meso/gid 兩道守門 + pending 委派時機，實際公會擴充
/// 邏輯由 GuildServiceTests 覆蓋。
/// cm.disbandGuild（P024）：驗證會長/公會兩道守門 + pending 委派時機，實際解散邏輯由
/// GuildServiceTests 覆蓋。
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

    [Fact]
    public async Task NpcConversation_IncreaseGuildCapacity_SufficientMesoAndGuild_InvokesDelegate()
    {
        var player = NewPlayer();
        player.Character.Meso = 250_000;
        player.Character.GuildId = 7;
        var ctx = new NpcContext(2010007, player, NewQuestService());
        var increaseCalled = false;

        var convo = new NpcConversation(
            2010007,
            new ActionScript(() => ctx.IncreaseGuildCapacity()),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            increaseGuildCapacity: _ =>
            {
                increaseCalled = true;
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.True(increaseCalled);
    }

    [Fact]
    public async Task NpcConversation_IncreaseGuildCapacity_InsufficientMeso_SendsPopupNotIncreaseRequest()
    {
        var player = NewPlayer();
        player.Character.Meso = 249_999;
        player.Character.GuildId = 7;
        var ctx = new NpcContext(2010007, player, NewQuestService());
        var increaseCalled = false;
        string? popup = null;

        var convo = new NpcConversation(
            2010007,
            new ActionScript(() => ctx.IncreaseGuildCapacity()),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            increaseGuildCapacity: _ =>
            {
                increaseCalled = true;
                return Task.CompletedTask;
            },
            sendPopupMessage: (msg, _) =>
            {
                popup = msg;
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.False(increaseCalled);
        Assert.Equal("金錢不足25萬.", popup);
    }

    [Fact]
    public async Task NpcConversation_IncreaseGuildCapacity_NoGuild_SendsNothing_MatchingJavaSilentReturn()
    {
        var player = NewPlayer();
        player.Character.Meso = 250_000;
        player.Character.GuildId = 0;
        var ctx = new NpcContext(2010007, player, NewQuestService());
        var increaseCalled = false;
        var popupCalled = false;

        var convo = new NpcConversation(
            2010007,
            new ActionScript(() => ctx.IncreaseGuildCapacity()),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            increaseGuildCapacity: _ =>
            {
                increaseCalled = true;
                return Task.CompletedTask;
            },
            sendPopupMessage: (_, _) =>
            {
                popupCalled = true;
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.False(increaseCalled);
        Assert.False(popupCalled);
    }

    [Fact]
    public async Task NpcConversation_DisbandGuild_Leader_InvokesDelegate()
    {
        var player = NewPlayer();
        player.Character.GuildId = 7;
        player.Character.GuildRank = 1;
        var ctx = new NpcContext(2010007, player, NewQuestService());
        var disbandCalled = false;

        var convo = new NpcConversation(
            2010007,
            new ActionScript(() => ctx.DisbandGuild()),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            disbandGuild: _ =>
            {
                disbandCalled = true;
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.True(disbandCalled);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 2)]
    public async Task NpcConversation_DisbandGuild_NotLeaderOrNoGuild_SendsNothing_MatchingJavaSilentReturn(int guildId, byte guildRank)
    {
        var player = NewPlayer();
        player.Character.GuildId = guildId;
        player.Character.GuildRank = guildRank;
        var ctx = new NpcContext(2010007, player, NewQuestService());
        var disbandCalled = false;

        var convo = new NpcConversation(
            2010007,
            new ActionScript(() => ctx.DisbandGuild()),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            disbandGuild: _ =>
            {
                disbandCalled = true;
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.False(disbandCalled);
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

    private sealed class ActionScript : INpcScript
    {
        private readonly Action _onStart;

        public ActionScript(Action onStart) => _onStart = onStart;

        public void Start() => _onStart();

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
