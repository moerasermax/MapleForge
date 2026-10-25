using Maple.Application.Quests;

namespace Maple.Application.Npcs;

/// <summary>
/// session-local 對話 handle（**不進 process-wide registry**——對話無跨 session reader，
/// registry 純屬幽靈洩漏面，見任務歷程 12 / 架構風險#2）。由 Adapters handler 持有一個欄位，
/// 在 NPC_TALK 建立、NPC_TALK_MORE 推進、連線 finally 收掉。
///
/// queue-and-flush：驅動腳本（同步 Start/Resume）→ 讀 <see cref="NpcContext"/> 待送內容 → await flush
/// （送對話封包 / warp）。腳本同步呼叫中不碰 async，故無 sync-over-async 死鎖。
/// </summary>
public sealed class NpcConversation
{
    private readonly INpcScript _script;
    private readonly NpcContext _ctx;
    private readonly Func<NpcDialog, CancellationToken, Task> _sendDialog;
    private readonly Func<int, CancellationToken, Task> _warp;
    private readonly Func<int, CancellationToken, Task>? _openShop;
    private readonly Func<int, CancellationToken, Task>? _openStorage;
    private readonly Func<QuestTransactionResult, CancellationToken, Task>? _sendQuestResult;
    private readonly Func<int, string, CancellationToken, Task>? _sendInfoQuestUpdate;
    private readonly Func<int, CancellationToken, Task>? _sendBuddyCapacity;
    private readonly Func<CancellationToken, Task>? _increaseGuildCapacity;
    private readonly Func<string, CancellationToken, Task>? _sendPopupMessage;
    private readonly Func<CancellationToken, Task>? _disbandGuild;
    private readonly Func<int, CancellationToken, Task>? _sendRepairWindow;
    private readonly Func<int, int, CancellationToken, Task>? _warpToPortal;
    private readonly Func<int, string, CancellationToken, Task>? _sendMessage;
    private readonly Func<Maple.Application.Stats.JobChangeResult, CancellationToken, Task>? _sendJobChange;
    private readonly Func<bool, int, string, CancellationToken, Task>? _sendEnvironment;
    private readonly Func<int, string, CancellationToken, Task>? _sendWorldMessage;
    private readonly Func<CancellationToken, Task>? _playPortalSe;
    private readonly Func<NpcUiEffect, CancellationToken, Task>? _sendUiEffect;
    private readonly Func<CancellationToken, Task>? _killAllMob;
    private readonly Func<NpcPartyAction, CancellationToken, Task>? _sendPartyAction;
    private readonly Func<Maple.Application.Squads.SquadNotice, CancellationToken, Task>? _sendSquadNotice;
    private readonly Func<Maple.Core.Inventory.InventoryType, byte, CancellationToken, Task>? _sendSlotUpdate;

    public int NpcId { get; }
    public bool Active { get; private set; } = true;

    public NpcConversation(
        int npcId,
        INpcScript script,
        NpcContext ctx,
        Func<NpcDialog, CancellationToken, Task> sendDialog,
        Func<int, CancellationToken, Task> warp,
        Func<int, CancellationToken, Task>? openShop = null,
        Func<int, CancellationToken, Task>? openStorage = null,
        Func<QuestTransactionResult, CancellationToken, Task>? sendQuestResult = null,
        Func<int, string, CancellationToken, Task>? sendInfoQuestUpdate = null,
        Func<int, CancellationToken, Task>? sendBuddyCapacity = null,
        Func<CancellationToken, Task>? increaseGuildCapacity = null,
        Func<string, CancellationToken, Task>? sendPopupMessage = null,
        Func<CancellationToken, Task>? disbandGuild = null,
        Func<int, CancellationToken, Task>? sendRepairWindow = null,
        Func<int, int, CancellationToken, Task>? warpToPortal = null,
        Func<int, string, CancellationToken, Task>? sendMessage = null,
        Func<Maple.Application.Stats.JobChangeResult, CancellationToken, Task>? sendJobChange = null,
        Func<bool, int, string, CancellationToken, Task>? sendEnvironment = null,
        Func<int, string, CancellationToken, Task>? sendWorldMessage = null,
        Func<CancellationToken, Task>? playPortalSe = null,
        Func<NpcUiEffect, CancellationToken, Task>? sendUiEffect = null,
        Func<CancellationToken, Task>? killAllMob = null,
        Func<NpcPartyAction, CancellationToken, Task>? sendPartyAction = null,
        Func<Maple.Application.Squads.SquadNotice, CancellationToken, Task>? sendSquadNotice = null,
        Func<Maple.Core.Inventory.InventoryType, byte, CancellationToken, Task>? sendSlotUpdate = null)
    {
        _sendSlotUpdate = sendSlotUpdate;
        _sendSquadNotice = sendSquadNotice;
        _sendPartyAction = sendPartyAction;
        _killAllMob = killAllMob;
        _sendUiEffect = sendUiEffect;
        _playPortalSe = playPortalSe;
        _sendEnvironment = sendEnvironment;
        _sendWorldMessage = sendWorldMessage;
        _sendJobChange = sendJobChange;
        _warpToPortal = warpToPortal;
        _sendMessage = sendMessage;
        NpcId = npcId;
        _script = script;
        _ctx = ctx;
        _sendDialog = sendDialog;
        _warp = warp;
        _openShop = openShop;
        _openStorage = openStorage;
        _sendQuestResult = sendQuestResult;
        _sendInfoQuestUpdate = sendInfoQuestUpdate;
        _sendBuddyCapacity = sendBuddyCapacity;
        _increaseGuildCapacity = increaseGuildCapacity;
        _sendPopupMessage = sendPopupMessage;
        _disbandGuild = disbandGuild;
        _sendRepairWindow = sendRepairWindow;
    }

