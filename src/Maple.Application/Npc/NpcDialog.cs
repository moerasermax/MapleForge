namespace Maple.Application.Npcs;

/// <summary>
/// NPC 對話 UI 原語的種類（跨版本語意，不講 wire bytes）。對應 OdinMS cm.send* 家族。
/// 由 Adapters 的 encoder 映射成 v113 getNPCTalk 的 msgType + 按鈕旗標。
/// </summary>
public enum NpcDialogKind
{
    /// <summary>單一「確定」鈕（sendOk）。</summary>
    Ok,
    /// <summary>只有「下一步」鈕（sendNext）。</summary>
    Next,
    /// <summary>只有「上一步」鈕（sendPrev）。</summary>
    Prev,
    /// <summary>「上一步」+「下一步」鈕（sendNextPrev）。</summary>
    NextPrev,
    /// <summary>是／否（sendYesNo）。</summary>
    YesNo,
    /// <summary>選單（#L..#l 標記，sendSimple）。</summary>
    Simple,
    /// <summary>輸入文字（sendGetText）。</summary>
    GetText,
    /// <summary>輸入數字（sendGetNumber，含預設/最小/最大）。</summary>
    GetNumber,
}

/// <summary>
/// 一則待送的 NPC 對話（版本無關 DTO）。由 <see cref="NpcContext"/> 在腳本同步呼叫中產生，
/// 再由 Adapters 的 encoder 編成 v113 封包送出（queue-and-flush）。
/// </summary>
public sealed record NpcDialog(
    int NpcId,
    NpcDialogKind Kind,
    string Text,
    byte SpeakerType = 0,
    int NumberDefault = 0,
    int NumberMin = 0,
    int NumberMax = 0);
