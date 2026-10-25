using Maple.Application.Npcs;
using Maple.Application.Quests;
using Maple.Core.Characters;
using Maple.Core.Inventory;
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
        Directory.CreateDirectory(Path.Combine(_root, "quest"));
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

    [Fact]
    public async Task JavaShim_MathPointStringEquals_AndUiEffects()
    {
        // P171：Rhino 腳本的 java.lang.Math／java.awt.Point／String.equals 與 showInstruction／mapMessage。
        File.WriteAllText(Path.Combine(_root, "portal", "shim.js"), """
            function enter(pi) {
                var p = new java.awt.Point(3, 4);
                var n = java.lang.Math.floor(java.lang.Math.min(7.9, 9));
                if ("abc".equals("abc") && p.x == 3 && n == 7 && java.lang.Integer.MAX_VALUE == 2147483647) {
                    pi.showInstruction("提示", 0, 0);
                    pi.mapMessage(6, "地圖訊息");
                    pi.warpS(100000000, 2);
                }
            }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js" }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(0, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var effects = new List<NpcUiEffect>();
        (int, int)? warped = null;
        var convo = new NpcConversation(0, factory.TryCreatePortal("shim", ctx)!, ctx, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
            warpToPortal: (m, p, _) => { warped = (m, p); return Task.CompletedTask; },
            sendUiEffect: (e, _) => { effects.Add(e); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal([new NpcUiEffect(NpcUiEffectKind.Hint, "提示"), new NpcUiEffect(NpcUiEffectKind.MapMessage, "地圖訊息", 6)], effects);
        Assert.Equal((100000000, 2), warped);
    }

    [Fact]
    public async Task QuestScript_StartAndEnd_UseCurrentQuestId()
    {
        // P174：scripts/quest/<id>.js 的 start/end(mode, type, selection)；qm.forceStartQuest()／forceCompleteQuest() 作用在目前任務。
        File.WriteAllText(Path.Combine(_root, "quest", "2001.js"), """
            var status = -1;
            function start(mode, type, selection) {
                status++;
                if (status == 0) { qm.sendNext("開始任務 " + qm.getQuest()); }
                else { qm.forceStartQuest(); qm.dispose(); }
            }
            function end(mode, type, selection) {
                qm.gainExp(5);
                qm.forceCompleteQuest();
                qm.dispose();
            }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 10 }, new Position(0, 0, 0, 0));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var quests = new QuestService(new EmptyQuestCatalog());
        var dialogs = new List<NpcDialog>();

        var startCtx = new NpcContext(1012000, player, quests, questId: 2001);
        var starting = new NpcConversation(1012000, factory.TryCreateQuest(2001, end: false, startCtx)!, startCtx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (_, _) => Task.CompletedTask);
        await starting.StartAsync(CancellationToken.None);
        Assert.Equal("開始任務 2001", Assert.Single(dialogs).Text);
        await starting.ContinueAsync(1, 0, -1, CancellationToken.None);
        Assert.Equal(1, player.GetQuestStatus(2001));

        var endCtx = new NpcContext(1012000, player, quests, questId: 2001);
        var ending = new NpcConversation(1012000, factory.TryCreateQuest(2001, end: true, endCtx)!, endCtx, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        await ending.StartAsync(CancellationToken.None);
        Assert.Equal(2, player.GetQuestStatus(2001));
        Assert.Equal(5, player.Character.Exp);
        Assert.False(ending.Active);
        Assert.Null(factory.TryCreateQuest(9999, end: false, endCtx));
    }

    [Fact]
    public async Task MapFacade_MonstersReactors_HaveMonster_KillAllMob()
    {
        // P175：portal/PQ 腳本常用的 getAllMonstersThreadsafe().size()、getReactorByName(name).getState()、haveMonster、killAllMob。
        File.WriteAllText(Path.Combine(_root, "portal", "pq.js"), """
            function enter(pi) {
                var map = pi.getMap();
                if (map.getAllMonstersThreadsafe().size() == 2 && map.countMonsterById(9300001) == 2 && pi.haveMonster(9300001)
                    && map.getReactorByName("gate").getState() == 1 && map.getReactorByName("none") == null) {
                    pi.killAllMob();
                    pi.warp(100000000, 0);
                }
            }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", MapId = 103000800 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(0, player, new QuestService(new EmptyQuestCatalog()),
            monstersOf: id => id == 103000800 ? [9300001, 9300001] : [],
            reactorStateOf: (id, name) => name == "gate" ? 1 : -1);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var killed = 0;
        var warped = 0;
        var convo = new NpcConversation(0, factory.TryCreatePortal("pq", ctx)!, ctx, (_, _) => Task.CompletedTask, (_, _) => { warped++; return Task.CompletedTask; },
            killAllMob: _ => { killed++; return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal((1, 1), (killed, warped));
    }

    [Fact]
    public async Task PartyApi_WarpPartyAndPartyExp_TargetMembersOnSameMap()
    {
        // P176：Java warpParty／givePartyExp：只對與自己同地圖的隊員；經驗 × 頻道倍率；自己的經驗當場給。
        File.WriteAllText(Path.Combine(_root, "npc", "9999995.js"), """
            function start() {
                if (cm.isLeader() && cm.getParty().getMembers().size() == 3 && !cm.allMembersHere()) {
                    cm.givePartyExp(10);
                    cm.warpParty(103000800);
                }
                cm.dispose();
            }
            """);
        var parties = new Maple.Application.Parties.InMemoryPartyRegistry();
        var leader = new Player(new Character { Id = 1, Name = "L", Level = 30, MapId = 103000000 }, new Position(0, 0, 0, 0));
        var created = parties.CreateParty(Maple.Core.Parties.PartyMember.FromCharacter(leader.Character, 1));
        parties.JoinParty(created.Party!.Id, new Maple.Core.Parties.PartyMember(2, "Near", 10, 100, 103000000, 1));
        parties.JoinParty(created.Party!.Id, new Maple.Core.Parties.PartyMember(3, "Far", 10, 100, 100000000, 1));
        var ctx = new NpcContext(9999995, leader, new QuestService(new EmptyQuestCatalog()),
            parties: parties, playersOnMap: map => map == 103000000 ? [1, 2] : [3], expRate: 4);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var actions = new List<NpcPartyAction>();
        var convo = new NpcConversation(9999995, factory.TryCreate(9999995, ctx)!, ctx, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
            sendPartyAction: (a, _) => { actions.Add(a); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal(40, leader.Character.Exp);
        Assert.Equal(
            [
                new NpcPartyAction(NpcPartyActionKind.Exp, 2, Amount: 40),
                new NpcPartyAction(NpcPartyActionKind.Warp, 1, MapId: 103000800),
                new NpcPartyAction(NpcPartyActionKind.Warp, 2, MapId: 103000800),
            ],
            actions);
    }

    [Fact]
    public async Task Script_BossLogAndOneTimeLog()
    {
        // P191：cm.getBossLog／setBossLog、cm.getPlayer().getOneTimeLog／setOneTimeLog。
        File.WriteAllText(Path.Combine(_root, "npc", "9999990.js"), """
            function start() {
                cm.setBossLog("ZAK");
                cm.getPlayer().setOneTimeLog("fk1");
                cm.sendOk(cm.getBossLog("ZAK") + "/" + cm.getPlayer().getBossLog("HT") + "/" + cm.getOneTimeLog("fk1"));
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999990, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var dialogs = new List<NpcDialog>();
        var convo = new NpcConversation(9999990, factory.TryCreate(9999990, ctx)!, ctx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (_, _) => Task.CompletedTask);

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal("1/0/1", Assert.Single(dialogs).Text);
        Assert.Single(player.Character.BossLog);
    }

    [Fact]
    public async Task Script_ExpandInventoryGetInventoryAndHelpers()
    {
        // P192：expandInventory（上限 96）＋getSlotUpdate、getInventory(type) 查詢、setQuestRecord、getReadableMillis。
        File.WriteAllText(Path.Combine(_root, "npc", "9999989.js"), """
            function start() {
                cm.expandInventory(2, 4);
                var inv = cm.getInventory(2);
                cm.setQuestRecord(cm.getPlayer(), 7777, "abc");
                cm.sendOk(inv.getSlotLimit() + "/" + inv.getNumFreeSlot() + "/" + inv.getNextFreeSlot() + "/" + inv.getItem(1).getQuantity()
                    + "/" + cm.getReadableMillis(0, 3723000) + "/" + cm.getName());
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30 }, new Position(0, 0, 0, 0));
        player.Inventory.By(InventoryType.Use).Put(new Item { ItemId = 2000000, Slot = 1, Quantity = 7 });
        var ctx = new NpcContext(9999989, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var dialogs = new List<NpcDialog>();
        var slots = new List<(InventoryType, byte)>();
        var convo = new NpcConversation(9999989, factory.TryCreate(9999989, ctx)!, ctx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (_, _) => Task.CompletedTask,
            sendSlotUpdate: (t, n, _) => { slots.Add((t, n)); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal("28/27/2/7/1 hour, 2 minutes, and 3 seconds./Js", Assert.Single(dialogs).Text);
        Assert.Equal([(InventoryType.Use, (byte)28)], slots);
        Assert.Equal((byte)28, player.Character.UseSlots);
        Assert.Equal("abc", player.GetOrAddQuest(7777).CustomData);
        Assert.Equal((byte)96, player.ExpandInventory(InventoryType.Use, 200)); // 上限 96
    }

    [Fact]
    public async Task Script_GachaponRareMega_SpawnAndWarpMap()
    {
        // P193：gainGachaponItem 稀有度 1 → 全服公告；spawnMonster 玩家位置；warpMap 待處理。
        File.WriteAllText(Path.Combine(_root, "npc", "9999988.js"), """
            function start() {
                var got = cm.gainGachaponItem(2022217, 1, "測試轉蛋");
                cm.spawnMonster(100100, 2);
                cm.warpMap(100000000, 3);
                cm.sendOk("" + got);
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30, MapId = 104000000 }, new Position(15, 25, 0, 0));
        var ctx = new NpcContext(9999988, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var dialogs = new List<NpcDialog>();
        (string, int, (int, int, int, int, int)[], (int, int)?) seen = default;
        var convo = new NpcConversation(9999988, factory.TryCreate(9999988, ctx)!, ctx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (_, _) => Task.CompletedTask,
            applyWorldEffects: (c, _) =>
            {
                seen = (c.PendingGachaponMegas.Single().Message, c.PendingGachaponMegas.Single().Item.ItemId, c.PendingMobSpawns.ToArray(), c.PendingMapWarp);
                return Task.CompletedTask;
            });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal("2022217", Assert.Single(dialogs).Text);
        Assert.Equal("[測試轉蛋] Js : 被他抽到了，大家恭喜他吧！", seen.Item1);
        Assert.Equal(2022217, seen.Item2);
        Assert.Equal([(104000000, 100100, 2, 15, 25)], seen.Item3!);
        Assert.Equal((100000000, 3), seen.Item4);
        Assert.Equal(1, player.Inventory.By(InventoryType.Use).CountById(2022217));
    }

    [Fact]
    public async Task Script_TutorialUi_SummonHintOnce_IntroAndEnvironment()
    {
        // P194：summonMsg 首次先 playerSummonHint(true)；MovieClipIntroUI = DisableUI＋Lock；showMapEffect／environmentChange。
        File.WriteAllText(Path.Combine(_root, "npc", "9999987.js"), """
            function start() {
                cm.summonMsg("嗨");
                cm.summonMsg(3);
                cm.MovieClipIntroUI(true);
                cm.AranTutInstructionalBubble("Effect/OnUserEff.img/guideEffect/aranTutorial/legendBalloon1");
                cm.showMapEffect("maplemap/enter/100000000");
                cm.environmentChange(true, "gate");
                cm.dispose();
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 10 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999987, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var effects = new List<NpcUiEffect>();
        var environment = new List<(bool, int, string)>();
        var convo = new NpcConversation(9999987, factory.TryCreate(9999987, ctx)!, ctx, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
            sendEnvironment: (b, m, n, _) => { environment.Add((b, m, n)); return Task.CompletedTask; },
            sendUiEffect: (e, _) => { effects.Add(e); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal(
            [NpcUiEffectKind.SummonHelper, NpcUiEffectKind.SummonMessageText, NpcUiEffectKind.SummonMessageType, NpcUiEffectKind.IntroDisableUi, NpcUiEffectKind.IntroLock, NpcUiEffectKind.AranBalloon],
            effects.Select(static e => e.Kind));
        Assert.True(player.HasSummonHint);
        Assert.Equal([(false, 3, "maplemap/enter/100000000"), (true, 2, "gate")], environment);
    }

    [Fact]
    public async Task Script_MapFactoryChannelServerNpcAndWarpBack()
    {
        // P195：getMapFactory().getMap(id)、getChannelServer().getEvent()（無活動 −1）、getNpc、warpBack、removeNpc（no-op）。
        File.WriteAllText(Path.Combine(_root, "npc", "9999986.js"), """
            function start() {
                var m = cm.getMapFactory().getMap(240060100);
                cm.removeNpc(cm.getMapId(), cm.getNpc());
                cm.warpBack(200090070, 104000000, 80);
                cm.sendOk(m.getId() + "/" + cm.getChannelServer().getEvent() + "/" + cm.getNpc());
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999986, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var dialogs = new List<NpcDialog>();
        var warps = new List<int>();
        (int, int)? back = null;
        int? clock = null;
        var convo = new NpcConversation(9999986, factory.TryCreate(9999986, ctx)!, ctx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (m, _) => { warps.Add(m); return Task.CompletedTask; },
            applyWorldEffects: (c, _) => { back = c.PendingWarpBack; clock = c.PendingClock; return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal("240060100/-1/9999986", Assert.Single(dialogs).Text);
        Assert.Equal([200090070], warps);
        Assert.Equal((104000000, 80), back);
        Assert.Equal(80, clock);
    }

    [Fact]
    public async Task Script_GuildGp_PendingAndSnapshot()
    {
        // P196：cm.getGuild()（無公會 null）、gainGP 累計、getGP = 快照＋本次累計。
        File.WriteAllText(Path.Combine(_root, "npc", "9999985.js"), """
            function start() {
                if (cm.getGuild() == null) { cm.sendOk("none"); return; }
                cm.gainGP(30);
                cm.genericGuildMessage(0x1C);
                cm.sendOk(cm.getGuild().getId() + "/" + cm.getGP());
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30, GuildId = 7 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999985, player, new QuestService(new EmptyQuestCatalog()), guildPointsOf: id => id == 7 ? 100 : 0);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var dialogs = new List<NpcDialog>();
        (int, byte[])? pending = null;
        var convo = new NpcConversation(9999985, factory.TryCreate(9999985, ctx)!, ctx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (_, _) => Task.CompletedTask,
            applyWorldEffects: (c, _) => { pending = (c.PendingGuildPoints, c.PendingGuildMessages.ToArray()); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal("7/130", Assert.Single(dialogs).Text);
        Assert.Equal(30, pending!.Value.Item1);
        Assert.Equal(new byte[] { 0x1C }, pending.Value.Item2);
    }

    [Fact]
    public async Task Script_GainPet_ClampsLikeJava()
    {
        // P197：gainPet——ID 範圍外改 5000000、等級 ≤ 30、親密 ≤ 30000、飽食 ≤ 100。
        File.WriteAllText(Path.Combine(_root, "npc", "9999984.js"), """
            function start() { cm.gainPet(5999999, "龍", 99, 99999, 500, 45); cm.dispose(); }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30 }, new Position(0, 0, 0, 0));
        var ctx = new NpcContext(9999984, player, new QuestService(new EmptyQuestCatalog()));
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var convo = new NpcConversation(9999984, factory.TryCreate(9999984, ctx)!, ctx, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);

        await convo.StartAsync(CancellationToken.None);

        var pet = Assert.Single(player.Inventory.By(InventoryType.Cash).Items);
        Assert.Equal((5000000, "龍", (byte)30, (short)30000, (byte)100), (pet.ItemId, pet.Owner, pet.PetLevel, pet.PetCloseness, pet.PetFullness));
        Assert.True(pet.UniqueId > 0);
        Assert.True(pet.Expiration > DateTimeOffset.UtcNow.AddDays(44).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Script_MiscApis_RemoveSlotAddHpResetMapPartyBossLog()
    {
        // P199：removeSlot、addHP（夾限）、isCash、resetMap、partyMembersInMap、setPartyBossLog（自己直接寫、隊友排動作）、getChannelNumber。
        File.WriteAllText(Path.Combine(_root, "npc", "9999983.js"), """
            function start() {
                cm.removeSlot(4, 1, 2);
                cm.addHP(-9999);
                cm.resetMap(280030000);
                cm.setPartyBossLog("ZAK");
                cm.sendOk(cm.isCash(5000000) + "/" + cm.isCash(2000000) + "/" + cm.partyMembersInMap() + "/" + cm.getChannelNumber() + "/" + cm.isPlayerInstance());
            }
            function action(mode, type, selection) { cm.dispose(); }
            """);
        var parties = new Maple.Application.Parties.InMemoryPartyRegistry();
        var player = new Player(new Character { Id = 1, Name = "Js", Level = 30, MapId = 100000000 }, new Position(0, 0, 0, 0));
        player.Character.Stats.MaxHp = 500;
        player.Character.Stats.Hp = 300;
        player.Inventory.By(InventoryType.Etc).Put(new Item { ItemId = 4000000, Slot = 1, Quantity = 5 });
        var created = parties.CreateParty(Maple.Core.Parties.PartyMember.FromCharacter(player.Character, channelIndex: 0));
        parties.JoinParty(created.Party!.Id, Maple.Core.Parties.PartyMember.FromCharacter(new Character { Id = 2, Name = "Mate", MapId = 100000000 }, channelIndex: 0));
        var ctx = new NpcContext(9999983, player, new QuestService(new EmptyQuestCatalog()), parties: parties, playersOnMap: _ => [1, 2, 3], channelNumber: 3);
        var factory = new JintNpcScriptFactory(new NpcScriptOptions { ScriptsDirectory = _root }, NullLogger<JintNpcScriptFactory>.Instance);
        var dialogs = new List<NpcDialog>();
        var actions = new List<NpcPartyAction>();
        IReadOnlyList<int>? resets = null;
        var convo = new NpcConversation(9999983, factory.TryCreate(9999983, ctx)!, ctx, (d, _) => { dialogs.Add(d); return Task.CompletedTask; }, (_, _) => Task.CompletedTask,
            sendPartyAction: (a, _) => { actions.Add(a); return Task.CompletedTask; },
            applyWorldEffects: (c, _) => { resets = c.PendingMapResets.ToArray(); return Task.CompletedTask; });

        await convo.StartAsync(CancellationToken.None);

        Assert.Equal("true/false/2/3/false", Assert.Single(dialogs).Text);
        Assert.Equal((short)3, player.Inventory.By(InventoryType.Etc).Get(1)!.Quantity);
        Assert.Equal((short)0, player.Character.Stats.Hp);
        Assert.Equal([280030000], resets);
        Assert.Equal(1, player.GetBossLog("ZAK", DateTimeOffset.UtcNow));
        Assert.Equal(new NpcPartyAction(NpcPartyActionKind.BossLog, 2, Text: "ZAK"), Assert.Single(actions));
    }

    private sealed class EmptyQuestCatalog : IQuestCatalog
    {
        public QuestDefinition? GetQuest(int questId) => null;
    }
}
