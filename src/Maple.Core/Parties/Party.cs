namespace Maple.Core.Parties;

public sealed class Party
{
    public const int MaxMembers = 6;

    private readonly List<PartyMember> _members = new(MaxMembers);

    public Party(int id, PartyMember leader)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (leader.CharacterId <= 0) throw new ArgumentOutOfRangeException(nameof(leader));

        Id = id;
        LeaderId = leader.CharacterId;
        _members.Add(leader);
    }

    public int Id { get; }

    public int LeaderId { get; private set; }

    public bool IsFull => _members.Count >= MaxMembers;

    public PartyMember? GetMember(int characterId) =>
        _members.FirstOrDefault(m => m.CharacterId == characterId);

    public bool ContainsMember(int characterId) =>
        _members.Any(m => m.CharacterId == characterId);

    public bool TryAddMember(PartyMember member)
    {
        if (member.CharacterId <= 0 || IsFull || ContainsMember(member.CharacterId))
        {
            return false;
        }

        _members.Add(member);
        return true;
    }

    public bool TryRemoveMember(int characterId, out PartyMember? removed)
    {
        var index = _members.FindIndex(m => m.CharacterId == characterId);
        if (index < 0)
        {
            removed = null;
            return false;
        }

        removed = _members[index];
        _members.RemoveAt(index);
        return true;
    }

    public bool TryUpdateMember(PartyMember member)
    {
        var index = _members.FindIndex(m => m.CharacterId == member.CharacterId);
        if (index < 0)
        {
            return false;
        }

        _members[index] = member;
        return true;
    }

    public bool TryChangeLeader(int newLeaderId)
    {
        if (!ContainsMember(newLeaderId))
        {
            return false;
        }

        LeaderId = newLeaderId;
        return true;
    }

    public PartyState Snapshot() => new(Id, LeaderId, _members.ToArray());
}

public sealed record PartyState(int Id, int LeaderId, IReadOnlyList<PartyMember> Members)
{
    public bool IsFull => Members.Count >= Party.MaxMembers;

    public PartyMember? Leader => GetMember(LeaderId);

    public PartyMember? GetMember(int characterId) =>
        Members.FirstOrDefault(m => m.CharacterId == characterId);
}
