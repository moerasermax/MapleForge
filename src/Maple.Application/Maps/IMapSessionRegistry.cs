using Maple.Core.Characters;

namespace Maple.Application.Maps;

/// <summary>
/// 地圖玩家 session 登記表：追蹤哪些 session 在哪個地圖，供廣播使用。
/// 實作必須是 thread-safe（多個連線同時操作）。
/// </summary>
public interface IMapSessionRegistry
{
    /// <summary>
    /// 玩家進入地圖時登記（必須在 SET_FIELD 之後呼叫）。
    /// sendPacket 是該 session 的封包送出函式（thread-safe lambda）。
    /// </summary>
    void Register(int mapId, int charId, Character character, Func<byte[], CancellationToken, Task> sendPacket);

    /// <summary>玩家離開地圖時取消登記。</summary>
    void Deregister(int mapId, int charId);

    /// <summary>取得同地圖其他玩家（不包含 charId 自己）。</summary>
    IReadOnlyList<MapPlayerEntry> GetOthers(int mapId, int charId);
}

/// <summary>同地圖玩家的 session 資訊。</summary>
public sealed record MapPlayerEntry(
    int CharId,
    Character Character,
    Func<byte[], CancellationToken, Task> SendPacket);
