using Maple.Core.World;
using System.Collections.Concurrent;

namespace Maple.Application.Maps;

/// <summary>
/// 記憶體內的地圖 session 登記表（process 內單例）。
/// 外層: mapId → 內層: charId → entry。
/// </summary>
public sealed class InMemoryMapSessionRegistry : IMapSessionRegistry
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, MapPlayerEntry>> _maps = new();

    public void Register(int mapId, int charId, Player player, Func<byte[], CancellationToken, Task> sendPacket, object token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var map = _maps.GetOrAdd(mapId, _ => new ConcurrentDictionary<int, MapPlayerEntry>());
        map[charId] = new MapPlayerEntry(charId, player, sendPacket, token);
    }

    public bool Deregister(int mapId, int charId, object token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (!_maps.TryGetValue(mapId, out var map) ||
            !map.TryGetValue(charId, out var entry) ||
            !ReferenceEquals(entry.Token, token))
        {
            return false;
        }

        var pair = new KeyValuePair<int, MapPlayerEntry>(charId, entry);
        return ((ICollection<KeyValuePair<int, MapPlayerEntry>>)map).Remove(pair);
    }

    public IReadOnlyList<MapPlayerEntry> GetOthers(int mapId, int charId)
    {
        if (!_maps.TryGetValue(mapId, out var map)) return Array.Empty<MapPlayerEntry>();
        return map.Values.Where(e => e.CharId != charId).ToList();
    }

    public IReadOnlyList<MapPlayerEntry> GetAll(int mapId)
    {
        if (!_maps.TryGetValue(mapId, out var map)) return Array.Empty<MapPlayerEntry>();
        return map.Values.ToList();
    }
}
