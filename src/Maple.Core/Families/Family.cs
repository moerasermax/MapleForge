namespace Maple.Core.Families;

public sealed class Family
{
    public int Id { get; set; }

    public int LeaderId { get; set; }

    public string Notice { get; set; } = string.Empty;

    public Dictionary<int, FamilyMember> Members { get; set; } = new();

    public FamilyState Snapshot(IReadOnlyDictionary<int, int>? onlineChannels = null) => new(
        Id,
        LeaderId,
        Notice,
        Members.Values
            .OrderBy(static member => member.CharacterId)
            .Select(member => ToState(member, onlineChannels))
            .ToArray(),
        GetGenerations());

    public FamilyMember? GetMember(int characterId) =>
        characterId > 0 && Members.TryGetValue(characterId, out var member) ? member : null;

    public FamilyMember? GetLeader() => GetMember(LeaderId);

    public bool ContainsMember(int characterId) => Members.ContainsKey(characterId);

    public bool TryAddMember(FamilyMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (member.CharacterId <= 0 || Members.ContainsKey(member.CharacterId))
        {
            return false;
        }

        Members.Add(member.CharacterId, member);
        return true;
    }

    public bool RemoveMember(int characterId) => Members.Remove(characterId);

    public void SetNotice(string notice) => Notice = notice ?? string.Empty;

    public int GetDescendantCount(int characterId)
    {
        var member = GetMember(characterId);
        return member is null ? 0 : CountDescendants(member, new HashSet<int>());
    }

    public int GetGenerations()
    {
        var leader = GetLeader();
        return leader is null ? 0 : Math.Max(0, GetDepth(leader, new HashSet<int>()) - 1);
    }

    public IReadOnlyList<FamilyMember> GetPedigreeMembers(int characterId)
    {
        var member = GetMember(characterId);
        if (member is null)
        {
            return Array.Empty<FamilyMember>();
        }

        var result = new List<FamilyMember>();
        AddIfPresent(result, GetLeader());

        if (member.SeniorId > 0)
        {
            var senior = GetMember(member.SeniorId);
            if (senior?.SeniorId > 0)
            {
                AddIfPresent(result, GetMember(senior.SeniorId));
            }

            AddIfPresent(result, senior);
        }

        AddIfPresent(result, member);

        if (member.SeniorId > 0)
        {
            var senior = GetMember(member.SeniorId);
            if (senior?.Junior1 > 0 && senior.Junior1 != characterId)
            {
                AddIfPresent(result, GetMember(senior.Junior1));
            }
            else if (senior?.Junior2 > 0 && senior.Junior2 != characterId)
            {
                AddIfPresent(result, GetMember(senior.Junior2));
            }
        }

        AddIfPresent(result, GetMember(member.Junior1));
        AddIfPresent(result, GetMember(member.Junior2));
        AddJuniorChildren(result, member.Junior1);
        AddJuniorChildren(result, member.Junior2);

        return result
            .DistinctBy(static m => m.CharacterId)
            .ToArray();
    }

    private void AddJuniorChildren(List<FamilyMember> result, int juniorId)
    {
        var junior = GetMember(juniorId);
        if (junior is null)
        {
            return;
        }

        AddIfPresent(result, GetMember(junior.Junior1));
        AddIfPresent(result, GetMember(junior.Junior2));
    }

    private static void AddIfPresent(List<FamilyMember> result, FamilyMember? member)
    {
        if (member is not null)
        {
            result.Add(member);
        }
    }

    private int CountDescendants(FamilyMember member, HashSet<int> visited)
    {
        if (!visited.Add(member.CharacterId))
        {
            return 0;
        }

        var count = 0;
        foreach (var juniorId in new[] { member.Junior1, member.Junior2 })
        {
            var junior = GetMember(juniorId);
            if (junior is null)
            {
                continue;
            }

            count += 1 + CountDescendants(junior, visited);
        }

        return count;
    }

    private int GetDepth(FamilyMember member, HashSet<int> visited)
    {
        if (!visited.Add(member.CharacterId))
        {
            return 0;
        }

        var childDepth = 0;
        foreach (var juniorId in new[] { member.Junior1, member.Junior2 })
        {
            var junior = GetMember(juniorId);
            if (junior is not null)
            {
                childDepth = Math.Max(childDepth, GetDepth(junior, visited));
            }
        }

        return 1 + childDepth;
    }

    private static FamilyMemberState ToState(FamilyMember member, IReadOnlyDictionary<int, int>? onlineChannels)
    {
        var channel = -1;
        var online = onlineChannels is not null && onlineChannels.TryGetValue(member.CharacterId, out channel);
        return new FamilyMemberState(
            member.CharacterId,
            member.Name,
            member.SeniorId,
            member.Junior1,
            member.Junior2,
            member.CurrentRep,
            member.TotalRep,
            member.Level,
            member.Job,
            online,
            channel);
    }
}

public sealed record FamilyState(
    int Id,
    int LeaderId,
    string Notice,
    IReadOnlyList<FamilyMemberState> Members,
    int Generations)
{
    public FamilyMemberState? GetMember(int characterId) =>
        Members.FirstOrDefault(member => member.CharacterId == characterId);

    public FamilyMemberState? Leader => GetMember(LeaderId);
}

public sealed record FamilyMemberState(
    int CharacterId,
    string Name,
    int SeniorId,
    int Junior1,
    int Junior2,
    int CurrentRep,
    int TotalRep,
    int Level,
    int Job,
    bool IsOnline,
    int Channel)
{
    public int JuniorCount => (Junior1 > 0 ? 1 : 0) + (Junior2 > 0 ? 1 : 0);
}
