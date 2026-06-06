using Maple.Core.Characters;

namespace Maple.Application.OnlinePlayers;

public sealed record OnlinePlayer(
    int CharacterId,
    string Name,
    int Channel,
    Character Character,
    Func<byte[], CancellationToken, Task> SendPacket);

public interface IOnlinePlayerRegistry
{
    void Register(OnlinePlayer player, object token);

    OnlinePlayer? Deregister(int characterId, object token);

    OnlinePlayer? FindById(int characterId);

    OnlinePlayer? FindByName(string name);
}
