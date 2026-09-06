using Maple.Application.Alliances;
using Maple.Application.Guilds;
using Maple.Core.Guilds;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed record V113GuildSessionPlayer(
    int CharacterId,
    string Name,
    short Level,
    int JobId,
    int GuildId,
    byte GuildRank,
    byte AllianceRank,
    int Channel)
{
    public GuildMember ToGuildMember() => new()
    {
        CharacterId = CharacterId,
        Name = Name,
        Level = Level,
        JobId = JobId,
        GuildId = GuildId,
        GuildRank = GuildRank,
        AllianceRank = AllianceRank,
        Channel = Channel > 0 ? (byte)Math.Min(Channel, byte.MaxValue - 1) : byte.MaxValue,
        IsOnline = true,
    };
}

public interface IV113GuildSessionHook
{
    ValueTask<V113GuildSessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct);

    Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct);

    Task UpdateGuildStatusAsync(int characterId, int guildId, byte guildRank, byte allianceRank, CancellationToken ct);
}

public sealed class V113GuildOperationHandler
{
    private readonly GuildService _guilds;
    private readonly IV113GuildSessionHook _sessions;
    private readonly AllianceService _alliances;

    public V113GuildOperationHandler(GuildService guilds, IV113GuildSessionHook sessions, AllianceService alliances)
    {
        _guilds = guilds;
        _sessions = sessions;
        _alliances = alliances;
    }

