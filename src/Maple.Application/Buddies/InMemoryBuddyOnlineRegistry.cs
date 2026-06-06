using System.Collections.Concurrent;

namespace Maple.Application.Buddies;

public sealed class InMemoryBuddyOnlineRegistry : IBuddyOnlineRegistry
{
    private readonly ConcurrentDictionary<int, BuddyOnlinePlayer> _byId = new();
    private readonly ConcurrentDictionary<string, int> _idByName = new(StringComparer.OrdinalIgnoreCase);

    public void Register(BuddyOnlinePlayer player)
    {
        if (_byId.TryGetValue(player.CharacterId, out var previous))
        {
            _idByName.TryRemove(previous.Name, out _);
        }

        _byId[player.CharacterId] = player;
        _idByName[player.Name] = player.CharacterId;
    }

    public BuddyOnlinePlayer? Deregister(int characterId)
    {
        if (!_byId.TryRemove(characterId, out var removed))
        {
            return null;
        }

        if (_idByName.TryGetValue(removed.Name, out var mappedId) && mappedId == characterId)
        {
            _idByName.TryRemove(removed.Name, out _);
        }

        return removed;
    }

    public BuddyOnlinePlayer? FindById(int characterId)
        => _byId.TryGetValue(characterId, out var player) ? player : null;

    public BuddyOnlinePlayer? FindByName(string name)
    {
        return _idByName.TryGetValue(name, out var characterId)
            ? FindById(characterId)
            : null;
    }
}
