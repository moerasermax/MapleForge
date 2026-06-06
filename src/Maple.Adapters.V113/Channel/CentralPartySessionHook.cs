using Maple.Application.OnlinePlayers;

namespace Maple.Adapters.V113.Channel;

public sealed class CentralPartySessionHook : IV113PartySessionHook
{
    private readonly IOnlinePlayerRegistry _online;

    public CentralPartySessionHook(IOnlinePlayerRegistry online)
    {
        _online = online;
    }

    public ValueTask<V113PartySessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct)
    {
        var online = _online.FindByName(characterName);
        if (online is null)
        {
            return ValueTask.FromResult<V113PartySessionPlayer?>(null);
        }

        var chr = online.Character;
        var player = new V113PartySessionPlayer(
            chr.Id,
            chr.Name,
            chr.Level,
            chr.Job,
            chr.MapId,
            ChannelIndex: Math.Max(0, online.Channel - 1));

        return ValueTask.FromResult<V113PartySessionPlayer?>(player);
    }

    public async Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
    {
        var online = _online.FindById(characterId);
        if (online is null)
        {
            return;
        }

        await online.SendPacket(packet, ct);
    }
}