    public async Task HandleGuildOperationAsync(
        PacketReader reader,
        Player player,
        int channel,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(sendSelf);

        V113GuildClientOperation operation;
        try
        {
            operation = V113GuildPackets.ReadOperation(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        switch (operation)
        {
            case V113GuildClientOperation.Create:
                await HandleCreateAsync(reader, player, channel, sendSelf, ct);
                break;

            case V113GuildClientOperation.Invite:
                await HandleInviteAsync(reader, player, sendSelf, ct);
                break;

            case V113GuildClientOperation.Accepted:
                await HandleAcceptedAsync(reader, player, channel, sendSelf, ct);
                break;

            case V113GuildClientOperation.Leaving:
                await HandleLeavingAsync(reader, player, sendSelf, ct);
                break;

            case V113GuildClientOperation.Expel:
                await HandleExpelAsync(reader, player, sendSelf, ct);
                break;

            case V113GuildClientOperation.ChangeRankTitle:
                await HandleChangeRankTitlesAsync(reader, player, sendSelf, ct);
                break;

            case V113GuildClientOperation.ChangeRank:
                await HandleChangeRankAsync(reader, player, sendSelf, ct);
                break;

            case V113GuildClientOperation.ChangeEmblem:
                await HandleChangeEmblemAsync(reader, player, sendSelf, ct);
                break;

            case V113GuildClientOperation.ChangeNotice:
                await HandleChangeNoticeAsync(reader, player, sendSelf, ct);
                break;
        }
    }

    public async Task HandleDenyGuildRequestAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(player);

        string inviterName;
        try
        {
            reader.Skip(1);
            inviterName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var inviter = await _sessions.FindOnlinePlayerByNameAsync(inviterName, ct);
        if (inviter is null)
        {
            return;
        }

        await _sessions.SendToCharacterAsync(
            inviter.CharacterId,
            V113GuildPackets.DenyGuildInvitation(player.Character.Name),
            ct);
    }

    public async Task OnPlayerLoggedInAsync(
        Player player,
        int channel,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var result = await _guilds.SetMemberOnlineAsync(player, online: true, channel: channel, ct: ct);
        if (!result.Succeeded || result.Guild is null || result.Target is null)
        {
            return;
        }

        await sendSelf(V113GuildPackets.ShowGuildInfo(result.Guild), ct);
        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.GuildMemberOnline(result.Guild.Id, result.Target.CharacterId, online: true),
            ct);

        if (result.OnlineStatusChanged)
        {
            await BroadcastAllianceMemberOnlineAsync(result.Guild, result.Target.CharacterId, online: true, ct);
        }
    }

    public async Task OnPlayerLoggedOutAsync(Player player, CancellationToken ct)
    {
        var result = await _guilds.SetMemberOnlineAsync(player, online: false, channel: -1, ct: ct);
        if (!result.Succeeded || result.Guild is null || result.Target is null)
        {
            return;
        }

        await BroadcastRemoteOnlyAsync(
            result,
            result.Target.CharacterId,
            V113GuildPackets.GuildMemberOnline(result.Guild.Id, result.Target.CharacterId, online: false),
            ct);

        if (result.OnlineStatusChanged)
        {
            await BroadcastAllianceMemberOnlineAsync(result.Guild, result.Target.CharacterId, online: false, ct);
        }
    }

    /// <summary>
    /// 對照 Java <c>MapleGuild.setOnline</c>：狀態實際翻轉時，若該公會屬於某個同盟，還要通知同盟裡
    /// <b>其他公會</b>的所有成員（<c>World.Alliance.sendGuild(packet, exceptionId=guildId, allianceId)</c>
    /// 用 guildId 當排除鍵，整個來源公會都跳過——因為公會內部已經在上面用
    /// <see cref="V113GuildPackets.GuildMemberOnline"/> 通知過了）。對應封包
    /// <see cref="V113AlliancePackets.AllianceMemberOnline"/> 先前已存在但零呼叫者。
    /// </summary>
    private async Task BroadcastAllianceMemberOnlineAsync(GuildState guild, int characterId, bool online, CancellationToken ct)
    {
        var allianceId = guild.AllianceId > 0
            ? guild.AllianceId
            : await _alliances.GetAllianceIdForGuildAsync(guild.Id, ct).ConfigureAwait(false);
        if (allianceId <= 0)
        {
            return;
        }

        var alliance = await _alliances.GetAllianceInfoAsync(allianceId, ct).ConfigureAwait(false);
        if (alliance is null)
        {
            return;
        }

        var packet = V113AlliancePackets.AllianceMemberOnline(allianceId, guild.Id, characterId, online);
        foreach (var guildId in alliance.GuildIds)
        {
            if (guildId == guild.Id)
            {
                continue;
            }

            var otherGuild = await _guilds.GetGuildAsync(guildId, ct).ConfigureAwait(false);
            if (otherGuild is null)
            {
                continue;
            }

            foreach (var member in otherGuild.Members)
            {
                if (!member.IsOnline)
                {
                    continue;
                }

                try
                {
                    await _sessions.SendToCharacterAsync(member.CharacterId, packet, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// P059：對照 Java <c>MapleGuild.memberLevelJobUpdate</c>——玩家等級提升時同步公會成員快取的
    /// 等級/職業並廣播 <see cref="V113GuildPackets.GuildMemberLevelJobUpdate"/>（含玩家自己，忠實對照
    /// Java 的 <c>broadcast(packet)</c> 不排除來源角色）；若公會屬於同盟，比照
    /// <see cref="BroadcastAllianceMemberOnlineAsync"/> 的手法通知同盟裡其他公會的線上成員
    /// <see cref="V113AlliancePackets.UpdateAllianceMember"/>。目前只接在擊殺怪物取得經驗值升級的路徑
    /// （<c>SendMobKillRewardsAsync</c>），道具/藥水直接領取經驗值升級與職業轉職（MapleForge 尚未實作
    /// 轉職）暫不覆蓋，留給後續 P-phase；不在公會時呼叫端已提前跳過，這裡不會有 side effect。
    /// </summary>
    public async Task SyncMemberLevelJobAsync(
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var result = await _guilds.SyncMemberLevelJobAsync(player, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.Guild is null || result.Target is null)
        {
            return;
        }

        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.GuildMemberLevelJobUpdate(result.Target),
            ct);

        await BroadcastAllianceMemberLevelJobAsync(result.Guild, result.Target, ct);
    }

    /// <summary>對照 Java <c>if (allianceid > 0) World.Alliance.sendGuild(updateAlliance(mgc, allianceid), id, allianceid)</c>：
    /// 用 guildId 當排除鍵跳過來源公會（公會內部已經在上面廣播過了），通知同盟裡其他公會的線上成員。</summary>
    private async Task BroadcastAllianceMemberLevelJobAsync(GuildState guild, GuildMember member, CancellationToken ct)
    {
        var allianceId = guild.AllianceId > 0
            ? guild.AllianceId
            : await _alliances.GetAllianceIdForGuildAsync(guild.Id, ct).ConfigureAwait(false);
        if (allianceId <= 0)
        {
            return;
        }

        var alliance = await _alliances.GetAllianceInfoAsync(allianceId, ct).ConfigureAwait(false);
        if (alliance is null)
        {
            return;
        }

        var packet = V113AlliancePackets.UpdateAllianceMember(allianceId, guild.Id, member.CharacterId, member.Level, member.JobId);
        foreach (var guildId in alliance.GuildIds)
        {
            if (guildId == guild.Id)
            {
                continue;
            }

            var otherGuild = await _guilds.GetGuildAsync(guildId, ct).ConfigureAwait(false);
            if (otherGuild is null)
            {
                continue;
            }

            foreach (var otherMember in otherGuild.Members)
            {
                if (!otherMember.IsOnline)
                {
                    continue;
                }

                try
                {
                    await _sessions.SendToCharacterAsync(otherMember.CharacterId, packet, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }
        }
    }

    private async Task HandleCreateAsync(
        PacketReader reader,
        Player player,
        int channel,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        string guildName;
        try
        {
            guildName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _guilds.CreateGuildAsync(player, guildName, channel, ct);
        if (!result.Succeeded || result.Guild is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.ToGenericStatus(result.Status)), ct);
            return;
        }

        await sendSelf(V113GuildPackets.ShowGuildInfo(result.Guild), ct);
        await sendSelf(V113ShopPackets.UpdateMeso(player.Character.Meso), ct);
        await sendSelf(V113GuildPackets.UpdateGuildPoints(result.Guild.Id, result.Guild.GuildPoints), ct);
    }

    private async Task HandleInviteAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        string targetName;
        try
        {
            targetName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var target = await _sessions.FindOnlinePlayerByNameAsync(targetName, ct);
        if (target is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.StatusNotInChannel), ct);
            return;
        }

        var result = await _guilds.InviteMemberAsync(player.Character.Id, target.ToGuildMember(), ct);
        if (!result.Succeeded || result.Guild is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.ToGenericStatus(result.Status)), ct);
            return;
        }

        await _sessions.SendToCharacterAsync(
            target.CharacterId,
            V113GuildPackets.GuildInvite(result.Guild.Id, player.Character.Name),
            ct);
    }

    private async Task HandleAcceptedAsync(
        PacketReader reader,
        Player player,
        int channel,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int guildId;
        int characterId;
        try
        {
            guildId = reader.ReadInt();
            characterId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (characterId != player.Character.Id)
        {
            return;
        }

        var result = await _guilds.AcceptInviteAsync(player, guildId, channel, ct);
        if (!result.Succeeded || result.Guild is null || result.Target is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.ToGenericStatus(result.Status)), ct);
            return;
        }

        await sendSelf(V113GuildPackets.ShowGuildInfo(result.Guild), ct);
        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.NewGuildMember(result.Target),
            ct);
    }

