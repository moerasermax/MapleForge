using Maple.Application.Guilds;
using Maple.Application.OnlinePlayers;
using Maple.Core.Guilds;

namespace Maple.Adapters.V113.Channel;

public sealed class CentralAllianceSessionHook : IV113AllianceSessionHook
{
    private readonly IOnlinePlayerRegistry _online;
    private readonly GuildService _guilds;

    public CentralAllianceSessionHook(IOnlinePlayerRegistry online, GuildService guilds)
    {
        _online = online;
        _guilds = guilds;
    }

    public async Task<GuildState?> GetGuildAsync(int guildId, CancellationToken ct)
        => await _guilds.GetGuildAsync(guildId, ct);

    public ValueTask<V113AllianceInviteTarget?> FindGuildLeaderByGuildNameAsync(string guildName, CancellationToken ct)
    {
        var leader = _online.FindByName(guildName);
        if (leader is null)
            return ValueTask.FromResult<V113AllianceInviteTarget?>(null);

        var chr = leader.Character;
        if (chr.GuildId <= 0)
            return ValueTask.FromResult<V113AllianceInviteTarget?>(null);

        return ValueTask.FromResult<V113AllianceInviteTarget?>(
            new V113AllianceInviteTarget(chr.GuildId, chr.Id, chr.Name));
    }

    public ValueTask<V113AllianceMember?> FindAllianceMemberAsync(int characterId, CancellationToken ct)
    {
        var online = _online.FindById(characterId);
        if (online is null)
            return ValueTask.FromResult<V113AllianceMember?>(null);

        var chr = online.Character;
        return ValueTask.FromResult<V113AllianceMember?>(
            new V113AllianceMember(chr.Id, chr.GuildId, chr.AllianceRank));
    }
}
