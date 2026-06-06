using Maple.Core.Characters;

namespace Maple.Application.Chats;

public sealed record ChatOnlinePlayer(
    int CharacterId,
    string Name,
    int Channel,
    Character Character,
    Func<byte[], CancellationToken, Task> SendPacket);

public interface IChatOnlineRegistry
{
    void Register(ChatOnlinePlayer player);

    ChatOnlinePlayer? Deregister(int characterId);

    ChatOnlinePlayer? FindById(int characterId);

    ChatOnlinePlayer? FindByName(string name);
}
