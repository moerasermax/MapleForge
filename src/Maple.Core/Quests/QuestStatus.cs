namespace Maple.Core.Quests;

/// <summary>OdinMS MapleQuestStatus status values.</summary>
public enum QuestStatus : byte
{
    NotStarted = 0,
    Started = 1,
    Completed = 2,
}

/// <summary>Persisted mob kill counter for a started quest.</summary>
public sealed class QuestMobKillRecord
{
    public int MobId { get; set; }

    public int Count { get; set; }
}

/// <summary>
/// Persisted quest state, aligned to Java MapleQuestStatus:
/// quest/status/killedMobs/npc/completionTime/forfeited/customData.
/// </summary>
public sealed class QuestRecord
{
    public int QuestId { get; set; }

    public byte Status { get; set; }

    public int Npc { get; set; }

    public long CompletionTimeUnixMillis { get; set; }

    public int Forfeited { get; set; }

    public string? CustomData { get; set; }

    public List<QuestMobKillRecord> MobKills { get; set; } = new();
}

/// <summary>Persisted MapleCharacter infoquest entry.</summary>
public sealed class QuestInfoRecord
{
    public int QuestId { get; set; }

    public string Data { get; set; } = string.Empty;
}
