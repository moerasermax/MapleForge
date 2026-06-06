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
    void Register(OnlinePlayer player);

    OnlinePlayer? Deregister(int characterId);

    OnlinePlayer? FindById(int characterId);

    OnlinePlayer? FindByName(string name);
}