    /// <summary>進入對話（呼叫 start() 並 flush 第一則對話）。</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _ctx.ClearPending();
        _script.Start();
        await FlushAsync(ct);
    }

    /// <summary>P168：getText 對話的回應（Java setGetText 後再 action）。</summary>
    public Task ContinueWithTextAsync(int mode, int type, string text, CancellationToken ct)
    {
        _ctx.LastText = text;
        return ContinueAsync(mode, type, -1, ct);
    }

    /// <summary>玩家回應推進（呼叫 action(mode,type,selection) 並 flush 結果）。</summary>
    public async Task ContinueAsync(int mode, int type, int selection, CancellationToken ct)
    {
        if (!Active) return;

        // P168：Java NPCScriptManager.action 只在 mode != -1 時呼叫腳本；NPCMoreTalk 的 ESC 走 dispose。
        if (mode == -1)
        {
            Active = false;
            return;
        }

        // P166：Java safeDispose 的 pendingDisposal：下一次回應直接結束對話，不再呼叫腳本。
        if (_ctx.PendingDisposal)
        {
            Active = false;
            return;
        }

        _ctx.ClearPending();
        _script.Resume(mode, type, selection);
        await FlushAsync(ct);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        if (_sendJobChange is not null)
        {
            foreach (var change in _ctx.PendingJobChanges)
            {
                await _sendJobChange(change, ct);
            }
        }

        if (_sendQuestResult is not null)
        {
            foreach (var result in _ctx.PendingQuestResults)
            {
                await _sendQuestResult(result, ct);
            }
        }

        if (_sendInfoQuestUpdate is not null)
        {
            foreach (var (questId, data) in _ctx.PendingInfoQuestUpdates)
            {
                await _sendInfoQuestUpdate(questId, data, ct);
            }
        }

        if (_sendMessage is not null)
        {
            foreach (var (type, message) in _ctx.PendingMessages)
            {
                await _sendMessage(type, message, ct);
            }
        }

        if (_sendEnvironment is not null)
        {
            foreach (var (broadcast, mode, name) in _ctx.PendingEnvironment)
            {
                await _sendEnvironment(broadcast, mode, name, ct);
            }
        }

        if (_sendWorldMessage is not null)
        {
            foreach (var (type, message) in _ctx.PendingWorldMessages)
            {
                await _sendWorldMessage(type, message, ct);
            }
        }

        if (_sendUiEffect is not null)
        {
            foreach (var effect in _ctx.PendingUiEffects)
            {
                await _sendUiEffect(effect, ct);
            }
        }

        if (_ctx.PendingDialog is { } dialog)
            await _sendDialog(dialog, ct);

        if (_ctx.PendingShop is { } shopOrNpcId && _openShop is not null)
            await _openShop(shopOrNpcId, ct);

        if (_sendPartyAction is not null)
        {
            foreach (var action in _ctx.PendingPartyActions)
            {
                await _sendPartyAction(action, ct);
            }
        }

        if (_sendSlotUpdate is not null)
        {
            foreach (var (type, slots) in _ctx.PendingSlotUpdates)
            {
                await _sendSlotUpdate(type, slots, ct);
            }
        }

        if (_sendSquadNotice is not null)
        {
            foreach (var notice in _ctx.PendingSquadNotices)
            {
                await _sendSquadNotice(notice, ct);
            }
        }

        if (_ctx.PendingKillAllMob && _killAllMob is not null)
            await _killAllMob(ct);

        if (_ctx.PendingPortalSe && _playPortalSe is not null)
            await _playPortalSe(ct);

        if (_ctx.PendingWarp is { } mapId)
        {
            var namedPortal = _ctx.PendingWarpPortalName is { } portalName ? _ctx.GetMapPortalId(mapId, portalName) : -1;
            if (namedPortal > 0 && _warpToPortal is not null)
                await _warpToPortal(mapId, namedPortal, ct);
            else if (_ctx.PendingWarpPortal != 0 && _warpToPortal is not null)
                await _warpToPortal(mapId, _ctx.PendingWarpPortal, ct);
            else
                await _warp(mapId, ct);
        }

        if (_ctx.PendingStorageNpcId is { } npcId && _openStorage is not null)
            await _openStorage(npcId, ct);

        if (_ctx.PendingBuddyCapacityUpdate is { } capacity && _sendBuddyCapacity is not null)
            await _sendBuddyCapacity(capacity, ct);

        if (_ctx.PendingGuildCapacityIncrease && _increaseGuildCapacity is not null)
            await _increaseGuildCapacity(ct);

        if (_ctx.PendingPopupMessage is { } popup && _sendPopupMessage is not null)
            await _sendPopupMessage(popup, ct);

        if (_ctx.PendingGuildDisband && _disbandGuild is not null)
            await _disbandGuild(ct);

        if (_ctx.PendingRepairWindowNpcId is { } repairNpcId && _sendRepairWindow is not null)
            await _sendRepairWindow(repairNpcId, ct);

        if (_ctx.Ended)
            Active = false;
    }

    /// <summary>P170：portal 腳本 <c>pi.openNpc(id)</c> 要開的 NPC（呼叫端在 flush 後啟動新對話）。</summary>
    public int? PendingOpenNpcId => _ctx.PendingOpenNpcId;
}
