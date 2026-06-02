using Maple.Core.World;

namespace Maple.Application.Npcs;

/// <summary>
/// <c>cm</c> 的薄橋接實作：把腳本呼叫接到領域（<see cref="Player"/>）與待送 dialog。
/// 送對話類只記錄 <see cref="PendingDialog"/>（不在腳本同步呼叫中送包，避免 sync-over-async 死鎖）；
/// 領域類即時走 Core 富領域行為。coordinator-facing 成員為 <c>internal</c>，故 Jint 反射看不到、
/// 腳本只見 <see cref="INpcScriptContext"/> 介面方法（surface 乾淨）。
/// </summary>
public sealed class NpcContext : INpcScriptContext
{
    private readonly int _npcId;
    private readonly Player _player;

    public NpcContext(int npcId, Player player)
    {
        _npcId = npcId;
        _player = player;
    }

    // ── coordinator-facing（internal：不外洩給 JS）──────────────────────────────
    internal NpcDialog? PendingDialog { get; private set; }
    internal int? PendingWarp { get; private set; }
    internal bool Ended { get; private set; }

    internal void ClearPending()
    {
        PendingDialog = null;
        PendingWarp = null;
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

    public void GainMeso(int amount) => _player.GainMeso(amount);

    public int GetJob() => _player.Character.Job;
    public int GetMeso() => _player.Character.Meso;
    public int GetMap() => _player.Character.MapId;
}
