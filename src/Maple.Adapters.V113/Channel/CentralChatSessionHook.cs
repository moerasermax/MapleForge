using Maple.Application.OnlinePlayers;

namespace Maple.Adapters.V113.Channel;

public interface IV113ChatSessionHook
{
    Task<bool> TrySendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct);
}

public sealed class CentralChatSessionHook : IV113ChatSessionHook
{
    private readonly IOnlinePlayerRegistry _online;

    public CentralChatSessionHook(IOnlinePlayerRegistry online)
    {
        _online = online;
    }

    public async Task<bool> TrySendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
    {
        var online = _online.FindById(characterId);
        if (online is null)
        {
            return false;
        }

        await online.SendPacket(packet, ct);
        return true;
    }
}
