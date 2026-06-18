using Maple.Application.Alliances;
using Maple.Core.Alliances;
using Maple.Core.Guilds;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed record V113AllianceInviteTarget(int GuildId, int LeaderCharacterId, string LeaderName);

public sealed record V113AllianceMember(int CharacterId, int GuildId, byte AllianceRank);

public sealed record V113AllianceGuildPacket(int GuildId, byte[] Packet);

public sealed record V113AllianceCharacterPacket(int CharacterId, byte[] Packet);

public sealed record V113AllianceCharacterNotice(int CharacterId, string Message);

public sealed record V113AllianceHandleResult(
    AllianceCommandStatus Status,
    IReadOnlyList<byte[]> SelfPackets,
    IReadOnlyList<V113AllianceGuildPacket> GuildPackets,
    IReadOnlyList<V113AllianceCharacterPacket> CharacterPackets,
    IReadOnlyList<V113AllianceCharacterNotice> CharacterNotices)
{
    public bool Succeeded => Status == AllianceCommandStatus.Success;
}

public interface IV113AllianceSessionHook
{
    Task<GuildState?> GetGuildAsync(int guildId, CancellationToken ct);

    ValueTask<V113AllianceInviteTarget?> FindGuildLeaderByGuildNameAsync(string guildName, CancellationToken ct);

    ValueTask<V113AllianceMember?> FindAllianceMemberAsync(int characterId, CancellationToken ct);
}

public sealed class V113AllianceHandler
{
    private readonly AllianceService _alliances;
    private readonly IV113AllianceSessionHook _sessions;

    public V113AllianceHandler(AllianceService alliances, IV113AllianceSessionHook sessions)
    {
        _alliances = alliances;
        _sessions = sessions;
    }

