using Maple.Core.Guilds;
using Maple.Core.Inventory;
using Maple.Core.Quests;
using Maple.Core.World;
using Maple.Application.Guilds;
using Maple.Application.Quests;

namespace Maple.Application.Npcs;

/// <summary>
/// <c>cm</c> 的薄橋接實作：把腳本呼叫接到領域（<see cref="Player"/>）與待送 dialog。
/// 送對話類只記錄 <see cref="PendingDialog"/>（不在腳本同步呼叫中送包，避免 sync-over-async 死鎖）；
/// 領域類即時走 Core 富領域行為。coordinator-facing 成員為 <c>internal</c>，故 Jint 反射看不到、
/// 腳本只見 <see cref="INpcScriptContext"/> 介面方法（surface 乾淨）。
/// </summary>
public sealed class NpcContext : INpcScriptContext, INpcShopScriptContext
{
    private readonly int _npcId;
    private readonly Player _player;
    private readonly QuestService _quests;
    private readonly List<QuestTransactionResult> _pendingQuestResults = new();
    private readonly List<(int QuestId, string Data)> _pendingInfoQuestUpdates = new();
    private readonly List<(int Type, string Message)> _pendingMessages = new();

    private readonly Maple.Application.Stats.JobAdvancementService? _jobs;
    private readonly List<Maple.Application.Stats.JobChangeResult> _pendingJobChanges = new();

    private readonly Func<int, bool> _itemExists;
    private readonly Func<int, int> _playerCount;
    private readonly (string Name, int Id)? _portal;
    private readonly Maple.Application.Events.TransportService? _transport;
    private readonly int _questId;
    private readonly Func<int, IReadOnlyList<int>> _monstersOf;
    private readonly Func<int, string, int> _reactorStateOf;
    private readonly Maple.Application.Parties.IPartyRegistry? _parties;
    private readonly Func<int, IReadOnlyList<int>> _playersOnMap;
    private readonly int _expRate;
    private readonly List<NpcPartyAction> _pendingPartyActions = new();

    internal IReadOnlyList<NpcPartyAction> PendingPartyActions => _pendingPartyActions;

    /// <summary>P175：cm.killAllMob → 呼叫端清掉目前地圖所有怪（Java killAllMonsters(true)）。</summary>
    internal bool PendingKillAllMob { get; private set; }
    private readonly Func<int, string, int> _portalIdOf;

    internal string? PendingWarpPortalName { get; private set; }
    internal bool PendingPortalSe { get; private set; }
    internal int? PendingOpenNpcId { get; private set; }
    private readonly Func<int, int> _returnMapOf;
    private readonly List<(bool Broadcast, int Mode, string Name)> _pendingEnvironment = new();
    private readonly List<(int Type, string Message)> _pendingWorldMessages = new();

    internal IReadOnlyList<(bool Broadcast, int Mode, string Name)> PendingEnvironment => _pendingEnvironment;
    internal IReadOnlyList<(int Type, string Message)> PendingWorldMessages => _pendingWorldMessages;
    private readonly List<NpcUiEffect> _pendingUiEffects = new();
    internal IReadOnlyList<NpcUiEffect> PendingUiEffects => _pendingUiEffects;
    private readonly Random _random;

    public NpcContext(
        int npcId,
        Player player,
        QuestService quests,
        Maple.Application.Stats.JobAdvancementService? jobs = null,
        Func<int, bool>? itemExists = null,
        Random? random = null,
        Func<int, int>? playerCount = null,
        Func<int, int>? returnMapOf = null,
        (string Name, int Id)? portal = null,
        Func<int, string, int>? portalIdOf = null,
        Maple.Application.Events.TransportService? transport = null,
        int questId = 0,
        Func<int, IReadOnlyList<int>>? monstersOf = null,
        Func<int, string, int>? reactorStateOf = null,
        Maple.Application.Parties.IPartyRegistry? parties = null,
        Func<int, IReadOnlyList<int>>? playersOnMap = null,
        int expRate = 1)
    {
        _parties = parties;
        _playersOnMap = playersOnMap ?? (static _ => []);
        _expRate = Math.Max(1, expRate);
        _questId = questId;
        _monstersOf = monstersOf ?? (static _ => []);
        _reactorStateOf = reactorStateOf ?? (static (_, _) => -1);
        _transport = transport;
        _portal = portal;
        _portalIdOf = portalIdOf ?? (static (_, _) => -1);
        _playerCount = playerCount ?? (static _ => 0);
        _returnMapOf = returnMapOf ?? (static id => id);
        _npcId = npcId;
        _player = player;
        _quests = quests;
        _jobs = jobs;
        _itemExists = itemExists ?? (static _ => true);
        _random = random ?? Random.Shared;
    }

