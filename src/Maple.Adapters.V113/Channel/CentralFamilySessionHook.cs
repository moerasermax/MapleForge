using Maple.Application.OnlinePlayers;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed class CentralFamilySessionHook : IV113FamilySessionHook
{
    private readonly IOnlinePlayerRegistry _online;

    public CentralFamilySessionHook(IOnlinePlayerRegistry online)
    {
        _online = online;
    }

    public ValueTask<Player?> FindOnlinePlayerByNameAsync(string name, CancellationToken ct)
    {
        var online = _online.FindByName(name);
        return ValueTask.FromResult(online?.Player);
    }

    public ValueTask<Player?> FindOnlinePlayerByIdAsync(int characterId, CancellationToken ct)
    {
        var online = _online.FindById(characterId);
        return ValueTask.FromResult(online?.Player);
    }

    public async ValueTask SendPacketAsync(int characterId, byte[] packet, CancellationToken ct)
    {
        var online = _online.FindById(characterId);
        if (online is null) return;
        await online.SendPacket(packet, ct);
    }
}
