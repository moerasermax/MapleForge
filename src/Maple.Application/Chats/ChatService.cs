using Maple.Application.Alliances;
using Maple.Application.Guilds;
using Maple.Application.OnlinePlayers;
using Maple.Application.Parties;
using Maple.Core.Characters;

namespace Maple.Application.Chats;

public enum GroupChatKind : byte
{
    Buddy = 0,
    Party = 1,
    Guild = 2,
    Alliance = 3,
}

public sealed record ChatRecipient(int CharacterId, string Name, int Channel, Character Character);

public sealed class ChatService
{
    private readonly IOnlinePlayerRegistry _online;
    private readonly IPartyRegistry _parties;
    private readonly IGuildRegistry _guilds;
    private readonly AllianceService _alliances;

    public ChatService(IOnlinePlayerRegistry online, IPartyRegistry parties, IGuildRegistry guilds, AllianceService alliances)
    {
        _online = online;
        _parties = parties;
        _guilds = guilds;
        _alliances = alliances;
    }

    public OnlinePlayer? FindOnlineByName(string characterName) =>
        _online.FindByName(characterName);

    public OnlinePlayer? FindOnlineById(int characterId) =>
        _online.FindById(characterId);

    public async ValueTask<IReadOnlyList<ChatRecipient>> GetRecipientsAsync(
        Character sender,
        GroupChatKind kind,
        IReadOnlyList<int> clientRecipientIds,
        CancellationToken ct = default)
    {
        return kind switch
        {
            GroupChatKind.Buddy => GetBuddyRecipients(sender, clientRecipientIds),
            GroupChatKind.Party => GetPartyRecipients(sender),
            GroupChatKind.Guild => await GetGuildRecipientsAsync(sender, ct).ConfigureAwait(false),
            GroupChatKind.Alliance => await GetAllianceRecipientsAsync(sender, ct).ConfigureAwait(false),
            _ => Array.Empty<ChatRecipient>(),
        };
    }

    private async ValueTask<IReadOnlyList<ChatRecipient>> GetGuildRecipientsAsync(Character sender, CancellationToken ct)
    {
        if (sender.GuildId <= 0)
        {
            return Array.Empty<ChatRecipient>();
        }

        var guild = await _guilds.GetGuildForCharacterAsync(sender.Id, ct).ConfigureAwait(false);
        if (guild is null)
        {
            return Array.Empty<ChatRecipient>();
        }

        var recipients = new List<ChatRecipient>(guild.Members.Count);
        foreach (var member in guild.Members)
        {
            if (member.CharacterId == sender.Id)
            {
                continue;
            }

            var online = _online.FindById(member.CharacterId);
            if (online is not null)
            {
                recipients.Add(ToRecipient(online));
            }
        }

        return recipients;
    }

    /// <summary>
    /// 對照 Java <c>World.Alliance.allianceChat</c>：sender 所屬公會的同盟裡，每個公會的每個
    /// 在線成員（排除發送者本人）都是收件人。
    /// </summary>
    /// <remarks>
    /// 同盟成員關係的權威來源是 <see cref="AllianceService"/> 內部的 guildId→allianceId 對照
    /// （<c>GetAllianceIdForGuildAsync</c>），不是 <see cref="GuildState.AllianceId"/>——後者只在
    /// 少數封包建構情境被臨時投影，從未真正寫回 <see cref="IGuildRegistry"/>。這裡沿用
    /// <c>V113AllianceHandler.ResolveAllianceIdAsync</c> 已驗證過的「先看 guild.AllianceId 當快取，
    /// 沒有再問 AllianceService」解析順序，避免依賴不可靠的欄位。
    /// </remarks>
    private async ValueTask<IReadOnlyList<ChatRecipient>> GetAllianceRecipientsAsync(Character sender, CancellationToken ct)
    {
        if (sender.GuildId <= 0)
        {
            return Array.Empty<ChatRecipient>();
        }

        var senderGuild = await _guilds.GetGuildForCharacterAsync(sender.Id, ct).ConfigureAwait(false);
        if (senderGuild is null)
        {
            return Array.Empty<ChatRecipient>();
        }

        var allianceId = senderGuild.AllianceId > 0
            ? senderGuild.AllianceId
            : await _alliances.GetAllianceIdForGuildAsync(senderGuild.Id, ct).ConfigureAwait(false);
        if (allianceId <= 0)
        {
            return Array.Empty<ChatRecipient>();
        }

        var alliance = await _alliances.GetAllianceInfoAsync(allianceId, ct).ConfigureAwait(false);
        if (alliance is null)
        {
            return Array.Empty<ChatRecipient>();
        }

        var recipients = new List<ChatRecipient>();
        foreach (var guildId in alliance.GuildIds)
        {
            var guild = await _guilds.GetGuildAsync(guildId, ct).ConfigureAwait(false);
            if (guild is null)
            {
                continue;
            }

            foreach (var member in guild.Members)
            {
                if (member.CharacterId == sender.Id)
                {
                    continue;
                }

                var online = _online.FindById(member.CharacterId);
                if (online is not null)
                {
                    recipients.Add(ToRecipient(online));
                }
            }
        }

        return recipients;
    }

    private IReadOnlyList<ChatRecipient> GetPartyRecipients(Character sender)
    {
        var party = _parties.GetPartyForCharacter(sender.Id);
        if (party is null)
        {
            return Array.Empty<ChatRecipient>();
        }

        var recipients = new List<ChatRecipient>(party.Members.Count);
        foreach (var member in party.Members)
        {
            if (member.CharacterId == sender.Id)
            {
                continue;
            }

            var online = _online.FindById(member.CharacterId);
            if (online is not null)
            {
                recipients.Add(ToRecipient(online));
            }
        }

        return recipients;
    }

    private IReadOnlyList<ChatRecipient> GetBuddyRecipients(
        Character sender,
        IReadOnlyList<int> clientRecipientIds)
    {
        if (clientRecipientIds.Count == 0)
        {
            return Array.Empty<ChatRecipient>();
        }

        var recipients = new List<ChatRecipient>(clientRecipientIds.Count);
        var seen = new HashSet<int>();
        foreach (var characterId in clientRecipientIds)
        {
            if (!seen.Add(characterId))
            {
                continue;
            }

            var online = _online.FindById(characterId);
            if (online?.Character.BuddyList.ContainsVisible(sender.Id) == true)
            {
                recipients.Add(ToRecipient(online));
            }
        }

        return recipients;
    }

    private static ChatRecipient ToRecipient(OnlinePlayer player) =>
        new(player.CharacterId, player.Name, player.Channel, player.Character);
}
