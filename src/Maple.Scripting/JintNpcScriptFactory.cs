using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jint;
using Maple.Application.Npcs;
using Microsoft.Extensions.Logging;

namespace Maple.Scripting;

/// <summary>
/// 用 Jint 跑既有 OdinMS NPC .js 腳本（不重寫腳本）。
/// **sandbox**：CLR 預設關閉（不呼叫 AllowClr）＋遞迴/敘述/逾時上限，擋惡意/失控腳本。
/// **相容層**：regex 剝除 load()/importPackage()，並注入 no-op shim 吃掉殘留 Java-ism。
/// cm 以「lowercase 委派物件」暴露給 JS（與 C# PascalCase 解耦，免命名策略不確定性）。
/// 預處理後的腳本原始碼依 npcId 快取。
/// </summary>
public sealed class JintNpcScriptFactory : INpcScriptFactory, IPortalScriptFactory, IQuestScriptFactory
{
    private readonly string _npcDir;
    private readonly string _portalDir;
    private readonly string _questDir;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string?> _questSourceCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> _portalSourceCache = new(StringComparer.Ordinal);
    private readonly ILogger<JintNpcScriptFactory> _log;
    private readonly ConcurrentDictionary<int, string?> _sourceCache = new();

    private static readonly Regex LoadCall = new(@"load\s*\([^)]*\)\s*;?", RegexOptions.Compiled);
    private static readonly Regex ImportPackageCall = new(@"importPackage\s*\([^)]*\)\s*;?", RegexOptions.Compiled);

    private readonly Maple.Application.Events.IEventScriptHost? _events;

    public JintNpcScriptFactory(NpcScriptOptions options, ILogger<JintNpcScriptFactory> log, Maple.Application.Events.IEventScriptHost? events = null)
    {
        _events = events;
        _npcDir = Path.Combine(options.ScriptsDirectory, "npc");
        _portalDir = Path.Combine(options.ScriptsDirectory, "portal");
        _questDir = Path.Combine(options.ScriptsDirectory, "quest");
        _log = log;
    }

    public INpcScript? TryCreate(int npcId, INpcScriptContext cm)
    {
        var source = _sourceCache.GetOrAdd(npcId, LoadSource);
        if (source is null) return null;

        try
        {
            var engine = BuildEngine(cm);
            engine.Execute(source);
            return new JintNpcScript(engine);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[NpcScript] npcId={Id} 腳本載入/執行失敗：{Msg}", npcId, ex.Message);
            return null;
        }
    }

