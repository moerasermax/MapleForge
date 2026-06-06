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
    private readonly IChatOnlineRegistry _online;
    private readonly IPartyRegistry _parties;

    public ChatService(IChatOnlineRegistry online, IPartyRegistry parties)
    {
        _online = online;
        _parties = parties;
    }

    public void RegisterOnline(
        Character character,
        int channel,
        Func<byte[], CancellationToken, Task> sendPacket)
    {
        _online.Register(new ChatOnlinePlayer(
            character.Id,
            character.Name,
            channel,
            character,
            sendPacket));
    }

    public ChatOnlinePlayer? DeregisterOnline(int characterId) =>
        _online.Deregister(characterId);

    public ChatOnlinePlayer? FindOnlineByName(string characterName) =>
        _online.FindByName(characterName);

    public ChatOnlinePlayer? FindOnlineById(int characterId) =>
        _online.FindById(characterId);

    public IReadOnlyList<ChatRecipient> GetRecipients(
        Character sender,
        GroupChatKind kind,
        IReadOnlyList<int> clientRecipientIds)
    {
        return kind switch
        {
            GroupChatKind.Buddy => GetBuddyRecipients(sender, clientRecipientIds),
            GroupChatKind.Party => GetPartyRecipients(sender),
            // Guild/alliance chat is reserved until guild runtime state is ported.
            GroupChatKind.Guild => Array.Empty<ChatRecipient>(),
            GroupChatKind.Alliance => Array.Empty<ChatRecipient>(),
            _ => Array.Empty<ChatRecipient>(),
        };
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

    private static ChatRecipient ToRecipient(ChatOnlinePlayer player) =>
        new(player.CharacterId, player.Name, player.Channel, player.Character);
}
