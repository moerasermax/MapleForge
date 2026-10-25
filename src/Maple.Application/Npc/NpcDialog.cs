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
    /// <summary>P167：接受／拒絕（askAcceptDecline，msgType 0x0B）。</summary>
    AcceptDecline,
    /// <summary>P167：接受／拒絕、不可 ESC（askAcceptDeclineNoESC，msgType 0x0C）。</summary>
    AcceptDeclineNoEsc,
    /// <summary>P168：髮型／臉型選擇（sendStyle／askAvatar，msgType 7 + 選項清單）。</summary>
    Style,
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
    int NumberMax = 0,
    IReadOnlyList<int>? Styles = null);

/// <summary>P171：腳本觸發的 UI 效果（版本無關；Adapters 編成封包）。</summary>
public enum NpcUiEffectKind
{
    /// <summary>Java <c>showInstruction(msg, width, height)</c> → sendHint。</summary>
    Hint,

    /// <summary>Java <c>showWZEffect(path)</c> → UIPacket.ShowWZEffect。</summary>
    WzEffect,

    /// <summary>Java <c>mapMessage(type, msg)</c>：全地圖 broadcastMessage。</summary>
    MapMessage,
}

public sealed record NpcUiEffect(NpcUiEffectKind Kind, string Text, int A = 0, int B = 0);

/// <summary>P176：跨隊員的腳本效果（warpParty／givePartyExp／givePartyItems），由呼叫端排進各隊員連線。</summary>
public enum NpcPartyActionKind
{
    Warp,
    Exp,
    Items,
}

/// <summary>
/// <paramref name="CharacterId"/> 為目標隊員；Warp：(MapId, Portal)；Exp：Amount（已乘倍率）；Items：(ItemId, Quantity, RemoveAll)。
/// </summary>
public sealed record NpcPartyAction(NpcPartyActionKind Kind, int CharacterId, int MapId = 0, int Portal = 0, int Amount = 0, int ItemId = 0, int Quantity = 0, bool RemoveAll = false);
