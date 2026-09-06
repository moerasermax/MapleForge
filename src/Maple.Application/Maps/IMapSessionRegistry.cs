using Maple.Core.Characters;
using Maple.Core.World;

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
    void Register(int mapId, int charId, Player player, Func<byte[], CancellationToken, Task> sendPacket, object token);

    /// <summary>玩家離開地圖時取消登記。</summary>
    bool Deregister(int mapId, int charId, object token);

    /// <summary>取得同地圖其他玩家（不包含 charId 自己）。</summary>
    IReadOnlyList<MapPlayerEntry> GetOthers(int mapId, int charId);
}

/// <summary>
/// 同地圖玩家的 session 資訊。P047：改帶完整 <see cref="Player"/>（原本只帶
/// <see cref="Character"/>），讓廣播端也能讀到只存在於 <c>Player</c> 執行期欄位的狀態
/// （如戒指外觀 <c>MarriagePartnerCharacterId</c>/<c>MarriageRingId</c>）。
/// </summary>
public sealed record MapPlayerEntry(
    int CharId,
    Player Player,
    Func<byte[], CancellationToken, Task> SendPacket,
    object Token)
{
    /// <summary>沿用既有呼叫端慣用寫法（<c>entry.Character</c>）的薄轉發。</summary>
    public Character Character => Player.Character;
}