    private async Task HandleLeavingAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int characterId;
        string name;
        try
        {
            characterId = reader.ReadInt();
            name = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (characterId != player.Character.Id || !string.Equals(name, player.Character.Name, StringComparison.Ordinal))
        {
            return;
        }

        var result = await _guilds.LeaveGuildAsync(player, ct);
        if (!result.Succeeded || result.Target is null)
        {
            return;
        }

        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.MemberLeft(result.Target, expelled: false),
            ct);
        await sendSelf(V113GuildPackets.ShowGuildInfo(null), ct);
    }

    private async Task HandleExpelAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int targetId;
        string targetName;
        try
        {
            targetId = reader.ReadInt();
            targetName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _guilds.ExpelMemberAsync(player, targetId, targetName, ct);
        if (!result.Succeeded || result.Target is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.ToGenericStatus(result.Status)), ct);
            return;
        }

        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.MemberLeft(result.Target, expelled: true),
            ct);
        await _sessions.UpdateGuildStatusAsync(
            result.Target.CharacterId,
            0,
            Guild.DefaultMemberRank,
            Guild.DefaultAllianceRank,
            ct);
        await _sessions.SendToCharacterAsync(result.Target.CharacterId, V113GuildPackets.ShowGuildInfo(null), ct);
    }

    private async Task HandleChangeRankTitlesAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var titles = new string[Guild.RankCount];
        try
        {
            for (var i = 0; i < titles.Length; i++)
            {
                titles[i] = reader.ReadMapleString();
            }
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _guilds.ChangeRankTitlesAsync(player, titles, ct);
        if (!result.Succeeded || result.Guild is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.ToGenericStatus(result.Status)), ct);
            return;
        }

        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.RankTitleChange(result.Guild.Id, result.Guild.RankTitles),
            ct);
    }

    private async Task HandleChangeRankAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int targetId;
        byte newRank;
        try
        {
            targetId = reader.ReadInt();
            newRank = reader.ReadByte();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _guilds.ChangeRankAsync(player, targetId, newRank, ct);
        if (!result.Succeeded || result.Target is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.ToGenericStatus(result.Status)), ct);
            return;
        }

        if (targetId == player.Character.Id)
        {
            player.ChangeGuildRank(newRank);
        }
        else
        {
            await _sessions.UpdateGuildStatusAsync(
                targetId,
                result.Target.GuildId,
                newRank,
                result.Target.AllianceRank,
                ct);
        }

        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.ChangeRank(result.Target),
            ct);
    }

    private async Task HandleChangeEmblemAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        GuildEmblem emblem;
        try
        {
            emblem = new GuildEmblem
            {
                LogoBackground = reader.ReadShort(),
                LogoBackgroundColor = reader.ReadByte(),
                Logo = reader.ReadShort(),
                LogoColor = reader.ReadByte(),
            };
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _guilds.ChangeEmblemAsync(player, emblem, ct);
        if (!result.Succeeded || result.Guild is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.ToGenericStatus(result.Status)), ct);
            return;
        }

        await sendSelf(V113ShopPackets.UpdateMeso(player.Character.Meso), ct);
        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.GuildEmblemChange(result.Guild.Id, result.Guild.Emblem),
            ct);
    }

    private async Task HandleChangeNoticeAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        string notice;
        try
        {
            notice = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _guilds.ChangeNoticeAsync(player, notice, ct);
        if (!result.Succeeded || result.Guild is null)
        {
            await sendSelf(V113GuildPackets.GenericGuildMessage(V113GuildPackets.ToGenericStatus(result.Status)), ct);
            return;
        }

        await BroadcastAsync(
            result,
            player.Character.Id,
            sendSelf,
            () => V113GuildPackets.GuildNotice(result.Guild.Id, result.Guild.Notice),
            ct);
    }

    private async Task BroadcastAsync(
        GuildCommandResult result,
        int currentCharacterId,
        Func<byte[], CancellationToken, Task> sendSelf,
        Func<byte[]> buildPacket,
        CancellationToken ct)
    {
        var packet = buildPacket();
        foreach (var recipientId in result.Recipients.Distinct())
        {
            if (recipientId == currentCharacterId)
            {
                await sendSelf(packet, ct);
                continue;
            }

            try
            {
                await _sessions.SendToCharacterAsync(recipientId, packet, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Guild broadcasts are best effort; stale sessions are owned by the central hook.
            }
        }
    }

    private async Task BroadcastRemoteOnlyAsync(
        GuildCommandResult result,
        int currentCharacterId,
        byte[] packet,
        CancellationToken ct)
    {
        foreach (var recipientId in result.Recipients.Distinct())
        {
            if (recipientId == currentCharacterId)
            {
                continue;
            }

            try
            {
                await _sessions.SendToCharacterAsync(recipientId, packet, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
