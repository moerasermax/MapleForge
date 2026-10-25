using Maple.Application.Npcs;
using Maple.Application.Quests;
using Maple.Core.Characters;
using Maple.Core.Quests;
using Maple.Core.World;
using Maple.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple.Application.Tests.Npcs;

/// <summary>P166：Jint 實跑 OdinMS 風格腳本，確認新 cm API 與 cm.getPlayer() facade 的 overload 可被 JS 呼叫。</summary>
public sealed class JintNpcScriptApiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mf-jint-" + Guid.NewGuid().ToString("N"));

    public JintNpcScriptApiTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "npc"));
        Directory.CreateDirectory(Path.Combine(_root, "portal"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Script_UsesGetPlayerHaveItemQuantityAndGainItem()
    {
        File.WriteAllText(Path.Combine(_root, "npc", "9999999.js"), """
            function start() {
                cm.gainItem(4000000, 3);
                if (cm.getPlayer().getLevel() >= 30 && cm.haveItem(4000000, 3) && cm.canHold(2000000)) {
                    cm.sendOk("lv" + cm.getPlayer().getLevel() + " qty" + cm.itemQuantity(4000000) + " " + cm.getChar().getName());
                } else {
                    cm.sendOk("no");
                }
                cm.safeDispose();
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999999, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var script = factory.TryCreate(9999999, ctx)!;
        var dialogs = new List<NpcDialog>();
        var convo = new NpcConversation(9999999, script, ctx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (_, _) => Task.CompletedTask);

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal("lv30 qty3 Js", Assert.Single(dialogs).Text);
        await convo.ContinueAsync(1, 0, -1, CancellationToken.None);
        Assert.False(convo.Active);
    }

    [Fact]
    public async Task Script_ChangeJobTeachSkillAcceptDecline()
    {
        // P167：轉職 NPC 常見寫法：askAcceptDecline → changeJob → teachSkill(id, level)（master = 技能最大等級）。
        File.WriteAllText(Path.Combine(_root, "npc", "9999998.js"), """
            function start() { cm.askAcceptDecline("轉職？"); }
            function action(mode, type, selection) {
                cm.changeJob(100);
                cm.teachSkill(1000001, 1);
                cm.sendNext("#L0#選單#l");
                cm.dispose();
            }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 10 }, new Position(0, 0, 0, 0));
        var skill = new Maple.Core.Skills.MapleSkill { Id = 1000001, Effects = [new(), new(), new()] };
        var jobs = new Maple.Application.Stats.JobAdvancementService(new Maple.Application.Skills.InMemorySkillCatalog([skill]));
        var ctx = new NpcContext(9999998, player, new QuestService(new EmptyQuestCatalog()), jobs);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var dialogs = new List<NpcDialog>();
        var jobChanges = 0;
        var convo = new NpcConversation(
            9999998,
            factory.TryCreate(9999998, ctx)!,
            ctx,
            (d, _) => { dialogs.Add(d); return Task.CompletedTask; },
            (_, _) => Task.CompletedTask,
            sendJobChange: (_, _) => { jobChanges++; return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);
        await convo.ContinueAsync(1, 0x0B, -1, CancellationToken.None);

        Assert.Equal(NpcDialogKind.AcceptDecline, dialogs[0].Kind);
        Assert.Equal(NpcDialogKind.Simple, dialogs[1].Kind); // 含 #L 改 sendSimple
        Assert.Equal(1, jobChanges);
        Assert.Equal(100, player.Character.Job);
        Assert.Equal((1, 3), (player.GetSkillLevel(1000001), player.GetMasterLevel(1000001)));
    }

    [Fact]
    public async Task Script_SalonStyleWithJsArray_SetHair_GetText()
    {
        // P168：美容院腳本把 JS 陣列傳給 sendStyle，選擇後 setHair(hairnew[selection])。
        File.WriteAllText(Path.Combine(_root, "npc", "9999997.js"), """
            var hairnew = Array();
            function start() {
                hairnew.push(30000 + parseInt(cm.getPlayer().getHair() % 10));
                hairnew.push(30030);
                cm.sendStyle("選擇", cm.getCanHair(hairnew));
            }
            function action(mode, type, selection) {
                cm.setHair(hairnew[selection]);
                cm.sendGetText("名字?");
                cm.dispose();
            }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 10, Hair = 30002 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999997, player, new QuestService(new EmptyQuestCatalog()), itemExists: id => id != 30030);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var dialogs = new List<NpcDialog>();
        var results = new List<QuestTransactionResult>();
        var convo = new NpcConversation(
            9999997,
            factory.TryCreate(9999997, ctx)!,
            ctx,
            (d, _) => { dialogs.Add(d); return Task.CompletedTask; },
            (_, _) => Task.CompletedTask,
            sendQuestResult: (r, _) => { results.Add(r); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);
        Assert.Equal(NpcDialogKind.Style, dialogs[0].Kind);
        Assert.Equal([30002], dialogs[0].Styles); // 30030 不存在被 getCanHair 濾掉

        await convo.ContinueAsync(1, 7, 0, CancellationToken.None);
        Assert.Equal(30002, player.Character.Hair);
        Assert.True(Assert.Single(results).LookChanged);
    }

    [Fact]
    public async Task EscResponse_EndsWithoutCallingScript()
    {
        File.WriteAllText(Path.Combine(_root, "npc", "9999996.js"), """
            function start() { cm.sendNext("hi"); }
            function action(mode, type, selection) { cm.gainMeso(100); cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js" }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999996, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var convo = new NpcConversation(9999996, factory.TryCreate(9999996, ctx)!, ctx, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);

        await convo.StartAsync(CancellationToken.None);
        await convo.ContinueAsync(-1, 0, -1, CancellationToken.None);

        Assert.False(convo.Active);
        Assert.Equal(0, player.Character.Meso); // Java：mode -1 不呼叫 action
    }

    [Fact]
    public async Task PortalScript_MarketReturn_UsesSavedLocationMapPortalAndChangeMap()
    {
        // P170：scripts/portal/market00.js 風格：getSavedLocation → getMap(id).getPortal("market00") → getPlayer().changeMap。
        File.WriteAllText(Path.Combine(_root, "portal", "market00.js"), """
            function enter(pi) {
                var returnMap = pi.getSavedLocation("FREE_MARKET");
                pi.clearSavedLocation("FREE_MARKET");
                var target = pi.getMap(returnMap);
                var portal = target.getPortal("market00");
                if (portal == null) { portal = target.getPortal(0); }
                pi.playPortalSE();
                pi.getPlayer().changeMap(target, portal);
            }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30, MapId = 910000000 }, new Position(0, 0, 0, 0));
        player.Character.SavedLocations["FREE_MARKET"] = 100000000;
        var ctx = new NpcContext(0, player, new QuestService(new EmptyQuestCatalog()), portal: ("out00", 3), portalIdOf: (map, name) => map == 100000000 && name == "market00" ? 7 : -1);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        (int, int)? warped = null;
        var se = 0;
        var convo = new NpcConversation(
            0,
            factory.TryCreatePortal("market00", ctx)!,
            ctx,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            warpToPortal: (m, p, _) => { warped = (m, p); return Task.CompletedTask; },
            playPortalSe: _ => { se++; return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal((100000000, 7), warped);
        Assert.Equal(1, se);
        Assert.Equal(100000000, ctx.GetSavedLocation("FREE_MARKET")); // 已清除 → 預設值
    }

    [Fact]
    public async Task PortalScript_InFreeMarket_WarpsByPortalName()
    {
        File.WriteAllText(Path.Combine(_root, "portal", "market01.js"), "function enter(pi) { pi.inFreeMarket(); return true; }");
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30, MapId = 100000000 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(0, player, new QuestService(new EmptyQuestCatalog()), portalIdOf: (map, name) => map == 910000000 && name == "st00" ? 2 : -1);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        (int, int)? warped = null;
        var convo = new NpcConversation(0, factory.TryCreatePortal("market01", ctx)!, ctx, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
            warpToPortal: (m, p, _) => { warped = (m, p); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal((910000000, 2), warped);
        Assert.Equal(100000000, player.Character.SavedLocations["FREE_MARKET"]);
        Assert.Null(factory.TryCreatePortal("../npc/x", ctx));
    }

    private sealed class EmptyQuestCatalog : IQuestCatalog
    {
        public QuestDefinition? GetQuest(int questId) => null;
    }
}
