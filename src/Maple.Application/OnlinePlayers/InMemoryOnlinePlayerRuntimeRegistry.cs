using System.Collections.Concurrent;
using Maple.Core.World;

namespace Maple.Application.OnlinePlayers;

public sealed class InMemoryOnlinePlayerRuntimeRegistry : IOnlinePlayerRuntimeRegistry
{
    private readonly ConcurrentDictionary<int, RegisteredRuntimePlayer> _players = new();

    public void Register(Player player, object token)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(token);

        _players[player.Character.Id] = new RegisteredRuntimePlayer(player, token);
    }

    public Player? Deregister(int characterId, object token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (!_players.TryGetValue(characterId, out var entry) ||
            !ReferenceEquals(entry.Token, token))
        {
            return null;
        }

        var pair = new KeyValuePair<int, RegisteredRuntimePlayer>(characterId, entry);
        return ((ICollection<KeyValuePair<int, RegisteredRuntimePlayer>>)_players).Remove(pair)
            ? entry.Player
            : null;
    }

    public Player? FindById(int characterId) =>
        _players.TryGetValue(characterId, out var entry) ? entry.Player : null;

    private sealed class RegisteredRuntimePlayer
    {
        public RegisteredRuntimePlayer(Player player, object token)
        {
            Player = player;
            Token = token;
        }

        public Player Player { get; }

        public object Token { get; }
    }
}
