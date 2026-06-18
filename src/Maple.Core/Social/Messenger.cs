namespace Maple.Core.Social;

public sealed class Messenger
{
    public const int MaxMembers = 3;

    public Messenger(int id, MessengerMember firstMember)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (firstMember.CharacterId <= 0) throw new ArgumentOutOfRangeException(nameof(firstMember));

        Id = id;
        Members = new MessengerMember?[MaxMembers];
        Members[0] = firstMember with { Position = 0 };
    }

    private Messenger(int id, MessengerMember?[] members)
    {
        Id = id;
        Members = members;
    }

    public int Id { get; }

    public MessengerMember?[] Members { get; }

    public bool IsEmpty => Members.All(static m => m is null);

    public int GetLowestPosition()
    {
        for (var i = 0; i < Members.Length; i++)
        {
            if (Members[i] is null)
            {
                return i;
            }
        }

        return MaxMembers + 1;
    }

    public MessengerMember? GetMember(int characterId) =>
        Members.FirstOrDefault(m => m?.CharacterId == characterId);

    public MessengerMember? GetMemberByName(string name) =>
        Members.FirstOrDefault(m => m is not null && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool ContainsMember(int characterId) =>
        GetMember(characterId) is not null;

    public bool TryAddMember(MessengerMember member)
    {
        if (member.CharacterId <= 0 || ContainsMember(member.CharacterId))
        {
            return false;
        }

        var position = GetLowestPosition();
        if (position < 0 || position >= MaxMembers)
        {
            return false;
        }

        Members[position] = member with { Position = position };
        return true;
    }

    public bool TryRemoveMember(int characterId, out MessengerMember? removed)
    {
        for (var i = 0; i < Members.Length; i++)
        {
            if (Members[i]?.CharacterId != characterId)
            {
                continue;
            }

            removed = Members[i];
            Members[i] = null;
            return true;
        }

        removed = null;
        return false;
    }

    public Messenger Snapshot() => new(Id, Members.ToArray());
}
