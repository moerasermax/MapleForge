namespace Maple.Application.Npcs;

/// <summary>
/// 一個已載入、可重入的 NPC 腳本實例（OdinMS .js 的 start()/action() 包裝）。
/// 對話狀態（JS 全域 scope + status 變數）存活於實例內、跨多個 c2s 封包。
/// 由 <see cref="INpcScriptFactory"/> 建立；實作在 Maple.Scripting（Jint）。
/// </summary>
public interface INpcScript
{
    /// <summary>進入對話：呼叫腳本 start()。</summary>
    void Start();

    /// <summary>玩家回應後 resume：呼叫腳本 action(mode, type, selection)。</summary>
    /// <param name="mode">玩家動作（1=下一步/是/確定, 0=上一步/否, -1=關閉/ESC）。</param>
    /// <param name="type">上一則對話的型別（=送出時的 msgType）。</param>
    /// <param name="selection">選單索引或數字輸入（無則 -1）。</param>
    void Resume(int mode, int type, int selection);
}

/// <summary>
/// 依 npcId 建立腳本實例（無狀態工廠）。找不到對應 .js 回 <c>null</c>。
/// 實作在 Maple.Scripting；由 DI 注入 Adapters handler。
/// </summary>
public interface INpcScriptFactory
{
    INpcScript? TryCreate(int npcId, INpcScriptContext cm);
}

/// <summary>
/// P170：portal 腳本（scripts/portal/&lt;name&gt;.js 的 <c>enter(pi)</c>）。<see cref="INpcScript.Start"/> 呼叫 enter，
/// <c>pi</c> 與 NPC 的 <c>cm</c> 共用 <see cref="INpcScriptContext"/>（Java PortalPlayerInteraction 繼承 AbstractPlayerInteraction）。
/// </summary>
public interface IPortalScriptFactory
{
    INpcScript? TryCreatePortal(string scriptName, INpcScriptContext pi);
}
