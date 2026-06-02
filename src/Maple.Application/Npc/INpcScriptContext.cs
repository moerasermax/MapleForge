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

    int GetJob();
    int GetMeso();
    int GetMap();
}
