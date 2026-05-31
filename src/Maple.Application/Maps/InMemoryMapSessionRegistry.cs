using Maple.Core.Characters;
using System.Collections.Concurrent;

namespace Maple.Application.Maps;

/// <summary>
/// 記憶體內的地圖 session 登記表（process 內單例）。
/// 外層: mapId → 內層: charId → entry。
/// </summary>
public sealed class InMemoryMapSessionRegistry : IMapSessionRegistry
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, MapPlayerEntry>> _maps = new();

    public void Register(int mapId, int charId, Character character, Func<byte[], CancellationToken, Task> sendPacket)
    {
        var map = _maps.GetOrAdd(mapId, _ => new ConcurrentDictionary<int, MapPlayerEntry>());
        map[charId] = new MapPlayerEntry(charId, character, sendPacket);
    }

    public void Deregister(int mapId, int charId)
    {
        if (_maps.TryGetValue(mapId, out var map))
        {
            map.TryRemove(charId, out _);
        }
    }

    public IReadOnlyList<MapPlayerEntry> GetOthers(int mapId, int charId)
    {
        if (!_maps.TryGetValue(mapId, out var map)) return Array.Empty<MapPlayerEntry>();
        return map.Values.Where(e => e.CharId != charId).ToList();
    }
}
