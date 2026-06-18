namespace Maple.Core.Alliances;

public sealed class Alliance
{
    public const int RankCount = 5;
    public const int MaximumGuilds = 5;
    public const int InitialCapacity = 2;
    public const byte LeaderRank = 1;
    public const byte SubLeaderRank = 2;
    public const byte LowestRank = 5;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int LeaderId { get; set; }

    public string Notice { get; set; } = string.Empty;

    public string[] Ranks { get; set; } =
    [
        "公會長",
        "公會副會長",
        "公會成員",
        "公會成員",
        "公會成員",
    ];

    public List<int> GuildIds { get; set; } = new(MaximumGuilds);

    public int Capacity { get; set; } = InitialCapacity;

    public AllianceState Snapshot() => new(
        Id,
        Name,
        LeaderId,
        Notice,
        NormalizedRanks(),
        NormalizedGuildIds(),
        Math.Clamp(Capacity, 0, MaximumGuilds));

    public bool CanInvite => NormalizedGuildIds().Count < Math.Clamp(Capacity, 0, MaximumGuilds);

    public bool ContainsGuild(int guildId) => NormalizedGuildIds().Contains(guildId);

    public bool TryAddGuild(int guildId)
    {
        if (guildId <= 0)
        {
            return false;
        }

        var guildIds = NormalizedGuildIds();
        if (guildIds.Contains(guildId) || guildIds.Count >= Math.Clamp(Capacity, 0, MaximumGuilds))
        {
            return false;
        }

        guildIds.Add(guildId);
        GuildIds = guildIds;
        return true;
    }

    public bool TryRemoveGuild(int guildId, out bool removedLeaderGuild)
    {
        var guildIds = NormalizedGuildIds();
        var index = guildIds.IndexOf(guildId);
        if (index < 0)
        {
            removedLeaderGuild = false;
            return false;
        }

        removedLeaderGuild = index == 0;
        guildIds.RemoveAt(index);
        GuildIds = guildIds;
        return true;
    }

    public bool TryChangeLeader(int characterId, int? leaderGuildId = null)
    {
        if (characterId <= 0 || LeaderId == characterId)
        {
            return false;
        }

        if (leaderGuildId is > 0)
        {
            var guildIds = NormalizedGuildIds();
            var index = guildIds.IndexOf(leaderGuildId.Value);
            if (index < 0)
            {
                return false;
            }

            guildIds.RemoveAt(index);
            guildIds.Insert(0, leaderGuildId.Value);
            GuildIds = guildIds;
        }

        LeaderId = characterId;
        return true;
    }

    public bool TryUpdateRanks(IReadOnlyList<string> ranks)
    {
        if (ranks.Count != RankCount || ranks.Any(static rank => string.IsNullOrWhiteSpace(rank)))
        {
            return false;
        }

        Ranks = ranks.Take(RankCount).ToArray();
        return true;
    }

    public bool TrySetCapacity(int capacity)
    {
        if (capacity < NormalizedGuildIds().Count || capacity > MaximumGuilds)
        {
            return false;
        }

        Capacity = capacity;
        return true;
    }

    public void SetNotice(string notice) => Notice = notice ?? string.Empty;

    private IReadOnlyList<string> NormalizedRanks()
    {
        var ranks = Ranks.Take(RankCount).ToList();
        while (ranks.Count < RankCount)
        {
            ranks.Add(string.Empty);
        }

        return ranks;
    }

    private List<int> NormalizedGuildIds() =>
        GuildIds.Where(static guildId => guildId > 0).Distinct().Take(MaximumGuilds).ToList();
}

public sealed record AllianceState(
    int Id,
    string Name,
    int LeaderId,
    string Notice,
    IReadOnlyList<string> Ranks,
    IReadOnlyList<int> GuildIds,
    int Capacity)
{
    public bool CanInvite => GuildIds.Count < Capacity && GuildIds.Count < Alliance.MaximumGuilds;

    public bool ContainsGuild(int guildId) => GuildIds.Contains(guildId);
}
