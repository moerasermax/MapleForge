using System.Collections.Concurrent;
using Maple.Application.Maps;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P172：角色 → 連線指令入口（排入 <c>MapleSession.Post</c> 的換圖等）。登入時註冊、斷線時以 session token 註銷
/// （同角色重連時舊 token 的註銷不會蓋掉新連線）。實作 <see cref="IPlayerTransferService"/>。
/// </summary>
public sealed class V113SessionCommandRegistry : IPlayerTransferService
{
    private readonly ConcurrentDictionary<int, Entry> _entries = new();
    private readonly IMapSessionRegistry _maps;

    public V113SessionCommandRegistry(IMapSessionRegistry maps)
    {
        _maps = maps;
    }

    public void Register(int characterId, object sessionToken, Func<int, int, bool> warp, Func<Func<CancellationToken, Task>, bool>? post = null)
        => _entries[characterId] = new Entry(sessionToken, warp, post);

    /// <summary>P176：把工作排進該角色的連線序列（隊伍經驗／道具等跨玩家效果）；不在線或未提供回 false。</summary>
    public bool TryPost(int characterId, Func<CancellationToken, Task> work)
        => _entries.TryGetValue(characterId, out var entry) && entry.Post is { } post && post(work);

    public void Unregister(int characterId, object sessionToken)
    {
        if (_entries.TryGetValue(characterId, out var entry) && ReferenceEquals(entry.Token, sessionToken))
        {
            _entries.TryRemove(new KeyValuePair<int, Entry>(characterId, entry));
        }
    }

    public bool Warp(int characterId, int mapId, int portal)
        => _entries.TryGetValue(characterId, out var entry) && entry.Warp(mapId, portal);

    public int WarpAll(int fromMapId, int toMapId, int portal)
    {
        var count = 0;
        foreach (var player in _maps.GetAll(fromMapId).ToArray())
        {
            if (Warp(player.CharId, toMapId, portal))
            {
                count++;
            }
        }

        return count;
    }

    private readonly record struct Entry(object Token, Func<int, int, bool> Warp, Func<Func<CancellationToken, Task>, bool>? Post);
}
