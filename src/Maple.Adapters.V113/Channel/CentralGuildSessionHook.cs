using Maple.Application.OnlinePlayers;
using Maple.Core.Guilds;

namespace Maple.Adapters.V113.Channel;

public sealed class CentralGuildSessionHook : IV113GuildSessionHook
{
    private readonly IOnlinePlayerRegistry _online;

    public CentralGuildSessionHook(IOnlinePlayerRegistry online)
    {
        _online = online;
    }

    public ValueTask<V113GuildSessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct)
    {
        var online = _online.FindByName(characterName);
        if (online is null)
        {
            return ValueTask.FromResult<V113GuildSessionPlayer?>(null);
        }

        var chr = online.Character;
        var player = new V113GuildSessionPlayer(
            chr.Id,
            chr.Name,
            chr.Level,
            chr.Job,
            chr.GuildId,
            chr.GuildRank,
            chr.AllianceRank,
            online.Channel);

        return ValueTask.FromResult<V113GuildSessionPlayer?>(player);
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

    public Task UpdateGuildStatusAsync(int characterId, int guildId, byte guildRank, byte allianceRank, CancellationToken ct)
    {
        var online = _online.FindById(characterId);
        if (online is null)
        {
            return Task.CompletedTask;
        }

        online.Character.GuildId = guildId;
        online.Character.GuildRank = guildRank;
        online.Character.AllianceRank = allianceRank == 0 ? Guild.DefaultAllianceRank : allianceRank;
        return Task.CompletedTask;
    }
}
