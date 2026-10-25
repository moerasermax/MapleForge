namespace Maple.Application.Maps;

/// <summary>
/// P172：伺服器端主動換圖（船班到站、事件傳送等；Java <c>chr.changeMap(map, portal)</c> 由排程觸發）。
/// 實作端把換圖排進各玩家連線的處理序列（與封包處理互斥），所以呼叫是非同步、立即返回。
/// </summary>
public interface IPlayerTransferService
{
    /// <summary>把指定角色傳到 <paramref name="mapId"/> 的 <paramref name="portal"/>；角色不在線回 false。</summary>
    bool Warp(int characterId, int mapId, int portal);

    /// <summary>Java 事件腳本的 <c>map.getCharacters()</c> 逐一 changeMap：把 <paramref name="fromMapId"/> 上所有人傳走，回傳排入的人數。</summary>
    int WarpAll(int fromMapId, int toMapId, int portal);
}