    /// <summary>P168：NPC_TALK_MORE 的 getText 輸入（Java cm.setGetText）。</summary>
    internal string LastText { get; set; } = string.Empty;

    internal IReadOnlyList<Maple.Application.Stats.JobChangeResult> PendingJobChanges => _pendingJobChanges;

    // ── coordinator-facing（internal：不外洩給 JS）──────────────────────────────
    internal NpcDialog? PendingDialog { get; private set; }
    internal int? PendingWarp { get; private set; }
    internal int PendingWarpPortal { get; private set; }
    internal bool PendingDisposal { get; private set; }
    internal int? PendingShop { get; private set; }
    internal int? PendingStorageNpcId { get; private set; }
    internal int? PendingBuddyCapacityUpdate { get; private set; }
    internal bool PendingGuildCapacityIncrease { get; private set; }
    internal bool PendingGuildDisband { get; private set; }
    internal int? PendingRepairWindowNpcId { get; private set; }
    internal string? PendingPopupMessage { get; private set; }
    internal IReadOnlyList<QuestTransactionResult> PendingQuestResults => _pendingQuestResults;
    internal IReadOnlyList<(int QuestId, string Data)> PendingInfoQuestUpdates => _pendingInfoQuestUpdates;
    internal IReadOnlyList<(int Type, string Message)> PendingMessages => _pendingMessages;
    internal bool Ended { get; private set; }

    internal void ClearPending()
    {
        PendingDialog = null;
        PendingWarp = null;
        PendingWarpPortal = 0;
        PendingShop = null;
        PendingStorageNpcId = null;
        PendingBuddyCapacityUpdate = null;
        PendingGuildCapacityIncrease = false;
        PendingGuildDisband = false;
        PendingRepairWindowNpcId = null;
        PendingPopupMessage = null;
        _pendingQuestResults.Clear();
        _pendingInfoQuestUpdates.Clear();
        _pendingMessages.Clear();
        _pendingJobChanges.Clear();
        _pendingEnvironment.Clear();
        PendingWarpPortalName = null;
        PendingPortalSe = false;
        PendingOpenNpcId = null;
        PendingKillAllMob = false;
        _pendingPartyActions.Clear();
        _pendingWorldMessages.Clear();
        _pendingUiEffects.Clear();
    }

    // ── cm surface（暴露給腳本）────────────────────────────────────────────────
    public void SendNext(string text) => SendS(NpcDialogKind.Next, text, 0);
    public void SendPrev(string text) => SendS(NpcDialogKind.Prev, text, 0);
    public void SendNextPrev(string text) => SendS(NpcDialogKind.NextPrev, text, 0);
    public void SendOk(string text) => SendS(NpcDialogKind.Ok, text, 0);
    public void SendYesNo(string text) => SendS(NpcDialogKind.YesNo, text, 0);

