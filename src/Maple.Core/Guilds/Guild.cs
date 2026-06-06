namespace Maple.Core.Guilds;

public sealed class Guild
{
    public const int RankCount = 5;
    public const int InitialCapacity = 10;
    public const int MaximumCapacity = 100;
    public const byte LeaderRank = 1;
    public const byte JuniorMasterRank = 2;
    public const byte DefaultMemberRank = 5;
    public const byte DefaultAllianceRank = 5;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int LeaderId { get; set; }

    public int GuildPoints { get; set; }

    public GuildEmblem Emblem { get; set; } = new();

    public int Capacity { get; set; } = InitialCapacity;

    public string Notice { get; set; } = string.Empty;

    public int Signature { get; set; }

    public int AllianceId { get; set; }

    public List<string> RankTitles { get; set; } =
    [
        "公會長",
        "公會副會長",
        "公會成員",
        "公會成員",
        "公會成員",
    ];

    public List<GuildMember> Members { get; set; } = new(InitialCapacity);

    public bool IsFull => Members.Count >= Capacity;

    public GuildState Snapshot() => new(
        Id,
        Name,
        LeaderId,
        GuildPoints,
        new GuildEmblem
        {
            LogoBackground = Emblem.LogoBackground,
            LogoBackgroundColor = Emblem.LogoBackgroundColor,
            Logo = Emblem.Logo,
            LogoColor = Emblem.LogoColor,
        },
        Capacity,
        Notice,
        Signature,
        AllianceId,
        NormalizedRankTitles(),
        Members.Select(static m => m.Clone()).ToArray());

    public GuildMember? GetMember(int characterId) =>
        Members.FirstOrDefault(m => m.CharacterId == characterId);

    public bool ContainsMember(int characterId) =>
        Members.Any(m => m.CharacterId == characterId);

    public bool TryAddMember(GuildMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (member.CharacterId <= 0 || IsFull || ContainsMember(member.CharacterId))
        {
            return false;
        }

        member.GuildId = Id;
        Members.Add(member);
        SortMembers();
        return true;
    }

    public bool TryRemoveMember(int characterId, out GuildMember? removed)
    {
        var index = Members.FindIndex(m => m.CharacterId == characterId);
        if (index < 0)
        {
            removed = null;
            return false;
        }

        removed = Members[index].Clone();
        Members.RemoveAt(index);
        return true;
    }

    public bool TryChangeRank(int characterId, byte newRank, out GuildMember? changed)
    {
        var member = GetMember(characterId);
        if (member is null)
        {
            changed = null;
            return false;
        }

        member.GuildRank = newRank;
        SortMembers();
        changed = member.Clone();
        return true;
    }

    public bool TryChangeRankTitles(IReadOnlyList<string> titles)
    {
        if (titles.Count != RankCount || titles.Any(static t => string.IsNullOrWhiteSpace(t)))
        {
            return false;
        }

        RankTitles = titles.Take(RankCount).ToList();
        return true;
    }

    public bool TryIncreaseCapacity(int amount = 5)
    {
        if (amount <= 0 || Capacity >= MaximumCapacity || Capacity + amount > MaximumCapacity)
        {
            return false;
        }

        Capacity += amount;
        return true;
    }

    public void SetNotice(string notice) =>
        Notice = notice ?? string.Empty;

    public void SetEmblem(GuildEmblem emblem) =>
        Emblem = emblem;

    public void GainGuildPoints(int amount)
    {
        var next = (long)GuildPoints + amount;
        GuildPoints = next < 0 ? 0 : next > int.MaxValue ? int.MaxValue : (int)next;
    }

    private IReadOnlyList<string> NormalizedRankTitles()
    {
        var titles = RankTitles.Take(RankCount).ToList();
        while (titles.Count < RankCount)
        {
            titles.Add(string.Empty);
        }

        return titles;
    }

    private void SortMembers()
    {
        Members.Sort(static (left, right) =>
        {
            var byRank = left.GuildRank.CompareTo(right.GuildRank);
            return byRank != 0 ? byRank : string.CompareOrdinal(left.Name, right.Name);
        });
    }
}

public sealed record GuildState(
    int Id,
    string Name,
    int LeaderId,
    int GuildPoints,
    GuildEmblem Emblem,
    int Capacity,
    string Notice,
    int Signature,
    int AllianceId,
    IReadOnlyList<string> RankTitles,
    IReadOnlyList<GuildMember> Members)
{
    public GuildMember? GetMember(int characterId) =>
        Members.FirstOrDefault(m => m.CharacterId == characterId);
}
