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

    [Fact]
    public async Task NpcConversation_SendRepairWindow_InvokesDelegateWithNpcId_AndDoesNotAutoEndConversation()
    {
        var player = NewPlayer();
        var ctx = new NpcContext(2080000, player, NewQuestService());
        var repairNpcId = 0;

        var convo = new NpcConversation(
            2080000,
            new ActionScript(() => ctx.SendRepairWindow()),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            sendRepairWindow: (npcId, _) =>
            {
                repairNpcId = npcId;
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal(2080000, repairNpcId);
        // 對照 Java 2080000.js：cm.sendRepairWindow() 之後腳本自行呼叫 cm.dispose()，
        // 這裡的 ActionScript 只呼叫 sendRepairWindow 不呼叫 dispose，驗證本身不會自動結束對話。
        Assert.True(convo.Active);
    }

    [Fact]
    public async Task GainItemAndMeso_FlushAsQuestResultsWithNotices()
    {
        // P166：Java gainItem → getShowItemGain(id, qty, true)；gainMeso → showMesoGain(gain, true)；背包／楓幣封包隨結果送出。
        var player = NewPlayer();
        var ctx = new NpcContext(9000000, player, NewQuestService());
        var results = new List<QuestTransactionResult>();
        var convo = new NpcConversation(
            9000000,
            new ActionScript(() =>
            {
                ctx.GainItem(2000000, 3);
                ctx.GainMeso(500);
                ctx.GainItem(2000000, -3);
                ctx.GainItem(2000001, -1); // 沒有 → 不扣、不提示
            }),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            sendQuestResult: (r, _) =>
            {
                results.Add(r);
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal([(2000000, 3)], results[0].ItemGainNotices);
        Assert.Single(results[0].GainedItems);
        Assert.Equal(500, results[1].MesoGained);
        Assert.Equal(500, player.Character.Meso);
        Assert.Equal([(2000000, -3)], results[2].ItemGainNotices);
        Assert.Equal(0, ctx.ItemQuantity(2000000));
    }

    [Fact]
    public void HaveItemQuantity_CanHold_RemoveAll()
    {
        var player = NewPlayer();
        var ctx = new NpcContext(9000000, player, NewQuestService());
        ctx.GainItem(4000000, 5);
        ctx.GainItem(4000000, 2);

        Assert.True(ctx.HaveItem(4000000, 7));
        Assert.False(ctx.HaveItem(4000000, 8));
        Assert.True(ctx.CanHold(4000000));
        Assert.True(ctx.CanHold());

        ctx.RemoveAll(4000000);
        Assert.Equal(0, ctx.ItemQuantity(4000000));
    }

    [Fact]
    public async Task SafeDispose_EndsOnNextResponseWithoutCallingScript()
    {
        var player = NewPlayer();
        var ctx = new NpcContext(9000000, player, NewQuestService());
        var resumed = 0;
        var script = new CountingScript(() => { ctx.SendOk("bye"); ctx.SafeDispose(); }, () => resumed++);
        var convo = new NpcConversation(9000000, script, ctx, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);

        await convo.StartAsync(CancellationToken.None);
        Assert.True(convo.Active);
        await convo.ContinueAsync(1, 0, -1, CancellationToken.None);

        Assert.False(convo.Active);
        Assert.Equal(0, resumed);
    }

    [Fact]
    public async Task GainExp_WarpPortal_PlayerMessage_Flush()
    {
        var player = NewPlayer();
        var ctx = new NpcContext(9000000, player, NewQuestService());
        var results = new List<QuestTransactionResult>();
        (int Map, int Portal)? warped = null;
        var messages = new List<(int, string)>();
        var convo = new NpcConversation(
            9000000,
            new ActionScript(() =>
            {
                ctx.GainExp(10);
                ctx.PlayerMessage("hello");
                ctx.Warp(100000000, 2);
            }),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            sendQuestResult: (r, _) => { results.Add(r); return Task.CompletedTask; },
            warpToPortal: (map, portal, _) => { warped = (map, portal); return Task.CompletedTask; },
            sendMessage: (type, msg, _) => { messages.Add((type, msg)); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal(10, Assert.Single(results).ExpGained);
        Assert.Equal(10, player.Character.Exp);
        Assert.Equal((100000000, 2), warped);
        Assert.Equal([(5, "hello")], messages);
    }

    [Fact]
    public async Task SavedLocation_ItemPeriod_EnvironmentAndWorldMessage()
    {
        // P169：Java saveLocation／getSavedLocation（未記錄 → 100000000）、gainItemPeriod、playSound／showEffect、worldMessage。
        var player = NewPlayer();
        player.Character.MapId = 101000000;
        var ctx = new NpcContext(9000000, player, NewQuestService(), playerCount: id => id == 910000000 ? 3 : 0);
        Assert.Equal(100000000, ctx.GetSavedLocation("FREE_MARKET"));
        ctx.SaveLocation("FREE_MARKET");
        Assert.Equal(101000000, ctx.GetSavedLocation("FREE_MARKET"));
        ctx.ClearSavedLocation("FREE_MARKET");
        Assert.Equal(100000000, ctx.GetSavedLocation("FREE_MARKET"));
        Assert.Equal(3, ctx.GetPlayerCount(910000000));

        var env = new List<(bool, int, string)>();
        var world = new List<(int, string)>();
        var results = new List<QuestTransactionResult>();
        var convo = new NpcConversation(
            9000000,
            new ActionScript(() =>
            {
                ctx.GainItemPeriod(1302000, 1, 7);
                ctx.PlaySound(true, "Party1/Clear");
                ctx.ShowEffect(false, "quest/party/clear");
                ctx.WorldMessage("恭喜");
            }),
            ctx,
            sendDialog: (_, _) => Task.CompletedTask,
            warp: (_, _) => Task.CompletedTask,
            sendQuestResult: (r, _) => { results.Add(r); return Task.CompletedTask; },
            sendEnvironment: (b, m, n, _) => { env.Add((b, m, n)); return Task.CompletedTask; },
            sendWorldMessage: (t, m, _) => { world.Add((t, m)); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.True(Assert.Single(Assert.Single(results).GainedItems).Expiration > DateTimeOffset.UtcNow.AddDays(6).ToUnixTimeMilliseconds());
        Assert.Equal([(true, 4, "Party1/Clear"), (false, 3, "quest/party/clear")], env);
        Assert.Equal([(6, "恭喜")], world);
    }

    private sealed class CountingScript(Action start, Action resume) : INpcScript
    {
        public void Start() => start();

        public void Resume(int mode, int type, int selection) => resume();
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
