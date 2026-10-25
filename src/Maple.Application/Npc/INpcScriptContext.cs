namespace Maple.Application.Npcs;

/// <summary>
/// 暴露給 NPC 腳本的 <c>cm</c> 介面（surface）。腳本以 camelCase 呼叫（Jint 命名策略對應 PascalCase）。
/// **這只是能力邊界**——真正擋住腳本亂動 server 的牆是 Jint sandbox（CLR 關閉）。
/// 送對話類（Send*）只記錄待送 dialog（不在腳本同步呼叫中送包）；領域類（GainMeso/Warp）即時委派。
/// MVP 範圍：純對話 + meso + warp；quest/shop/storage 等先排除（見任務歷程 12）。
/// </summary>
public interface INpcScriptContext
{
    void SendNext(string text);
    void SendPrev(string text);
    void SendNextPrev(string text);
    void SendOk(string text);
    void SendYesNo(string text);
    void SendSimple(string text);
    void SendGetText(string text);
    void SendGetNumber(string text, int def, int min, int max);

    /// <summary>結束對話（cm.dispose）。</summary>
    void Dispose();

    /// <summary>傳送玩家到指定地圖（cm.warp）。</summary>
    void Warp(int mapId);

    /// <summary>增減楓幣（cm.gainMeso）。</summary>
    void GainMeso(int amount);

    /// <summary>取得道具到背包（cm.gainItem，依 itemId 自動判背包類型）。</summary>
    void GainItem(int itemId, int quantity);

    /// <summary>背包是否持有道具（cm.haveItem）。</summary>
    bool HaveItem(int itemId);

    /// <summary>開啟帳號倉庫（cm.openStorage）。</summary>
    void OpenStorage();

    /// <summary>舊 OdinMS 腳本常用名稱：cm.sendStorage。</summary>
    void SendStorage();

    void StartQuest(int questId);
    void ForceStartQuest(int questId, int npcId = 0, string? customData = null);
    void CompleteQuest(int questId);
    void ForceCompleteQuest(int questId, int npcId = 0);
    int GetQuestStatus(int questId);
    string GetQuestCustomData(int questId);
    void SetQuestCustomData(int questId, string? customData);
    string GetInfoQuest(int questId);
    void UpdateInfoQuest(int questId, string? data);
    void ClearInfoQuest(int questId);

    int GetJob();
    int GetMeso();
    int GetMap();

    /// <summary>取得好友名單容量（cm.getBuddyCapacity）。</summary>
    int GetBuddyCapacity();

    /// <summary>擴充好友名單容量並即時通知客戶端（cm.updateBuddyCapacity）。</summary>
    void UpdateBuddyCapacity(int capacity);

    /// <summary>
    /// 通用角色數值查詢（cm.getPlayerStat，對照 Java AbstractPlayerInteraction.getPlayerStat）。
    /// 支援 LVL/STR/DEX/INT/LUK/HP/MP/MAXHP/MAXMP/RAP/RSP/GID/GRANK/ARANK/GM/ADMIN/GENDER/FACE/HAIR，
    /// 未知 type 回傳 -1（同 Java）。GM/ADMIN：MapleForge 尚無 GM 系統，固定回 0（無人具 GM 身分，非造假）。
    /// </summary>
    int GetPlayerStat(string type);

    /// <summary>
    /// 擴充公會人數（cm.increaseGuildCapacity）。對照 Java
    /// <c>NPCConversationManager.increaseGuildCapacity</c>：meso 不足彈出粉紅字提示；
    /// 有公會才會真的送出擴充請求（送出後楓幣無條件扣，即使已達上限也扣，見 <c>GuildService</c> 註解）。
    /// </summary>
    void IncreaseGuildCapacity();

    /// <summary>
    /// 解散公會（cm.disbandGuild）。對照 Java <c>NPCConversationManager.disbandGuild</c>：
    /// 非會長或無公會靜默返回，不送任何封包。
    /// </summary>
    void DisbandGuild();

    /// <summary>
    /// 開啟修理視窗（cm.sendRepairWindow）。對照 Java
    /// <c>NPCConversationManager.sendRepairWindow</c>：純粹送出視窗封包，腳本自行接著
    /// 呼叫 cm.dispose() 結束對話（不像 cm.openStorage 那樣自動結束）。
    /// </summary>
    void SendRepairWindow();

