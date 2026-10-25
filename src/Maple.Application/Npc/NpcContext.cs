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

    public NpcContext(int npcId, Player player, QuestService quests, Maple.Application.Stats.JobAdvancementService? jobs = null)
    {
        _npcId = npcId;
        _player = player;
        _quests = quests;
        _jobs = jobs;
    }

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
    public void GainItem(int itemId, int quantity)
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
