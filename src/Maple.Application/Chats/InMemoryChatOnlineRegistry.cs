using System.Collections.Concurrent;

namespace Maple.Application.Chats;

public sealed class InMemoryChatOnlineRegistry : IChatOnlineRegistry
{
    private readonly ConcurrentDictionary<int, ChatOnlinePlayer> _byId = new();
    private readonly ConcurrentDictionary<string, int> _idByName = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ChatOnlinePlayer player)
    {
        if (_byId.TryGetValue(player.CharacterId, out var previous))
        {
            _idByName.TryRemove(previous.Name, out _);
        }

        _byId[player.CharacterId] = player;
        _idByName[player.Name] = player.CharacterId;
    }

    public ChatOnlinePlayer? Deregister(int characterId)
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

    public ChatOnlinePlayer? FindById(int characterId)
        => _byId.TryGetValue(characterId, out var player) ? player : null;

    public ChatOnlinePlayer? FindByName(string name)
    {
        return _idByName.TryGetValue(name, out var characterId)
            ? FindById(characterId)
            : null;
    }
}
