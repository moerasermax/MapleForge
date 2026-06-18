using Maple.Application.OnlinePlayers;
using Maple.Core.Characters;

namespace Maple.Adapters.V113.Channel;

public sealed class CentralMessengerSessionHook : IV113MessengerSessionHook
{
    private readonly IOnlinePlayerRegistry _online;

    public CentralMessengerSessionHook(IOnlinePlayerRegistry online)
    {
        _online = online;
    }

    public ValueTask<V113MessengerSessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct)
    {
        var online = _online.FindByName(characterName);
        if (online is null)
            return ValueTask.FromResult<V113MessengerSessionPlayer?>(null);

        var chr = online.Character;
        return ValueTask.FromResult<V113MessengerSessionPlayer?>(
            new V113MessengerSessionPlayer(chr.Id, chr.Name, online.Channel, chr));
    }

    public async Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
    {
        var online = _online.FindById(characterId);
        if (online is null) return;
        await online.SendPacket(packet, ct);
    }
}
