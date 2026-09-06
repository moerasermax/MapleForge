using Maple.Core.Inventory;
using Maple.Core.Quests;
using Maple.Core.World;
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

    public NpcContext(int npcId, Player player, QuestService quests)
    {
        _npcId = npcId;
        _player = player;
        _quests = quests;
    }

    // ── coordinator-facing（internal：不外洩給 JS）──────────────────────────────
    internal NpcDialog? PendingDialog { get; private set; }
    internal int? PendingWarp { get; private set; }
    internal int? PendingShop { get; private set; }
    internal int? PendingStorageNpcId { get; private set; }
    internal int? PendingBuddyCapacityUpdate { get; private set; }
    internal IReadOnlyList<QuestTransactionResult> PendingQuestResults => _pendingQuestResults;
    internal IReadOnlyList<(int QuestId, string Data)> PendingInfoQuestUpdates => _pendingInfoQuestUpdates;
    internal bool Ended { get; private set; }

    internal void ClearPending()
    {
        PendingDialog = null;
        PendingWarp = null;
        PendingShop = null;
        PendingStorageNpcId = null;
        PendingBuddyCapacityUpdate = null;
        _pendingQuestResults.Clear();
        _pendingInfoQuestUpdates.Clear();
    }

    // ── cm surface（暴露給腳本）────────────────────────────────────────────────
    public void SendNext(string text) => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.Next, text);
    public void SendPrev(string text) => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.Prev, text);
    public void SendNextPrev(string text) => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.NextPrev, text);
    public void SendOk(string text) => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.Ok, text);
    public void SendYesNo(string text) => PendingDialog = new NpcDialog(_npcId, NpcDialogKind.YesNo, text);
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

    public void GainMeso(int amount) => _player.GainMeso(amount);

    public void GainItem(int itemId, int quantity) => _player.GainItem(InventoryTypeOf(itemId), itemId, (short)quantity);

    public bool HaveItem(int itemId) => _player.HasItem(InventoryTypeOf(itemId), itemId);

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

    private void EnqueueQuestResult(QuestTransactionResult result) => _pendingQuestResults.Add(result);
}
