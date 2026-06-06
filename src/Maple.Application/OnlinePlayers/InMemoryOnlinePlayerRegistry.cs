using System.Collections.Concurrent;

namespace Maple.Application.OnlinePlayers;

public sealed class InMemoryOnlinePlayerRegistry : IOnlinePlayerRegistry
{
    private readonly ConcurrentDictionary<int, RegisteredOnlinePlayer> _byId = new();
    private readonly ConcurrentDictionary<string, RegisteredOnlinePlayer> _byName = new(StringComparer.OrdinalIgnoreCase);

    public void Register(OnlinePlayer player, object token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var entry = new RegisteredOnlinePlayer(player, token);
        while (true)
        {
            if (_byId.TryGetValue(player.CharacterId, out var previous))
            {
                if (!_byId.TryUpdate(player.CharacterId, entry, previous))
                {
                    continue;
                }

                TryRemoveName(previous);
                break;
            }

            if (_byId.TryAdd(player.CharacterId, entry))
            {
                break;
            }
        }

        _byName[player.Name] = entry;
    }

    public OnlinePlayer? Deregister(int characterId, object token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (!_byId.TryGetValue(characterId, out var entry) ||
            !ReferenceEquals(entry.Token, token) ||
            !TryRemovePlayer(characterId, entry))
        {
            return null;
        }

        TryRemoveName(entry);

        return entry.Player;
    }

    public OnlinePlayer? FindById(int characterId) =>
        _byId.TryGetValue(characterId, out var entry) ? entry.Player : null;

    public OnlinePlayer? FindByName(string name)
    {
        return _byName.TryGetValue(name, out var entry) &&
            _byId.TryGetValue(entry.Player.CharacterId, out var current) &&
            ReferenceEquals(current, entry)
            ? entry.Player
            : null;
    }

    private bool TryRemovePlayer(int characterId, RegisteredOnlinePlayer entry)
    {
        var pair = new KeyValuePair<int, RegisteredOnlinePlayer>(characterId, entry);
        return ((ICollection<KeyValuePair<int, RegisteredOnlinePlayer>>)_byId).Remove(pair);
    }

    private void TryRemoveName(RegisteredOnlinePlayer entry)
    {
        var pair = new KeyValuePair<string, RegisteredOnlinePlayer>(entry.Player.Name, entry);
        ((ICollection<KeyValuePair<string, RegisteredOnlinePlayer>>)_byName).Remove(pair);
    }

    private sealed class RegisteredOnlinePlayer
    {
        public RegisteredOnlinePlayer(OnlinePlayer player, object token)
        {
            Player = player;
            Token = token;
        }

        public OnlinePlayer Player { get; }

        public object Token { get; }
    }
}
