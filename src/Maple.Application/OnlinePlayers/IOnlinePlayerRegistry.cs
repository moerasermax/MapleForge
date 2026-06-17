using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Application.OnlinePlayers;

public sealed record OnlinePlayer(
    int CharacterId,
    string Name,
    int Channel,
    Player Player,
    Func<byte[], CancellationToken, Task> SendPacket)
{
    /// <summary>Convenience accessor — avoids churn for consumers that only need <see cref="Character"/>.</summary>
    public Character Character => Player.Character;
}

public interface IOnlinePlayerRegistry
{
    void Register(Player player, int channel, Func<byte[], CancellationToken, Task> sendPacket, object token);

    OnlinePlayer? Deregister(int characterId, object token);

    OnlinePlayer? FindById(int characterId);

    OnlinePlayer? FindByName(string name);
}