    public async Task<V113AllianceHandleResult> HandleAllianceOperationAsync(
        PacketReader reader,
        Player player,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(player);

        GuildState? guild;
        try
        {
            guild = await GetCurrentGuildAsync(player, ct).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        if (guild is null)
        {
            return SelfOnly(AllianceCommandStatus.InvalidGuild, V113StatsPackets.EnableActions());
        }

        V113AllianceClientOperation operation;
        try
        {
            operation = V113AlliancePackets.ReadOperation(reader);
        }
        catch (InvalidDataException)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        if (player.Character.GuildRank != Guild.LeaderRank && operation != V113AllianceClientOperation.Load)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        if (operation == V113AllianceClientOperation.Deny)
        {
            return await DenyInviteAsync(player, guild, ct).ConfigureAwait(false);
        }

        var allianceId = await ResolveAllianceIdAsync(guild, ct).ConfigureAwait(false);
        var alliance = allianceId > 0 ? await _alliances.GetAllianceInfoAsync(allianceId, ct).ConfigureAwait(false) : null;
        var leaderId = alliance?.LeaderId ?? 0;

        if (operation != V113AllianceClientOperation.Accept)
        {
            if (allianceId <= 0 || leaderId <= 0)
            {
                return Empty(AllianceCommandStatus.AllianceNotFound);
            }
        }
        else if (allianceId > 0 || leaderId > 0)
        {
            return Empty(AllianceCommandStatus.AlreadyInAlliance);
        }

        return operation switch
        {
            V113AllianceClientOperation.Load => await HandleLoadAsync(alliance, ct).ConfigureAwait(false),
            V113AllianceClientOperation.Invite => await HandleInviteAsync(reader, player, guild, alliance!, leaderId, ct).ConfigureAwait(false),
            V113AllianceClientOperation.Accept => await HandleAcceptAsync(player, guild, ct).ConfigureAwait(false),
            V113AllianceClientOperation.Leave => await HandleRemoveGuildAsync(player, guild, allianceId, guild.Id, expelled: false, ct).ConfigureAwait(false),
            V113AllianceClientOperation.Expel => await HandleExpelAsync(reader, player, guild, allianceId, ct).ConfigureAwait(false),
            V113AllianceClientOperation.ChangeLeader => await HandleChangeLeaderAsync(reader, player, alliance!, leaderId, ct).ConfigureAwait(false),
            V113AllianceClientOperation.TitleUpdate => await HandleTitleUpdateAsync(reader, player, alliance!, leaderId, ct).ConfigureAwait(false),
            V113AllianceClientOperation.RankChange => await HandleRankChangeAsync(reader, player, alliance!, ct).ConfigureAwait(false),
            V113AllianceClientOperation.NoticeUpdate => await HandleNoticeUpdateAsync(reader, player, alliance!, ct).ConfigureAwait(false),
            _ => Empty(AllianceCommandStatus.InvalidOperation),
        };
    }

    public async Task<V113AllianceHandleResult> HandleDenyAllianceRequestAsync(
        PacketReader reader,
        Player player,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(player);

        var guild = await GetCurrentGuildAsync(player, ct).ConfigureAwait(false);
        return guild is null
            ? SelfOnly(AllianceCommandStatus.InvalidGuild, V113StatsPackets.EnableActions())
            : await DenyInviteAsync(player, guild, ct).ConfigureAwait(false);
    }

    private async Task<V113AllianceHandleResult> HandleLoadAsync(AllianceState? alliance, CancellationToken ct)
    {
        if (alliance is null)
        {
            return Empty(AllianceCommandStatus.AllianceNotFound);
        }

        var guilds = await LoadGuildsAsync(alliance, ct).ConfigureAwait(false);
        return SelfOnly(
            AllianceCommandStatus.Success,
            V113AlliancePackets.AllianceInfo(alliance),
            V113AlliancePackets.GuildAlliance(alliance, guilds),
            V113AlliancePackets.AllianceUpdate(alliance));
    }

    private async Task<V113AllianceHandleResult> HandleInviteAsync(
        PacketReader reader,
        Player player,
        GuildState guild,
        AllianceState alliance,
        int leaderId,
        CancellationToken ct)
    {
        string targetGuildName;
        try
        {
            targetGuildName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        if (player.Character.AllianceRank != Alliance.LeaderRank || leaderId != player.Character.Id)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        var target = await _sessions.FindGuildLeaderByGuildNameAsync(targetGuildName, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Empty(AllianceCommandStatus.InvalidGuild);
        }

        var result = await _alliances.InviteGuildAsync(alliance.Id, target.GuildId, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Empty(result.Status);
        }

        return new V113AllianceHandleResult(
            AllianceCommandStatus.Success,
            Array.Empty<byte[]>(),
            Array.Empty<V113AllianceGuildPacket>(),
            [new V113AllianceCharacterPacket(target.LeaderCharacterId, V113AlliancePackets.AllianceInvite(alliance.Name, guild.Id, player.Character.Name))],
            Array.Empty<V113AllianceCharacterNotice>());
    }

    private async Task<V113AllianceHandleResult> HandleAcceptAsync(
        Player player,
        GuildState guild,
        CancellationToken ct)
    {
        var result = await _alliances.AcceptInviteAsync(guild.Id, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.Alliance is null)
        {
            return Empty(result.Status);
        }

        var allianceGuilds = await LoadGuildsAsync(result.Alliance, ct, guild).ConfigureAwait(false);
        var joinedGuild = WithAllianceId(guild, result.Alliance.Id);
        var selfPackets = new[]
        {
            V113AlliancePackets.AllianceInfo(result.Alliance),
            V113AlliancePackets.GuildAlliance(result.Alliance, allianceGuilds),
            V113AlliancePackets.ChangeAlliance(result.Alliance, allianceGuilds, inAlliance: true),
        };

        var guildPackets = result.Alliance.GuildIds
            .Where(id => id != guild.Id)
            .SelectMany(id => new[]
            {
                new V113AllianceGuildPacket(id, V113AlliancePackets.AddGuildToAlliance(result.Alliance, joinedGuild)),
                new V113AllianceGuildPacket(id, V113AlliancePackets.ChangeGuildInAlliance(result.Alliance, joinedGuild, add: true)),
            })
            .ToArray();

        return new V113AllianceHandleResult(
            AllianceCommandStatus.Success,
            selfPackets,
            guildPackets,
            Array.Empty<V113AllianceCharacterPacket>(),
            Array.Empty<V113AllianceCharacterNotice>());
    }

    private async Task<V113AllianceHandleResult> HandleExpelAsync(
        PacketReader reader,
        Player player,
        GuildState guild,
        int allianceId,
        CancellationToken ct)
    {
        int guildId;
        try
        {
            guildId = reader.Remaining >= 4 ? reader.ReadInt() : guild.Id;
            if (reader.Remaining >= 4 && reader.ReadInt() != allianceId)
            {
                return Empty(AllianceCommandStatus.InvalidOperation);
            }
        }
        catch (InvalidDataException)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        return await HandleRemoveGuildAsync(player, guild, allianceId, guildId, expelled: true, ct).ConfigureAwait(false);
    }

    private async Task<V113AllianceHandleResult> HandleRemoveGuildAsync(
        Player player,
        GuildState currentGuild,
        int allianceId,
        int guildId,
        bool expelled,
        CancellationToken ct)
    {
        if (player.Character.AllianceRank > Alliance.SubLeaderRank ||
            (player.Character.AllianceRank != Alliance.LeaderRank && currentGuild.Id != guildId))
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        var removedGuild = await _sessions.GetGuildAsync(guildId, ct).ConfigureAwait(false);
        var result = await _alliances.RemoveGuildAsync(allianceId, guildId, expelled, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Empty(result.Status);
        }

        if (result.UpdateKind == AllianceUpdateKind.Disbanded)
        {
            return ToGuildPackets(
                AllianceCommandStatus.Success,
                result.AffectedGuilds,
                V113AlliancePackets.DisbandAlliance(allianceId));
        }

        if (result.Alliance is null || removedGuild is null)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        var withoutAlliance = WithAllianceId(removedGuild, 0);
        var packets = result.AffectedGuilds
            .SelectMany(id => new[]
            {
                new V113AllianceGuildPacket(id, V113AlliancePackets.ChangeGuildInAlliance(result.Alliance, withoutAlliance, add: false)),
                new V113AllianceGuildPacket(id, V113AlliancePackets.RemoveGuildFromAlliance(result.Alliance, withoutAlliance, expelled)),
                new V113AllianceGuildPacket(id, V113AlliancePackets.AllianceUpdate(result.Alliance)),
            })
            .ToArray();

        return new V113AllianceHandleResult(
            AllianceCommandStatus.Success,
            Array.Empty<byte[]>(),
            packets,
            Array.Empty<V113AllianceCharacterPacket>(),
            Array.Empty<V113AllianceCharacterNotice>());
    }

    private async Task<V113AllianceHandleResult> HandleChangeLeaderAsync(
        PacketReader reader,
        Player player,
        AllianceState alliance,
        int leaderId,
        CancellationToken ct)
    {
        int newLeaderId;
        try
        {
            newLeaderId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        if (player.Character.AllianceRank != Alliance.LeaderRank || leaderId != player.Character.Id)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        var target = await _sessions.FindAllianceMemberAsync(newLeaderId, ct).ConfigureAwait(false);
        var result = await _alliances.ChangeLeaderAsync(alliance.Id, newLeaderId, target?.GuildId, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.Alliance is null || result.PreviousLeaderId is null)
        {
            return Empty(result.Status);
        }

        var guilds = await LoadGuildsAsync(result.Alliance, ct).ConfigureAwait(false);
        var packets = new[]
        {
            V113AlliancePackets.ChangeAllianceLeader(result.Alliance.Id, newLeaderId, result.PreviousLeaderId.Value),
            V113AlliancePackets.UpdateAllianceLeader(result.Alliance.Id, newLeaderId, result.PreviousLeaderId.Value),
            V113AlliancePackets.AllianceUpdate(result.Alliance),
            V113AlliancePackets.GuildAlliance(result.Alliance, guilds),
        };

        return ToGuildPackets(AllianceCommandStatus.Success, result.AffectedGuilds, packets);
    }

    private async Task<V113AllianceHandleResult> HandleTitleUpdateAsync(
        PacketReader reader,
        Player player,
        AllianceState alliance,
        int leaderId,
        CancellationToken ct)
    {
        if (player.Character.AllianceRank != Alliance.LeaderRank || leaderId != player.Character.Id)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        var ranks = new string[Alliance.RankCount];
        try
        {
            for (var i = 0; i < ranks.Length; i++)
            {
                ranks[i] = reader.ReadMapleString();
            }
        }
        catch (InvalidDataException)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        var result = await _alliances.UpdateRanksAsync(alliance.Id, ranks, ct).ConfigureAwait(false);
        return result.Succeeded && result.Alliance is not null
            ? ToGuildPackets(AllianceCommandStatus.Success, result.AffectedGuilds, V113AlliancePackets.AllianceUpdate(result.Alliance))
            : Empty(result.Status);
    }

    private async Task<V113AllianceHandleResult> HandleRankChangeAsync(
        PacketReader reader,
        Player player,
        AllianceState alliance,
        CancellationToken ct)
    {
        if (player.Character.AllianceRank > Alliance.SubLeaderRank)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        int targetId;
        byte change;
        try
        {
            targetId = reader.ReadInt();
            change = reader.ReadByte();
        }
        catch (InvalidDataException)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        var target = await _sessions.FindAllianceMemberAsync(targetId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Empty(AllianceCommandStatus.InvalidGuild);
        }

        var result = await _alliances.ChangeAllianceRankAsync(alliance.Id, targetId, target.AllianceRank, change, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.AllianceRank is null)
        {
            return Empty(result.Status);
        }

        var packets = new[]
        {
            V113AlliancePackets.ChangeAllianceRank(alliance.Id, targetId, result.AllianceRank.Value),
            V113AlliancePackets.UpdateAllianceRank(alliance.Id, targetId, result.AllianceRank.Value),
        };
        return ToGuildPackets(AllianceCommandStatus.Success, result.AffectedGuilds, packets);
    }

    private async Task<V113AllianceHandleResult> HandleNoticeUpdateAsync(
        PacketReader reader,
        Player player,
        AllianceState alliance,
        CancellationToken ct)
    {
        if (player.Character.AllianceRank > Alliance.SubLeaderRank)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        string notice;
        try
        {
            notice = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return Empty(AllianceCommandStatus.InvalidOperation);
        }

        var result = await _alliances.UpdateNoticeAsync(alliance.Id, notice, ct).ConfigureAwait(false);
        return result.Succeeded && result.Alliance is not null
            ? ToGuildPackets(AllianceCommandStatus.Success, result.AffectedGuilds, V113AlliancePackets.AllianceUpdate(result.Alliance))
            : Empty(result.Status);
    }

    private async Task<V113AllianceHandleResult> DenyInviteAsync(
        Player player,
        GuildState guild,
        CancellationToken ct)
    {
        var result = await _alliances.DenyInviteAsync(guild.Id, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.InviterCharacterId is null)
        {
            return Empty(result.Status);
        }

        return new V113AllianceHandleResult(
            AllianceCommandStatus.Success,
            Array.Empty<byte[]>(),
            Array.Empty<V113AllianceGuildPacket>(),
            Array.Empty<V113AllianceCharacterPacket>(),
            [new V113AllianceCharacterNotice(result.InviterCharacterId.Value, $"{guild.Name} Guild has rejected the Guild Union invitation.")]);
    }

    private async Task<GuildState?> GetCurrentGuildAsync(Player player, CancellationToken ct) =>
        player.Character.GuildId <= 0 ? null : await _sessions.GetGuildAsync(player.Character.GuildId, ct).ConfigureAwait(false);

    private async Task<int> ResolveAllianceIdAsync(GuildState guild, CancellationToken ct) =>
        guild.AllianceId > 0 ? guild.AllianceId : await _alliances.GetAllianceIdForGuildAsync(guild.Id, ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<GuildState>> LoadGuildsAsync(
        AllianceState alliance,
        CancellationToken ct,
        GuildState? knownGuild = null)
    {
        var guilds = new List<GuildState>(alliance.GuildIds.Count);
        foreach (var guildId in alliance.GuildIds)
        {
            var guild = knownGuild?.Id == guildId ? knownGuild : await _sessions.GetGuildAsync(guildId, ct).ConfigureAwait(false);
            if (guild is not null)
            {
                guilds.Add(WithAllianceId(guild, alliance.Id));
            }
        }

        return guilds;
    }

    private static GuildState WithAllianceId(GuildState guild, int allianceId) =>
        guild with { AllianceId = allianceId };

    private static V113AllianceHandleResult Empty(AllianceCommandStatus status) =>
        new(
            status,
            Array.Empty<byte[]>(),
            Array.Empty<V113AllianceGuildPacket>(),
            Array.Empty<V113AllianceCharacterPacket>(),
            Array.Empty<V113AllianceCharacterNotice>());

    private static V113AllianceHandleResult SelfOnly(AllianceCommandStatus status, params byte[][] packets) =>
        new(
            status,
            packets,
            Array.Empty<V113AllianceGuildPacket>(),
            Array.Empty<V113AllianceCharacterPacket>(),
            Array.Empty<V113AllianceCharacterNotice>());

    private static V113AllianceHandleResult ToGuildPackets(
        AllianceCommandStatus status,
        IReadOnlyList<int> guildIds,
        params byte[][] packets) =>
        new(
            status,
            Array.Empty<byte[]>(),
            guildIds.SelectMany(guildId => packets.Select(packet => new V113AllianceGuildPacket(guildId, packet))).ToArray(),
            Array.Empty<V113AllianceCharacterPacket>(),
            Array.Empty<V113AllianceCharacterNotice>());
}
