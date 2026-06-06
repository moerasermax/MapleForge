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
        Func<int, string, CancellationToken, Task>? sendInfoQuestUpdate = null)
    {
        NpcId = npcId;
        _script = script;
        _ctx = ctx;
        _sendDialog = sendDialog;
        _warp = warp;
        _openShop = openShop;
        _openStorage = openStorage;
        _sendQuestResult = sendQuestResult;
        _sendInfoQuestUpdate = sendInfoQuestUpdate;
    }

    /// <summary>進入對話（呼叫 start() 並 flush 第一則對話）。</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        _ctx.ClearPending();
        _script.Start();
        await FlushAsync(ct);
    }

    /// <summary>玩家回應推進（呼叫 action(mode,type,selection) 並 flush 結果）。</summary>
    public async Task ContinueAsync(int mode, int type, int selection, CancellationToken ct)
    {
        if (!Active) return;
        _ctx.ClearPending();
        _script.Resume(mode, type, selection);
        await FlushAsync(ct);
    }

    private async Task FlushAsync(CancellationToken ct)
    {
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

        if (_ctx.PendingDialog is { } dialog)
            await _sendDialog(dialog, ct);

        if (_ctx.PendingShop is { } shopOrNpcId && _openShop is not null)
            await _openShop(shopOrNpcId, ct);

        if (_ctx.PendingWarp is { } mapId)
            await _warp(mapId, ct);

        if (_ctx.PendingStorageNpcId is { } npcId && _openStorage is not null)
            await _openStorage(npcId, ct);

        if (_ctx.Ended)
            Active = false;
    }
}
