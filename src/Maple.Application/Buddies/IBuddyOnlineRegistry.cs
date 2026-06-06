using Maple.Core.Characters;

namespace Maple.Application.Buddies;

public sealed record BuddyOnlinePlayer(
    int CharacterId,
    string Name,
    int Channel,
    Character Character,
    Func<byte[], CancellationToken, Task> SendPacket);

public interface IBuddyOnlineRegistry
{
    void Register(BuddyOnlinePlayer player);

    BuddyOnlinePlayer? Deregister(int characterId);

    BuddyOnlinePlayer? FindById(int characterId);

    BuddyOnlinePlayer? FindByName(string name);
}