    /// <summary>P170：scripts/portal/&lt;name&gt;.js；Start() 呼叫 <c>enter(pi)</c>（pi 同時設為全域）。</summary>
    public INpcScript? TryCreatePortal(string scriptName, INpcScriptContext pi)
    {
        if (string.IsNullOrWhiteSpace(scriptName) || scriptName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var source = _portalSourceCache.GetOrAdd(scriptName, name =>
        {
            var path = Path.Combine(_portalDir, $"{name}.js");
            return File.Exists(path) ? Preprocess(File.ReadAllText(path)) : null;
        });
        if (source is null) return null;

        try
        {
            var engine = BuildEngine(pi);
            engine.SetValue("pi", engine.GetValue("cm"));
            engine.Execute(source);
            return new JintPortalScript(engine);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[PortalScript] {Name} 腳本載入/執行失敗：{Msg}", scriptName, ex.Message);
            return null;
        }
    }

    /// <summary>P174：scripts/quest/&lt;id&gt;.js；腳本以 <c>qm</c>（同 cm facade）呼叫 API。</summary>
    public INpcScript? TryCreateQuest(int questId, bool end, INpcScriptContext qm)
    {
        var source = _questSourceCache.GetOrAdd(questId, id =>
        {
            var path = Path.Combine(_questDir, $"{id}.js");
            return File.Exists(path) ? Preprocess(File.ReadAllText(path)) : null;
        });
        if (source is null) return null;

        try
        {
            var engine = BuildEngine(qm);
            engine.SetValue("qm", engine.GetValue("cm"));
            engine.Execute(source);
            return new JintQuestScript(engine, end ? "end" : "start");
        }
        catch (Exception ex)
        {
            _log.LogWarning("[QuestScript] questId={Id} 腳本載入/執行失敗：{Msg}", questId, ex.Message);
            return null;
        }
    }

    private string? LoadSource(int npcId)
    {
        var path = Path.Combine(_npcDir, $"{npcId}.js");
        if (!File.Exists(path)) return null;
        return Preprocess(File.ReadAllText(path));
    }

    /// <summary>剝除 Jint 不認的 Rhino/Nashorn Java 互通呼叫（shim 另外注入吃殘留）。</summary>
    internal static string Preprocess(string raw)
    {
        raw = LoadCall.Replace(raw, string.Empty);
        raw = ImportPackageCall.Replace(raw, string.Empty);
        return raw;
    }

    private Engine BuildEngine(INpcScriptContext cm)
    {
        var engine = new Engine(o => o
            .LimitRecursion(64)
            .MaxStatements(200_000)
            .TimeoutInterval(TimeSpan.FromSeconds(2)));

        Action<int> openShop = cm is INpcShopScriptContext shopContext
            ? shopContext.OpenShop
            : _ => { };

        // 殘留 Java-ism 的 no-op shim（剝不乾淨時不炸）
        engine.SetValue("load", new Action<object?>(_ => { }));
        engine.SetValue("importPackage", new Action<object?>(_ => { }));
        engine.SetValue("Packages", new { });

        // P171：Rhino 腳本常用的 java.* 與 Java String 方法 shim（Math、Point、Integer.MAX_VALUE、String.equals）。
        engine.Execute(JavaShim);

        // cm facade：lowercase 名＝腳本 API。送對話類只記 pending、領域類即時委派。
        engine.SetValue("cm", new CmFacade(cm, openShop, _events));

        return engine;
    }

    internal const string JavaShim = """
        var java = {
            lang: {
                Math: { random: Math.random, floor: Math.floor, ceil: Math.ceil, min: Math.min, max: Math.max, abs: Math.abs, round: Math.round, pow: Math.pow, sqrt: Math.sqrt },
                Integer: { MAX_VALUE: 2147483647, MIN_VALUE: -2147483648, parseInt: function (s) { return parseInt(s, 10); } },
                Double: { POSITIVE_INFINITY: Infinity, NEGATIVE_INFINITY: -Infinity },
                System: { currentTimeMillis: function () { return Date.now(); } }
            },
            awt: {
                Point: function (x, y) { this.x = x; this.y = y; this.getX = function () { return this.x; }; this.getY = function () { return this.y; }; },
                Rectangle: function (x, y, w, h) { this.x = x; this.y = y; this.width = w; this.height = h; }
            },
            util: {}
        };
        if (!String.prototype.equals) { String.prototype.equals = function (o) { return this == String(o); }; }
        if (!String.prototype.equalsIgnoreCase) { String.prototype.equalsIgnoreCase = function (o) { return this.toLowerCase() == String(o).toLowerCase(); }; }
        """;

    private sealed class CmFacade
    {
        private readonly INpcScriptContext _cm;
        private readonly Action<int> _openShop;
        private readonly Maple.Application.Events.IEventScriptHost? _events;

        public CmFacade(INpcScriptContext cm, Action<int> openShop, Maple.Application.Events.IEventScriptHost? events)
        {
            _cm = cm;
            _openShop = openShop;
            _events = events;
        }

        public void sendNext(string text) => _cm.SendNext(text);
        public void sendPrev(string text) => _cm.SendPrev(text);
        public void sendNextPrev(string text) => _cm.SendNextPrev(text);
        public void sendOk(string text) => _cm.SendOk(text);
        public void sendYesNo(string text) => _cm.SendYesNo(text);
        public void sendSimple(string text) => _cm.SendSimple(text);
        public void sendGetText(string text) => _cm.SendGetText(text);
        public void sendGetNumber(string text, int def, int min, int max) => _cm.SendGetNumber(text, def, min, max);
        public void dispose() => _cm.Dispose();
        public void warp(int mapId) => _cm.Warp(mapId);
        public void openShop(int shopOrNpcId) => _openShop(shopOrNpcId);
        public void gainMeso(int amount) => _cm.GainMeso(amount);
        public void gainItem(int itemId, int quantity) => _cm.GainItem(itemId, quantity);
        public bool haveItem(int itemId) => _cm.HaveItem(itemId);
        public void openStorage() => _cm.OpenStorage();
        public void sendStorage() => _cm.SendStorage();
        public void startQuest(int questId) => _cm.StartQuest(questId);
        public void forceStartQuest(int questId, int npcId, string? customData) => _cm.ForceStartQuest(questId, npcId, customData);
        public void forceStartQuest(int questId, string customData) => _cm.ForceStartQuest(questId, 0, customData);
        public void forceStartQuest(int questId) => _cm.ForceStartQuest(questId);
        public void forceStartQuest(string customData) => _cm.ForceStartCurrentQuest(customData);
        public void forceStartQuest() => _cm.ForceStartCurrentQuest();
        public void forceCompleteQuest() => _cm.ForceCompleteCurrentQuest();
        public void startQuest() => _cm.StartQuest(_cm.GetQuest());
        public void completeQuest() => _cm.CompleteQuest(_cm.GetQuest());
        public int getQuest() => _cm.GetQuest();
        public void completeQuest(int questId) => _cm.CompleteQuest(questId);
        public void forceCompleteQuest(int questId, int npcId) => _cm.ForceCompleteQuest(questId, npcId);
        public void forceCompleteQuest(int questId) => _cm.ForceCompleteQuest(questId);
        public int getQuestStatus(int questId) => _cm.GetQuestStatus(questId);
        public string getQuestCustomData(int questId) => _cm.GetQuestCustomData(questId);
        public void setQuestCustomData(int questId, string? customData) => _cm.SetQuestCustomData(questId, customData);
        public string getInfoQuest(int questId) => _cm.GetInfoQuest(questId);
        public void updateInfoQuest(int questId, string? data) => _cm.UpdateInfoQuest(questId, data);
        public void clearInfoQuest(int questId) => _cm.ClearInfoQuest(questId);
        public int getJob() => _cm.GetJob();
        public int getMeso() => _cm.GetMeso();
        /// <summary>P170：Java 回 MapleMap；這裡回地圖 facade（getId／getPortal…）。</summary>
        public MapFacade getMap() => new(_cm, _cm.GetMap());
        public MapFacade getMap(int mapId) => new(_cm, mapId);
        public int getBuddyCapacity() => _cm.GetBuddyCapacity();
        public void updateBuddyCapacity(int capacity) => _cm.UpdateBuddyCapacity(capacity);
        public int getPlayerStat(string type) => _cm.GetPlayerStat(type);
        public void increaseGuildCapacity() => _cm.IncreaseGuildCapacity();
        public void disbandGuild() => _cm.DisbandGuild();
        public void sendRepairWindow() => _cm.SendRepairWindow();

        // ── P166：常用 cm API ──────────────────────────────────────────────────────
        public void safeDispose() => _cm.SafeDispose();
        public void warp(int mapId, int portal) => _cm.Warp(mapId, portal);
        public bool haveItem(int itemId, int quantity) => _cm.HaveItem(itemId, quantity);
        public bool canHold() => _cm.CanHold();
        public bool canHold(int itemId) => _cm.CanHold(itemId);
        public bool canHold(int itemId, int quantity) => _cm.CanHold(itemId, quantity);
        public void removeAll(int itemId) => _cm.RemoveAll(itemId);
        public int itemQuantity(int itemId) => _cm.ItemQuantity(itemId);
        public void playerMessage(string message) => _cm.PlayerMessage(message);
        public void playerMessage(int type, string message) => _cm.PlayerMessage(type, message);
        public void gainExp(int gain) => _cm.GainExp(gain);
        public int getLevel() => _cm.GetLevel();
        public int getMapId() => _cm.GetMapId();
        public bool isQuestActive(int questId) => _cm.IsQuestActive(questId);
        public bool isQuestFinished(int questId) => _cm.IsQuestFinished(questId);
        // ── P167：轉職與對話變體 ──────────────────────────────────────────────────────
        public void changeJob(int job) => _cm.ChangeJob(job);
        public void resetStats(int str, int dex, int intel, int luk) => _cm.ResetStats(str, dex, intel, luk);
        public void teachSkill(int skillId, int level, int masterLevel) => _cm.TeachSkill(skillId, level, masterLevel);
        public void teachSkill(int skillId, int level) => _cm.TeachSkill(skillId, level);
        public void askAcceptDecline(string text) => _cm.AskAcceptDecline(text);
        public void sendAcceptDecline(string text) => _cm.AskAcceptDecline(text);
        public void askAcceptDeclineNoESC(string text) => _cm.AskAcceptDeclineNoEsc(text);
        public void sendAcceptDeclineNoESC(string text) => _cm.AskAcceptDeclineNoEsc(text);
        public void sendNextS(string text, int type) => _cm.SendNextS(text, type);
        public void sendPrevS(string text, int type) => _cm.SendPrevS(text, type);
        public void sendNextPrevS(string text, int type) => _cm.SendNextPrevS(text, type);
        public void sendOkS(string text, int type) => _cm.SendOkS(text, type);
        public void sendYesNoS(string text, int type) => _cm.SendYesNoS(text, type);
        public void sendSimpleS(string text, int type) => _cm.SendSimpleS(text, type);

        // ── P168：美容院與輸入文字 ────────────────────────────────────────────────────
        public void sendStyle(string text, int[] styles) => _cm.SendStyle(text, styles);
        public void askAvatar(string text, params int[] styles) => _cm.SendStyle(text, styles);
        public void setHair(int hair) => _cm.SetHair(hair);
        public void setFace(int face) => _cm.SetFace(face);
        public void setSkin(int color) => _cm.SetSkin(color);
        public int setAvatar(int ticket, int style) => _cm.SetAvatar(ticket, style);
        public int setRandomAvatar(int ticket, params int[] styles) => _cm.SetRandomAvatar(ticket, styles);
        public int[] getCanHair(int[] hairs) => _cm.GetCanHair(hairs).ToArray();
        public int[] getCanFace(int[] faces) => _cm.GetCanFace(faces).ToArray();
        public string getText() => _cm.GetText();

        // ── P169 ─────────────────────────────────────────────────────────────────────
        public void saveLocation(string type) => _cm.SaveLocation(type);
        public void saveReturnLocation(string type) => _cm.SaveReturnLocation(type);
        public int getSavedLocation(string type) => _cm.GetSavedLocation(type);
        public void clearSavedLocation(string type) => _cm.ClearSavedLocation(type);
        public void gainItemPeriod(int itemId, int quantity, int days) => _cm.GainItemPeriod(itemId, quantity, days);
        public long getCurrentTime() => _cm.GetCurrentTime();
        public int getPlayerCount(int mapId) => _cm.GetPlayerCount(mapId);
        public void playSound(bool broadcast, string sound) => _cm.PlaySound(broadcast, sound);
        public void showEffect(bool broadcast, string effect) => _cm.ShowEffect(broadcast, effect);
        public void worldMessage(string text) => _cm.WorldMessage(text);
        public void worldMessage(int type, string text) => _cm.WorldMessage(type, text);
        public QuestRecordFacade getQuestRecord(int questId) => new(_cm, questId);

        // ── P170：PortalPlayerInteraction ─────────────────────────────────────────────
        public void warp(int mapId, string portalName) => _cm.Warp(mapId, portalName);
        public void playPortalSE() => _cm.PlayPortalSe();
        public void openNpc(int npcId) => _cm.OpenNpc(npcId);
        public PortalRef getPortal() => new(_cm.GetPortalId(), _cm.GetPortalName());
        public void inFreeMarket() => _cm.InFreeMarket();

        // ── P171 ─────────────────────────────────────────────────────────────────────
        public void warpS(int mapId, int portal) => _cm.Warp(mapId, portal);
        public void warpS(int mapId, string portalName) => _cm.Warp(mapId, portalName);
        public void showInstruction(string message, int width, int height) => _cm.ShowInstruction(message, width, height);
        public void showWZEffect(string path) => _cm.ShowWzEffect(path);
        public void ShowWZEffect(string path) => _cm.ShowWzEffect(path);
        public void mapMessage(string message) => _cm.MapMessage(5, message);
        public void mapMessage(int type, string message) => _cm.MapMessage(type, message);
        public bool getEvanIntroState(string data) => _cm.GetEvanIntroState(data);
        public void updateEvanIntroState(string data) => _cm.UpdateEvanIntroState(data);

        // ── P173 ─────────────────────────────────────────────────────────────────────
        /// <summary>P177：交通事件（原生 C#）優先，否則交給事件腳本主機（Jint em）。</summary>
        public object? getEventManager(string name) => _cm.HasEventManager(name) ? new EventManagerFacade(_cm, name) : _events?.GetEventManager(name);
        public void changeMusic(string song) => _cm.ChangeMusic(song);
        public ClientFacade getClient() => new(_cm, _events);

        // ── P175 ─────────────────────────────────────────────────────────────────────
        public bool haveMonster(int monsterId) => _cm.HaveMonster(monsterId);
        public void killAllMob() => _cm.KillAllMob();
        public void PlayerToNpc(string text) => _cm.PlayerToNpc(text);
        public void sendNextPrevS(string text) => _cm.SendNextPrevS(text, 3);

        // ── P176：隊伍 ───────────────────────────────────────────────────────────────────
        public PartyFacade? getParty() => _cm.GetPartyState() is { } party ? new(party) : null;
        public bool isLeader() => _cm.IsLeader();
        public bool allMembersHere() => _cm.AllMembersHere();
        public bool isAllPartyMembersAllowedJob(int job) => _cm.IsAllPartyMembersAllowedJob(job);
        public void warpParty(int mapId) => _cm.WarpParty(mapId, 0);
        public void warpParty(int mapId, int portal) => _cm.WarpParty(mapId, portal);
        public void givePartyExp(int amount) => _cm.GivePartyExp(amount);
        public void givePartyItems(int itemId, int quantity) => _cm.GivePartyItems(itemId, quantity, false);
        public void givePartyItems(int itemId, int quantity, bool removeAll) => _cm.GivePartyItems(itemId, quantity, removeAll);

        public PlayerFacade getPlayer() => new(_cm, _events);
        public PlayerFacade getChar() => new(_cm, _events);

        /// <summary>P178：Java <c>cm.getEventInstance()</c>＝<c>getPlayer().getEventInstance()</c>。</summary>
        public object? getEventInstance() => _events?.GetEventInstance(_cm.GetCharacterId());

        // ── P191 ───────────────────────────────────────────────────────────────────────
        public int getBossLog(string bossId) => _cm.GetBossLog(bossId);
        public void setBossLog(string bossId) => _cm.SetBossLog(bossId);
        public int getOneTimeLog(string log) => _cm.GetOneTimeLog(log);
        public void setOneTimeLog(string log) => _cm.SetOneTimeLog(log);

        // ── P190：遠征隊 ─────────────────────────────────────────────────────────────
        public int getSquadAvailability(string type) => _cm.GetSquadAvailability(type);
        public bool registerSquad(string type, int minutes, string startText) => _cm.RegisterSquad(type, minutes, startText);
        public int isSquadLeader(string type) => _cm.IsSquadLeader(type);
        public int isSquadMember(string type) => _cm.IsSquadMember(type);
        public int addMember(string type, bool join) => _cm.AddSquadMember(type, join);
        public void banMember(string type, int pos) => _cm.BanSquadMember(type, pos);
        public void acceptMember(string type, int pos) => _cm.AcceptSquadMember(type, pos);
        public bool getSquadList(string type, int kind) => _cm.GetSquadList(type, kind);
        public SquadFacade? getSquad(string type) => _cm.GetSquad(type) is { } squad ? new(_cm, type, squad) : null;

        /// <summary>Java getDisconnected(event)：該事件中「我斷線過」且仍有玩家的實例。</summary>
        public object? getDisconnected(string eventName)
            => (_events?.GetEventManager(eventName) as EventManagerApi)?.FindDisconnectedInstance(_cm.GetCharacterId());

        /// <summary>Java reAdd(eim, squad)：斷線回來者重新加入遠征隊與事件實例。</summary>
        public bool reAdd(string eventName, string squadType)
        {
            if (getDisconnected(eventName) is not EventInstanceApi eim || _cm.GetSquad(squadType) is null)
            {
                return false;
            }

            _cm.ReAddSquadMember(squadType);
            eim.registerPlayer(new PlayerFacade(_cm, _events));
            return true;
        }
    }

    /// <summary>P176：MapleParty 子集（getId／getLeader／getMembers）。</summary>
    private sealed class PartyFacade : IScriptPartyRef
    {
        private readonly Maple.Core.Parties.PartyState _party;

        public PartyFacade(Maple.Core.Parties.PartyState party) => _party = party;

        int IScriptPartyRef.PartyId => _party.Id;

        IReadOnlyList<int> IScriptPartyRef.MemberIds => _party.Members.Select(static m => m.CharacterId).ToArray();

        public int getId() => _party.Id;

        public PartyMemberFacade? getLeader() => _party.Leader is { } leader ? new(leader) : null;

        public PartyMemberList getMembers() => new(_party.Members.Select(static m => new PartyMemberFacade(m)).ToArray());

        public PartyMemberFacade? getMemberById(int id) => _party.GetMember(id) is { } member ? new(member) : null;
    }

    /// <summary>P176：Java Collection&lt;MaplePartyCharacter&gt; 子集（size／get／iterator）。</summary>
    private sealed class PartyMemberList
    {
        private readonly PartyMemberFacade[] _members;

        public PartyMemberList(PartyMemberFacade[] members) => _members = members;

        public int size() => _members.Length;

        public bool isEmpty() => _members.Length == 0;

        public PartyMemberFacade get(int index) => _members[index];

        public MemberIterator iterator() => new(_members);
    }

    private sealed class MemberIterator
    {
        private readonly PartyMemberFacade[] _members;
        private int _next;

        public MemberIterator(PartyMemberFacade[] members) => _members = members;

        public bool hasNext() => _next < _members.Length;

        public PartyMemberFacade next() => _members[_next++];
    }

    /// <summary>P176：MaplePartyCharacter 子集。</summary>
    private sealed class PartyMemberFacade
    {
        private readonly Maple.Core.Parties.PartyMember _member;

        public PartyMemberFacade(Maple.Core.Parties.PartyMember member) => _member = member;

        public int getId() => _member.CharacterId;

        public string getName() => _member.Name;

        public int getLevel() => _member.Level;

        public int getJobId() => _member.JobId;

        public int getMapid() => _member.MapId;

        public int getChannel() => _member.ChannelIndex;

        public bool isOnline() => _member.IsOnline;
    }

    /// <summary>P173：EventManager 子集（getProperty／setProperty）。</summary>
    private sealed class EventManagerFacade
    {
        private readonly INpcScriptContext _cm;
        private readonly string _name;

        public EventManagerFacade(INpcScriptContext cm, string name) => (_cm, _name) = (cm, name);

        public string? getProperty(string key) => _cm.GetEventProperty(_name, key);

        public void setProperty(string key, string value) => _cm.SetEventProperty(_name, key, value);
    }

    /// <summary>P173：<c>getClient().getChannelServer().getEventSM().getEventManager(name)</c> 鏈（portal 腳本 OBoat1 等用）。</summary>
    private sealed class ClientFacade
    {
        private readonly INpcScriptContext _cm;
        private readonly Maple.Application.Events.IEventScriptHost? _events;

        public ClientFacade(INpcScriptContext cm, Maple.Application.Events.IEventScriptHost? events) => (_cm, _events) = (cm, events);

        public ClientFacade getChannelServer() => this;

        public ClientFacade getEventSM() => this;

        public object? getEventManager(string name) => _cm.HasEventManager(name) ? new EventManagerFacade(_cm, name) : _events?.GetEventManager(name);
    }

    /// <summary>P170：MaplePortal 子集（getId／getName）。</summary>
    private sealed class PortalRef
    {
        public PortalRef(int id, string name) => (Id, Name) = (id, name);

        public int Id { get; }

        public string Name { get; }

        public int getId() => Id;

        public string getName() => Name;
    }

    /// <summary>P170：MapleMap 子集（getId、getPortal(name|id)、getCharactersSize）。</summary>
    private sealed class MapFacade : IScriptMapRef
    {
        private readonly INpcScriptContext _cm;

        public MapFacade(INpcScriptContext cm, int mapId) => (_cm, MapId) = (cm, mapId);

        public int MapId { get; }

        public int getId() => MapId;

        public PortalRef? getPortal(string name)
            => _cm.GetMapPortalId(MapId, name) is var id and >= 0 ? new PortalRef(id, name) : null;

        public PortalRef getPortal(int id) => new(id, string.Empty);

        public int getCharactersSize() => _cm.GetPlayerCount(MapId);

        // ── P175 ─────────────────────────────────────────────────────────────────────
        public MonsterList getAllMonstersThreadsafe() => new(_cm.GetMapMonsterIds(MapId));
        public MonsterList getAllMonsters() => getAllMonstersThreadsafe();
        public MonsterRef? getMonsterById(int id) => _cm.GetMapMonsterIds(MapId).Contains(id) ? new MonsterRef(id) : null;
        public int countMonsterById(int id) => _cm.GetMapMonsterIds(MapId).Count(m => m == id);
        public int getNumMonsters() => _cm.GetMapMonsterIds(MapId).Count;
        public ReactorRef? getReactorByName(string name) => _cm.GetReactorState(MapId, name) is var state and >= 0 ? new ReactorRef(name, state) : null;
    }

    /// <summary>P175：Java List&lt;MapleMonster&gt; 子集（size／isEmpty／get）。</summary>
    private sealed class MonsterList
    {
        private readonly IReadOnlyList<int> _ids;

        public MonsterList(IReadOnlyList<int> ids) => _ids = ids;

        public int size() => _ids.Count;

        public bool isEmpty() => _ids.Count == 0;

        public MonsterRef get(int index) => new(_ids[index]);
    }

    private sealed class MonsterRef
    {
        public MonsterRef(int id) => Id = id;

        public int Id { get; }

        public int getId() => Id;
    }

    /// <summary>P175：MapleReactor 子集（getState／getName）。</summary>
    private sealed class ReactorRef
    {
        private readonly string _name;
        private readonly int _state;

        public ReactorRef(string name, int state) => (_name, _state) = (name, state);

        public int getState() => _state;

        public string getName() => _name;
    }

    /// <summary>P169：<c>cm.getQuestRecord(id)</c>（MapleQuestStatus 子集：customData／status）。</summary>
    private sealed class QuestRecordFacade
    {
        private readonly INpcScriptContext _cm;
        private readonly int _questId;

        public QuestRecordFacade(INpcScriptContext cm, int questId) => (_cm, _questId) = (cm, questId);

        public string? getCustomData()
        {
            var data = _cm.GetQuestCustomData(_questId);
            return data.Length == 0 ? null : data;
        }

        public void setCustomData(string? data) => _cm.SetQuestCustomData(_questId, data);
        public int getStatus() => _cm.GetQuestStatus(_questId);
    }

    /// <summary>P166：<c>cm.getPlayer()</c>／<c>cm.getChar()</c> 的 MapleCharacter 子集（只讀查詢＋gainAp）。</summary>
    private sealed class PlayerFacade : IScriptCharacterRef
    {
        private readonly INpcScriptContext _cm;
        private readonly Maple.Application.Events.IEventScriptHost? _events;

        public PlayerFacade(INpcScriptContext cm, Maple.Application.Events.IEventScriptHost? events = null) => (_cm, _events) = (cm, events);

        int IScriptCharacterRef.CharacterId => _cm.GetCharacterId();

        /// <summary>P178：Java <c>chr.getEventInstance()</c>。</summary>
        public object? getEventInstance() => _events?.GetEventInstance(_cm.GetCharacterId());

        // P191
        public int getBossLog(string bossId) => _cm.GetBossLog(bossId);
        public void setBossLog(string bossId) => _cm.SetBossLog(bossId);
        public int getOneTimeLog(string log) => _cm.GetOneTimeLog(log);
        public void setOneTimeLog(string log) => _cm.SetOneTimeLog(log);

        public int getId() => _cm.GetCharacterId();
        public string getName() => _cm.GetName();
        public int getLevel() => _cm.GetLevel();
        public int getJob() => _cm.GetJob();
        public int getMapId() => _cm.GetMapId();
        public int getMeso() => _cm.GetMeso();
        public int getGender() => _cm.GetGender();
        public int getFace() => _cm.GetFace();
        public int getHair() => _cm.GetHair();
        public int getFame() => _cm.GetFame();
        public bool isGM() => _cm.IsGm();
        public int getRemainingSp() => _cm.GetRemainingSp();
        public int getRemainingAp() => _cm.GetRemainingAp();
        public int getSkillLevel(int skillId) => _cm.GetSkillLevel(skillId);
        public int getMasterLevel(int skillId) => _cm.GetMasterLevel(skillId);
        public int itemQuantity(int itemId) => _cm.ItemQuantity(itemId);
        public bool haveItem(int itemId) => _cm.HaveItem(itemId);
        public bool haveItem(int itemId, int quantity) => _cm.HaveItem(itemId, quantity);
        public bool hasEquipped(int itemId) => _cm.HasEquipped(itemId);
        public void gainAp(int amount) => _cm.GainAp(amount);
        public void changeJob(int job) => _cm.ChangeJob(job);
        public MapFacade getMap() => new(_cm, _cm.GetMap());
        public ClientFacade getClient() => new(_cm, _events);
        public PartyFacade? getParty() => _cm.GetPartyState() is { } party ? new(party) : null;

        /// <summary>P170：Java <c>changeMap(MapleMap, MaplePortal)</c> → warp(map, portal id)。</summary>
        public void changeMap(MapFacade map, PortalRef? portal) => _cm.Warp(map.MapId, portal?.Id ?? 0);
        public void changeMap(MapFacade map) => _cm.Warp(map.MapId, 0);
        public void dropMessage(string message) => _cm.PlayerMessage(5, message);
        public void dropMessage(int type, string message) => _cm.PlayerMessage(type, message);
    }

    /// <summary>P190：MapleSquad 子集（排隊、名單、狀態）。</summary>
    public sealed class SquadFacade : IScriptSquadRef
    {
        private readonly INpcScriptContext _cm;
        private readonly string _type;

        internal SquadFacade(INpcScriptContext cm, string type, Maple.Core.Squads.MapleSquad squad) => (_cm, _type, Squad) = (cm, type, squad);

        public Maple.Core.Squads.MapleSquad Squad { get; }

        public string getLeaderName() => Squad.LeaderName;
        public int getStatus() => Squad.Status;
        public int getSquadSize() => Squad.Members.Count;
        public int getBannedMemberSize() => Squad.BannedMembers.Count;
        public string? getSquadMemberString(int type) => Squad.MemberString(type);
        public string getNextPlayer() => _cm.GetSquadNextPlayerString(_type);
        public void setNextPlayer(string name) => _cm.SetSquadNextPlayer(_type, name);

        /// <summary>Java 回傳 List&lt;Pair&lt;String, Long&gt;&gt;，腳本的 <c>contains(名字)</c> 恆為 false（照抄）。</summary>
        public PairList getAllNextPlayer() => new(_cm.GetSquadQueue(_type).Count);
    }

    public sealed class PairList
    {
        private readonly int _count;

        internal PairList(int count) => _count = count;

        public int size() => _count;

        public bool isEmpty() => _count == 0;

        public bool contains(object? value) => false;
    }
}