    // ── P167：S（speaker type）／接受拒絕變體；Java 一律「含 #L 改送 sendSimple(S)」避免客戶端斷線 ─────────
    public void SendNextS(string text, int type) => SendS(NpcDialogKind.Next, text, type);
    public void SendPrevS(string text, int type) => SendS(NpcDialogKind.Prev, text, type);
    public void SendNextPrevS(string text, int type) => SendS(NpcDialogKind.NextPrev, text, type);
    public void SendOkS(string text, int type) => SendS(NpcDialogKind.Ok, text, type);
    public void SendYesNoS(string text, int type) => SendS(NpcDialogKind.YesNo, text, type);
    public void SendSimpleS(string text, int type) => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.Simple, text, (byte)type);
    public void AskAcceptDecline(string text) => SendS(NpcDialogKind.AcceptDecline, text, 0);
    public void AskAcceptDeclineNoEsc(string text) => SendS(NpcDialogKind.AcceptDeclineNoEsc, text, 0);

    private void SendS(NpcDialogKind kind, string text, int type)
        => PendingDialog = text.Contains("#L", StringComparison.Ordinal)
            ? new NpcDialog(_npcId, NpcDialogKind.Simple, text, (byte)type)
            : new NpcDialog(_npcId, kind, text, (byte)type);

    // ── P168：美容院（對照 NPCConversationManager.sendStyle／askAvatar／setHair／setFace／setSkin／setAvatar／setRandomAvatar）─

    public void SendStyle(string text, IReadOnlyList<int> styles)
        => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.Style, text, Styles: styles.ToArray());

    /// <summary>Java <c>setHair</c>：hairExists 才換 → updateSingleStat(HAIR) → equipChanged。</summary>
    public void SetHair(int hair)
    {
        if (_itemExists(hair))
        {
            _player.Character.Hair = hair;
            EnqueueLook(new PlayerStatUpdate(PlayerStatKind.Hair, hair));
        }
    }

    public void SetFace(int face)
    {
        if (_itemExists(face))
        {
            _player.Character.Face = face;
            EnqueueLook(new PlayerStatUpdate(PlayerStatKind.Face, face));
        }
    }

    public void SetSkin(int color)
    {
        _player.Character.SkinColor = (byte)color;
        EnqueueLook(new PlayerStatUpdate(PlayerStatKind.Skin, color));
    }

    /// <summary>
    /// Java <c>setAvatar(ticket, args)</c>：沒有券回 -1；扣一張券 → args &lt; 100 膚色、&lt; 30000 臉（faceExists）、其餘髮型（hairExists）
    /// → equipChanged；回 1。
    /// </summary>
    public int SetAvatar(int ticket, int style)
    {
        if (!HaveItem(ticket))
        {
            return -1;
        }

        GainItem(ticket, -1);
        if (style < 100)
        {
            _player.Character.SkinColor = (byte)style;
            EnqueueLook(new PlayerStatUpdate(PlayerStatKind.Skin, style));
        }
        else if (style < 30000)
        {
            if (_itemExists(style))
            {
                _player.Character.Face = style;
                EnqueueLook(new PlayerStatUpdate(PlayerStatKind.Face, style));
            }
            else
            {
                EnqueueLook(null);
            }
        }
        else if (_itemExists(style))
        {
            _player.Character.Hair = style;
            EnqueueLook(new PlayerStatUpdate(PlayerStatKind.Hair, style));
        }
        else
        {
            EnqueueLook(null);
        }

        return 1;
    }

    /// <summary>Java <c>setRandomAvatar(ticket, args...)</c>：同 setAvatar，樣式從清單隨機挑。</summary>
    public int SetRandomAvatar(int ticket, IReadOnlyList<int> styles)
        => !HaveItem(ticket) || styles.Count == 0 ? -1 : SetAvatar(ticket, styles[_random.Next(styles.Count)]);

    /// <summary>Java <c>getCanHair</c>：濾掉客戶端沒有的髮型（管理員提示省略：MapleForge 尚無 GM）。</summary>
    public IReadOnlyList<int> GetCanHair(IReadOnlyList<int> hairs) => hairs.Where(_itemExists).ToArray();

    public IReadOnlyList<int> GetCanFace(IReadOnlyList<int> faces) => faces.Where(_itemExists).ToArray();

    public string GetText() => LastText;

    // ── P169：對照 AbstractPlayerInteraction.saveLocation／getSavedLocation／gainItemPeriod 等 ─────────────

    /// <summary>Java <c>saveLocation(type)</c>：記錄目前地圖。</summary>
    public void SaveLocation(string type) => _player.Character.SavedLocations[type] = _player.Character.MapId;

    /// <summary>Java <c>saveReturnLocation(type)</c>：記錄目前地圖的 returnMap。</summary>
    public void SaveReturnLocation(string type) => _player.Character.SavedLocations[type] = _returnMapOf(_player.Character.MapId);

    /// <summary>Java <c>getSavedLocation(type)</c>：未記錄或 -1 → 100000000（弓箭手村）。</summary>
    public int GetSavedLocation(string type)
        => _player.Character.SavedLocations.TryGetValue(type, out var mapId) && mapId != -1 ? mapId : 100000000;

    public void ClearSavedLocation(string type) => _player.Character.SavedLocations[type] = -1;

    /// <summary>Java <c>gainItemPeriod(id, qty, days)</c> → gainItem(period)：expiration = now + days 天。</summary>
    public void GainItemPeriod(int itemId, int quantity, int days)
        => GainItemCore(itemId, quantity, days > 0 ? DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeMilliseconds() : -1);

    public long GetCurrentTime() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Java <c>getPlayerCount(mapId)</c>：該地圖目前人數。</summary>
    public int GetPlayerCount(int mapId) => _playerCount(mapId);

    /// <summary>Java <c>playSound(broadcast, sound)</c> → environmentChange(sound, 4)。</summary>
    public void PlaySound(bool broadcast, string sound) => _pendingEnvironment.Add((broadcast, 4, sound));

    public void ChangeMusic(string song) => _pendingEnvironment.Add((true, 6, song));

    /// <summary>P175：Java <c>map.getAllMonstersThreadsafe()</c> 的怪物 ID（地圖未建立回空）。</summary>
    public IReadOnlyList<int> GetMapMonsterIds(int mapId) => _monstersOf(mapId);

    /// <summary>P175：Java <c>map.getReactorByName(name).getState()</c>；找不到回 -1（Java 為 null）。</summary>
    public int GetReactorState(int mapId, string name) => _reactorStateOf(mapId, name);

    /// <summary>Java <c>haveMonster(mobid)</c>：目前地圖有沒有這隻怪。</summary>
    public bool HaveMonster(int monsterId) => GetMapMonsterIds(_player.Character.MapId).Contains(monsterId);

    /// <summary>Java <c>killAllMob()</c> → <c>map.killAllMonsters(true)</c>。</summary>
    public void KillAllMob() => PendingKillAllMob = true;

    /// <summary>Java <c>PlayerToNpc(text)</c> = sendNextPrevS(text, 3)。</summary>
    public void PlayerToNpc(string text) => SendNextPrevS(text, 3);

    // ── P176：隊伍 ──────────────────────────────────────────────────────────────────────

    public Maple.Core.Parties.PartyState? GetPartyState() => _parties?.GetPartyForCharacter(_player.Character.Id);

    /// <summary>Java <c>isLeader()</c>：有隊伍且隊長是自己。</summary>
    public bool IsLeader() => GetPartyState() is { } party && party.LeaderId == _player.Character.Id;

    /// <summary>Java <c>allMembersHere()</c>：每位隊員都在目前地圖。</summary>
    public bool AllMembersHere()
    {
        if (GetPartyState() is not { } party)
        {
            return false;
        }

        var here = _playersOnMap(_player.Character.MapId);
        return party.Members.All(m => here.Contains(m.CharacterId));
    }

    /// <summary>Java <c>isAllPartyMembersAllowedJob(job)</c>：每位隊員 jobId / 100 == job。</summary>
    public bool IsAllPartyMembersAllowedJob(int job)
        => GetPartyState() is { } party && party.Members.All(m => m.JobId / 100 == job);

    /// <summary>
    /// Java <c>warpParty(mapId[, portal])</c>：沒隊伍或只有自己 → 自己 warp；否則與自己同地圖的線上隊員（含自己）都換圖（portal &lt; 0 用 0）。
    /// </summary>
    public void WarpParty(int mapId, int portal)
    {
        if (SoloOrNoParty(out var party))
        {
            Warp(mapId, Math.Max(0, portal));
            return;
        }

        var here = _playersOnMap(_player.Character.MapId);
        foreach (var member in party.Members.Where(m => here.Contains(m.CharacterId)))
        {
            _pendingPartyActions.Add(new NpcPartyAction(NpcPartyActionKind.Warp, member.CharacterId, MapId: mapId, Portal: Math.Max(0, portal)));
        }
    }

    /// <summary>Java <c>givePartyExp(amount)</c>：amount × 頻道經驗倍率；沒隊伍 → 自己；否則同地圖隊員（含自己）。</summary>
    public void GivePartyExp(int amount)
    {
        var total = amount * _expRate;
        if (SoloOrNoParty(out var party))
        {
            GainExp(total);
            return;
        }

        var here = _playersOnMap(_player.Character.MapId);
        foreach (var member in party.Members.Where(m => here.Contains(m.CharacterId)))
        {
            if (member.CharacterId == _player.Character.Id)
            {
                GainExp(total);
            }
            else
            {
                _pendingPartyActions.Add(new NpcPartyAction(NpcPartyActionKind.Exp, member.CharacterId, Amount: total));
            }
        }
    }

    /// <summary>Java <c>givePartyItems(id, quantity, removeAll)</c>：沒隊伍 → 自己（removeAll 扣光）；否則同地圖隊員各自給/扣。</summary>
    public void GivePartyItems(int itemId, int quantity, bool removeAll)
    {
        if (SoloOrNoParty(out var party))
        {
            GainItem(itemId, removeAll ? -ItemQuantity(itemId) : quantity);
            return;
        }

        var here = _playersOnMap(_player.Character.MapId);
        foreach (var member in party.Members.Where(m => here.Contains(m.CharacterId)))
        {
            if (member.CharacterId == _player.Character.Id)
            {
                GainItem(itemId, removeAll ? -ItemQuantity(itemId) : quantity);
            }
            else
            {
                _pendingPartyActions.Add(new NpcPartyAction(NpcPartyActionKind.Items, member.CharacterId, ItemId: itemId, Quantity: quantity, RemoveAll: removeAll));
            }
        }
    }

    private bool SoloOrNoParty(out Maple.Core.Parties.PartyState party)
    {
        party = GetPartyState()!;
        return party is null || party.Members.Count == 1;
    }

    /// <summary>P174：Java <c>qm.getQuest()</c>（任務腳本的 questid；NPC 對話為 0）。</summary>
    public int GetQuest() => _questId;

    /// <summary>Java <c>qm.forceStartQuest()</c>／<c>forceStartQuest(customData)</c>：作用在目前任務。</summary>
    public void ForceStartCurrentQuest(string? customData = null) => ForceStartQuest(_questId, 0, customData);

    /// <summary>Java <c>qm.forceCompleteQuest()</c>。</summary>
    public void ForceCompleteCurrentQuest() => ForceCompleteQuest(_questId);

    /// <summary>Java <c>showEffect(broadcast, effect)</c> → environmentChange(effect, 3)。</summary>
    public void ShowEffect(bool broadcast, string effect) => _pendingEnvironment.Add((broadcast, 3, effect));

    /// <summary>Java <c>worldMessage(text)</c> → getItemNotice = broadcastMessage(6, …) 全世界。</summary>
    public void WorldMessage(string text) => WorldMessage(6, text);

    public void WorldMessage(int type, string text) => _pendingWorldMessages.Add((type, text));

    // ── P170：PortalPlayerInteraction ────────────────────────────────────────────────────

    /// <summary>Java <c>warp(map, portalName)</c>：依名稱找目標地圖的 portal（呼叫端解析）。</summary>
    public void Warp(int mapId, string portalName)
    {
        Warp(mapId);
        PendingWarpPortalName = portalName;
    }

    /// <summary>Java <c>playPortalSE()</c> → showOwnBuffEffect(0, 7)。</summary>
    public void PlayPortalSe() => PendingPortalSe = true;

    /// <summary>Java <c>openNpc(id)</c>：腳本結束後開啟該 NPC 對話。</summary>
    public void OpenNpc(int npcId)
    {
        PendingOpenNpcId = npcId;
        Ended = true;
    }

    public string GetPortalName() => _portal?.Name ?? string.Empty;

    public int GetPortalId() => _portal?.Id ?? -1;

    /// <summary>Java <c>PortalPlayerInteraction.inFreeMarket</c>：10 等以上 saveLocation(FREE_MARKET) → playPortalSE → warp(910000000, "st00")。</summary>
    public void InFreeMarket()
    {
        if (_player.Character.Level >= 10)
        {
            SaveLocation("FREE_MARKET");
            PlayPortalSe();
            Warp(910000000, "st00");
        }
        else
        {
            PlayerMessage(5, "你需要10級才可以進入自由市場");
        }
    }

    // ── P171 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Java <c>showInstruction</c> → sendHint：寬 &lt; 1 用字數×10（至少 40），高 &lt; 5 用 5（由編碼端套用）。</summary>
    public void ShowInstruction(string message, int width, int height) => _pendingUiEffects.Add(new NpcUiEffect(NpcUiEffectKind.Hint, message, width, height));

    public void ShowWzEffect(string path) => _pendingUiEffects.Add(new NpcUiEffect(NpcUiEffectKind.WzEffect, path));

    public void MapMessage(int type, string message) => _pendingUiEffects.Add(new NpcUiEffect(NpcUiEffectKind.MapMessage, message, type));

    /// <summary>Java <c>getEvanIntroState(data)</c> = getInfoQuest(22013).equals(data)。</summary>
    public bool GetEvanIntroState(string data) => GetInfoQuest(22013) == data;

    public void UpdateEvanIntroState(string data) => UpdateInfoQuest(22013, data);

    /// <summary>P173：Java <c>getEventManager(name) != null</c>（目前只有交通船班事件）。</summary>
    public bool HasEventManager(string name) => _transport is not null && Maple.Application.Events.TransportService.IsKnownEvent(name);

    public string? GetEventProperty(string name, string key) => _transport?.GetProperty(name, key);

    public void SetEventProperty(string name, string key, string value) => _transport?.SetProperty(name, key, value);

    /// <summary>MapleMap.getPortal(name).getId()（找不到回 -1）。</summary>
    public int GetMapPortalId(int mapId, string portalName) => _portalIdOf(mapId, portalName);

    private void EnqueueLook(PlayerStatUpdate? update)
        => EnqueueQuestResult(new QuestTransactionResult(QuestTransactionStatus.Success)
        {
            StatUpdates = update is { } u ? [u] : [],
            LookChanged = true,
        });

    /// <summary>P167：Java <c>cm.changeJob</c> → <c>MapleCharacter.changeJob</c>（結果由呼叫端送包與廣播）。</summary>
    public void ChangeJob(int job)
    {
        if (_jobs is null)
        {
            return;
        }

        var result = _jobs.ChangeJob(_player, job);
        if (result.AbnormalLevel)
        {
            PlayerMessage(6, Maple.Application.Stats.JobAdvancementService.AbnormalLevelMessage);
        }

        _pendingJobChanges.Add(result);
    }

    /// <summary>P167：Java <c>resetStats(str, dex, int, luk)</c>（updatePlayerStats itemReaction=false）。</summary>
    public void ResetStats(int str, int dex, int intel, int luk)
        => EnqueueQuestResult(new QuestTransactionResult(QuestTransactionStatus.Success)
        {
            StatUpdates = Maple.Application.Stats.JobAdvancementService.ResetStats(_player, str, dex, intel, luk),
        });

    /// <summary>P167：Java <c>teachSkill(id, level, master)</c> → changeSkillLevel（updateSkill）。</summary>
    public void TeachSkill(int skillId, int level, int masterLevel)
    {
        _player.ChangeSkillLevel(skillId, (byte)level, (byte)masterLevel);
        EnqueueQuestResult(new QuestTransactionResult(QuestTransactionStatus.Success)
        {
            SkillChanges = [(skillId, (byte)level, (byte)masterLevel)],
        });
    }

    /// <summary>P167：Java <c>teachSkill(id, level)</c>：等級取 max(目前, level)，master = 技能最大等級。</summary>
    public void TeachSkill(int skillId, int level)
        => TeachSkill(skillId, Math.Max(level, _player.GetSkillLevel(skillId)), _jobs?.GetMaxLevel(skillId) ?? 0);
    public void SendSimple(string text) => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.Simple, text);
    public void SendGetText(string text) => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.GetText, text);

    public void SendGetNumber(string text, int def, int min, int max) =>
        PendingDialog = new NpcDialog(_npcId, NpcDialogKind.GetNumber, text, NumberDefault: def, NumberMin: min, NumberMax: max);

    public void Dispose() => Ended = true;

    public void Warp(int mapId)
    {
        PendingWarp = mapId;
        Ended = true;   // warp 後對話結束（對照 OdinMS：warp 通常緊接 dispose）
    }

    public void OpenShop(int shopOrNpcId)
    {
        PendingShop = shopOrNpcId;
        Ended = true;
    }

    /// <summary>P166：Java gainMeso(gain, show=true, enableActions=false, inChat=true)：更新楓幣並在聊天欄提示。</summary>
    public void GainMeso(int amount)
    {
        _player.GainMeso(amount);
        EnqueueQuestResult(new QuestTransactionResult(QuestTransactionStatus.Success, mesoChanged: true, meso: _player.Character.Meso)
        {
            MesoGained = amount,
        });
    }

    /// <summary>
    /// P166：Java <c>gainItem(id, quantity)</c>：quantity ≥ 0 → 背包放不下（checkSpace）則不給；否則加入。
    /// quantity &lt; 0 → 數量不足則不扣；否則 removeById。兩者成功後都送 <c>getShowItemGain(id, quantity, true)</c>。
    /// 背包封包隨 <see cref="QuestTransactionResult"/> 送出。
    /// </summary>
    public void GainItem(int itemId, int quantity) => GainItemCore(itemId, quantity, expiration: -1);

    private void GainItemCore(int itemId, int quantity, long expiration)
    {
        var type = InventoryTypeOf(itemId);
        if (quantity >= 0)
        {
            if (quantity > 0 && !CanHold(itemId, quantity))
            {
                return;
            }

            var added = _player.GainItem(type, itemId, (short)Math.Clamp(quantity, 1, short.MaxValue));
            if (added is null)
            {
                return;
            }

            if (expiration > 0)
            {
                added.Expiration = expiration;
            }

            _player.FlushInventory();
            EnqueueQuestResult(new QuestTransactionResult(QuestTransactionStatus.Success, gainedItems: [added])
            {
                ItemGainNotices = [(itemId, quantity)],
            });
            return;
        }

        if (!_player.TryTakeItemById(type, itemId, (short)Math.Clamp(-quantity, 1, short.MaxValue), out var mutations))
        {
            return;
        }

        _player.FlushInventory();
        EnqueueQuestResult(new QuestTransactionResult(QuestTransactionStatus.Success, inventoryMutations: mutations)
        {
            ItemGainNotices = [(itemId, quantity)],
        });
    }

    public bool HaveItem(int itemId) => HaveItem(itemId, 1);

    /// <summary>Java <c>haveItem(id, quantity, checkEquipped=false, greaterOrEquals=true)</c>。</summary>
    public bool HaveItem(int itemId, int quantity) => ItemQuantity(itemId) >= quantity;

    public int ItemQuantity(int itemId) => _player.Inventory.By(InventoryTypeOf(itemId)).CountById(itemId);

    /// <summary>Java <c>canHold()</c>：五個背包都有空格。</summary>
    public bool CanHold()
        => new[] { InventoryType.Equip, InventoryType.Use, InventoryType.Setup, InventoryType.Etc, InventoryType.Cash }
            .All(t => _player.Inventory.By(t).FirstFreeSlot() is not null);

    /// <summary>Java <c>canHold(itemId)</c>：該類背包有空格（MapleForge 背包不疊加，故等同 checkSpace）。</summary>
    public bool CanHold(int itemId) => _player.Inventory.By(InventoryTypeOf(itemId)).FirstFreeSlot() is not null;

    public bool CanHold(int itemId, int quantity) => CanHold(itemId);

    /// <summary>Java <c>removeAll(id, show=true)</c>：清掉背包裡該道具全部數量並提示。</summary>
    public void RemoveAll(int itemId)
    {
        var count = ItemQuantity(itemId);
        if (count > 0)
        {
            GainItem(itemId, -count);
        }
    }

    /// <summary>Java <c>playerMessage(msg)</c> = <c>broadcastMessage(5, msg)</c>。</summary>
    public void PlayerMessage(string message) => PlayerMessage(5, message);

    public void PlayerMessage(int type, string message) => _pendingMessages.Add((type, message));

    public void GainExp(int gain) => EnqueueQuestResult(_quests.GrantExp(_player, gain));

    public int GetLevel() => _player.Character.Level;

    public int GetMapId() => _player.Character.MapId;

    public bool IsQuestActive(int questId) => _player.GetQuestStatus(questId) == (byte)QuestStatus.Started;

    public bool IsQuestFinished(int questId) => _player.GetQuestStatus(questId) == (byte)QuestStatus.Completed;

    /// <summary>Java <c>safeDispose()</c>：pendingDisposal——本輪對話照送，下一次回應時直接結束。</summary>
    public void SafeDispose() => PendingDisposal = true;

    public void Warp(int mapId, int portal)
    {
        Warp(mapId);
        PendingWarpPortal = portal;
    }

    public int GetCharacterId() => _player.Character.Id;
    public string GetName() => _player.Character.Name;
    public int GetGender() => _player.Character.Gender;
    public int GetFace() => _player.Character.Face;
    public int GetHair() => _player.Character.Hair;
    public int GetFame() => _player.Character.Fame;
    public bool IsGm() => false; // MapleForge 尚無 GM 系統（同 getPlayerStat("GM")）
    public int GetRemainingSp() => _player.Character.RemainingSp;
    public int GetRemainingAp() => _player.Character.RemainingAp;
    public int GetSkillLevel(int skillId) => _player.GetSkillLevel(skillId);
    public int GetMasterLevel(int skillId) => _player.GetMasterLevel(skillId);
    public bool HasEquipped(int itemId) => _player.Character.Equips.Any(e => e.ItemId == itemId);

    /// <summary>Java <c>gainAp(ap)</c>：remainingAp += ap → updateSingleStat(AVAILABLEAP)。</summary>
    public void GainAp(int amount)
    {
        _player.Character.RemainingAp = (short)(_player.Character.RemainingAp + amount);
        EnqueueQuestResult(new QuestTransactionResult(QuestTransactionStatus.Success)
        {
            StatUpdates = [new PlayerStatUpdate(PlayerStatKind.AvailableAp, _player.Character.RemainingAp)],
        });
    }

    public void OpenStorage()
    {
        PendingStorageNpcId = _npcId;
        Ended = true;
    }

    public void SendStorage() => OpenStorage();

    public void StartQuest(int questId) =>
        EnqueueQuestResult(_quests.StartQuest(_player, questId, _npcId));

    public void ForceStartQuest(int questId, int npcId = 0, string? customData = null) =>
        EnqueueQuestResult(_quests.ForceStartQuest(_player, questId, npcId == 0 ? _npcId : npcId, customData));

    public void CompleteQuest(int questId) =>
        EnqueueQuestResult(_quests.CompleteQuest(_player, questId, _npcId));

    public void ForceCompleteQuest(int questId, int npcId = 0) =>
        EnqueueQuestResult(_quests.ForceCompleteQuest(_player, questId, npcId == 0 ? _npcId : npcId));

    public int GetQuestStatus(int questId) => _player.GetQuestStatus(questId);

    public string GetQuestCustomData(int questId) => _player.GetQuest(questId).CustomData ?? string.Empty;

    public void SetQuestCustomData(int questId, string? customData)
    {
        var quest = _player.GetOrAddQuest(questId);
        quest.CustomData = customData;
        if (quest.Status == (byte)QuestStatus.Started)
        {
            EnqueueQuestResult(new QuestTransactionResult(QuestTransactionStatus.Success, quest));
        }
    }

    public string GetInfoQuest(int questId) => _player.GetInfoQuest(questId);

    public void UpdateInfoQuest(int questId, string? data)
    {
        var value = data ?? string.Empty;
        _player.UpdateInfoQuest(questId, value);
        _pendingInfoQuestUpdates.Add((questId, value));
    }

    public void ClearInfoQuest(int questId)
    {
        _player.ClearInfoQuest(questId);
        _pendingInfoQuestUpdates.Add((questId, string.Empty));
    }

    /// <summary>itemId 前綴判背包類型（1xxxxxx=Equip…5xxxxxx=Cash，對照 GameConstants.getInventoryType）。</summary>
    private static InventoryType InventoryTypeOf(int itemId) => Player.InventoryTypeOf(itemId);

    public int GetJob() => _player.Character.Job;
    public int GetMeso() => _player.Character.Meso;
    public int GetMap() => _player.Character.MapId;

    public int GetBuddyCapacity() => _player.BuddyList.Capacity;

    public void UpdateBuddyCapacity(int capacity)
    {
        _player.BuddyList.Capacity = (byte)capacity;
        PendingBuddyCapacityUpdate = capacity;
    }

    public int GetPlayerStat(string type)
    {
        var character = _player.Character;
        return type switch
        {
            "LVL" => character.Level,
            "STR" => character.Stats.Str,
            "DEX" => character.Stats.Dex,
            "INT" => character.Stats.Int,
            "LUK" => character.Stats.Luk,
            "HP" => character.Stats.Hp,
            "MP" => character.Stats.Mp,
            "MAXHP" => character.Stats.MaxHp,
            "MAXMP" => character.Stats.MaxMp,
            "RAP" => character.RemainingAp,
            "RSP" => character.RemainingSp,
            "GID" => character.GuildId,
            "GRANK" => character.GuildRank,
            "ARANK" => character.AllianceRank,
            "GM" or "ADMIN" => 0, // MapleForge 尚無 GM 系統，無人具 GM 身分
            "GENDER" => character.Gender,
            "FACE" => character.Face,
            "HAIR" => character.Hair,
            _ => -1,
        };
    }

    public void IncreaseGuildCapacity()
    {
        if (_player.Character.Meso < GuildService.IncreaseCapacityCost)
        {
            PendingPopupMessage = "金錢不足25萬.";
            return;
        }

        if (_player.Character.GuildId <= 0)
        {
            return; // 對照 Java：無公會靜默返回，不送任何封包
        }

        PendingGuildCapacityIncrease = true;
    }

    public void DisbandGuild()
    {
        if (_player.Character.GuildId <= 0 || _player.Character.GuildRank != Guild.LeaderRank)
        {
            return; // 對照 Java：非會長或無公會靜默返回，不送任何封包
        }

        PendingGuildDisband = true;
    }

    public void SendRepairWindow() => PendingRepairWindowNpcId = _npcId;

    private void EnqueueQuestResult(QuestTransactionResult result) => _pendingQuestResults.Add(result);
}