    // ── P166：常用 cm API（對照 AbstractPlayerInteraction／NPCConversationManager）────────────
    void SafeDispose();
    void Warp(int mapId, int portal);
    bool HaveItem(int itemId, int quantity);
    bool CanHold();
    bool CanHold(int itemId);
    bool CanHold(int itemId, int quantity);
    void RemoveAll(int itemId);
    int ItemQuantity(int itemId);
    void PlayerMessage(string message);
    void PlayerMessage(int type, string message);
    void GainExp(int gain);
    int GetLevel();
    int GetMapId();
    bool IsQuestActive(int questId);
    bool IsQuestFinished(int questId);

    // ── P166：cm.getPlayer()／cm.getChar() 的角色查詢 ───────────────────────────────────────
    int GetCharacterId();
    string GetName();
    int GetGender();
    int GetFace();
    int GetHair();
    int GetFame();
    bool IsGm();
    int GetRemainingSp();
    int GetRemainingAp();
    int GetSkillLevel(int skillId);
    int GetMasterLevel(int skillId);
    bool HasEquipped(int itemId);
    void GainAp(int amount);

    // ── P167：轉職與技能 ─────────────────────────────────────────────────────────────────
    void ChangeJob(int job);
    void ResetStats(int str, int dex, int intel, int luk);
    void TeachSkill(int skillId, int level, int masterLevel);
    void TeachSkill(int skillId, int level);
    void AskAcceptDecline(string text);
    void AskAcceptDeclineNoEsc(string text);
    void SendNextS(string text, int type);
    void SendPrevS(string text, int type);
    void SendNextPrevS(string text, int type);
    void SendOkS(string text, int type);
    void SendYesNoS(string text, int type);
    void SendSimpleS(string text, int type);

    // ── P168：美容院與輸入文字 ───────────────────────────────────────────────────────────
    void SendStyle(string text, IReadOnlyList<int> styles);
    void SetHair(int hair);
    void SetFace(int face);
    void SetSkin(int color);
    int SetAvatar(int ticket, int style);
    int SetRandomAvatar(int ticket, IReadOnlyList<int> styles);
    IReadOnlyList<int> GetCanHair(IReadOnlyList<int> hairs);
    IReadOnlyList<int> GetCanFace(IReadOnlyList<int> faces);
    string GetText();

    // ── P169：位置記錄、定時道具、環境效果、世界訊息 ─────────────────────────────────────
    void SaveLocation(string type);
    void SaveReturnLocation(string type);
    int GetSavedLocation(string type);
    void ClearSavedLocation(string type);
    void GainItemPeriod(int itemId, int quantity, int days);
    long GetCurrentTime();
    int GetPlayerCount(int mapId);
    void PlaySound(bool broadcast, string sound);
    void ShowEffect(bool broadcast, string effect);
    void WorldMessage(string text);
    void WorldMessage(int type, string text);

    // ── P170：portal 腳本（PortalPlayerInteraction）與換圖 ─────────────────────────────────
    void Warp(int mapId, string portalName);
    void PlayPortalSe();
    void OpenNpc(int npcId);
    string GetPortalName();
    int GetPortalId();
    void InFreeMarket();
    int GetMapPortalId(int mapId, string portalName);

    // ── P171 ─────────────────────────────────────────────────────────────────────────────
    void ShowInstruction(string message, int width, int height);
    void ShowWzEffect(string path);
    void MapMessage(int type, string message);
    bool GetEvanIntroState(string data);
    void UpdateEvanIntroState(string data);

    // ── P173：cm.getEventManager(name)（目前只有交通船班的屬性）────────────────────────────
    bool HasEventManager(string name);
    string? GetEventProperty(string name, string key);
    void SetEventProperty(string name, string key, string value);

    /// <summary>Java <c>changeMusic(song)</c> → 全地圖 musicChange（environmentChange mode 6）。</summary>
    void ChangeMusic(string song);

    // ── P174：任務腳本 qm（NPCConversationManager 的 questid）───────────────────────────────
    int GetQuest();
    void ForceStartCurrentQuest(string? customData = null);
    void ForceCompleteCurrentQuest();

