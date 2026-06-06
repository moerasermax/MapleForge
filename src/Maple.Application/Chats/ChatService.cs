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

    public ChatService(IOnlinePlayerRegistry online, IPartyRegistry parties, IGuildRegistry guilds)
    {
        _online = online;
        _parties = parties;
        _guilds = guilds;
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
            // Alliance chat is reserved until alliance runtime state is ported.
            GroupChatKind.Alliance => Array.Empty<ChatRecipient>(),
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
