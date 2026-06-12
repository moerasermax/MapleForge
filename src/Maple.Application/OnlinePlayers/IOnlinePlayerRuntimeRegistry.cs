using Maple.Core.World;

namespace Maple.Application.OnlinePlayers;

public interface IOnlinePlayerRuntimeRegistry
{
    void Register(Player player, object token);

    Player? Deregister(int characterId, object token);

    Player? FindById(int characterId);
}