    // ── P175：地圖怪物／反應堆查詢與清怪 ───────────────────────────────────────────────────
    IReadOnlyList<int> GetMapMonsterIds(int mapId);
    int GetReactorState(int mapId, string name);
    bool HaveMonster(int monsterId);
    void KillAllMob();
    void PlayerToNpc(string text);

    // ── P176：隊伍（AbstractPlayerInteraction.getParty／isLeader／warpParty／givePartyExp／givePartyItems）──────
    Maple.Core.Parties.PartyState? GetPartyState();

    // ── P192 ────────────────────────────────────────────────────────────────────────
    /// <summary>Java expandInventory(type, amount)：欄位上限 +amount（≤ 96）並送 getSlotUpdate。</summary>
    void ExpandInventory(int type, int amount) { }
    /// <summary>Java getInventory(type)：唯讀背包（腳本端 facade 包裝）。</summary>
    Maple.Core.Inventory.Inventory? GetInventory(int type) => null;
    void SetQuestRecord(int questId, string data) { }
    void GainExpR(int gain) { }
    int GetMonsterCount(int mapId) => 0;

    // ── P199：雜項 ──────────────────────────────────────────────────────────────────
    void RemoveSlot(int inventoryType, int slot, int quantity) { }
    void AddHp(int delta) { }
    bool IsCash(int itemId) => false;
    void ResetMap(int mapId) { }
    int PartyMembersInMap() => 0;
    void SetPartyBossLog(string bossId) { }
    int GetChannelNumber() => 1;

    // ── P197 ────────────────────────────────────────────────────────────────────────
    void GainPet(int itemId, string name, int level, int closeness, int fullness) { }

    // ── P196：公會 GP ──────────────────────────────────────────────────────────────────
    int GetGuildId() => 0;
    void GainGuildPoints(int amount) { }
    int GetGuildPoints() => 0;
    void GenericGuildMessage(int code) { }

    // ── P195 ────────────────────────────────────────────────────────────────────────
    int GetNpcId() => 0;
    /// <summary>Java warpBack(mid, retmap, time)：傳到 mid 並開 time 秒計時，到時回 retmap。</summary>
    void WarpBack(int mapId, int returnMapId, int seconds) { }

    // ── P194：教學 UI ────────────────────────────────────────────────────────────────
    void SummonMessage(string message) { }
    void SummonMessage(int type) { }
    void PlayerSummonHint(bool summon) { }
    void AranTutorialBalloon(string data) { }
    void EvanTutorial(string data) { }
    void MovieClipIntroUi(bool enabled) { }
    void ShowMapEffect(string path) { }
    void EnvironmentChange(bool broadcast, string env) { }

    // ── P193 ────────────────────────────────────────────────────────────────────────
    int GainGachaponItem(int itemId, int quantity, string? message) => -1;
    string? GetItemName(int itemId) => null;
    void SpawnMonster(int monsterId, int quantity, int? x, int? y, int? mapId) { }
    void WarpMap(int mapId, string portal) { }

    // ── P191：打王／一次性紀錄 ─────────────────────────────────────────────────────────
    int GetBossLog(string bossId) => 0;
    void SetBossLog(string bossId) { }
    int GetOneTimeLog(string log) => 0;
    void SetOneTimeLog(string log) { }

    // ── P190：遠征隊（Java NPCConversationManager squad API）──────────────────────────────
    int GetSquadAvailability(string type) => -1;
    bool RegisterSquad(string type, int minutes, string startText) => false;
    int IsSquadLeader(string type) => -1;
    int IsSquadMember(string type) => -1;
    int AddSquadMember(string type, bool join) => -1;
    void BanSquadMember(string type, int pos) { }
    void AcceptSquadMember(string type, int pos) { }
    bool GetSquadList(string type, int kind) => false;
    Maple.Core.Squads.MapleSquad? GetSquad(string type) => null;
    void ReAddSquadMember(string type) { }
    string GetSquadNextPlayerString(string type) => string.Empty;
    void SetSquadNextPlayer(string type, string name) { }
    IReadOnlyList<string> GetSquadQueue(string type) => [];
    bool IsLeader();
    bool AllMembersHere();
    bool IsAllPartyMembersAllowedJob(int job);
    void WarpParty(int mapId, int portal);
    void GivePartyExp(int amount);
    void GivePartyItems(int itemId, int quantity, bool removeAll);
}
