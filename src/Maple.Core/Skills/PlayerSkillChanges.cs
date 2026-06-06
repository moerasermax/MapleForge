namespace Maple.Core.Skills;

public enum PlayerSkillApplicationStatus
{
    Applied,
    NotEnoughHp,
    NotEnoughMp,
}

public sealed record PlayerBuffChange(
    int SourceId,
    int DurationMilliseconds,
    DateTimeOffset StartedAt,
    IReadOnlyList<BuffStatValue> Stats);

public sealed record PlayerBuffCancellation(
    int SourceId,
    IReadOnlyList<MapleBuffStat> Stats);

public sealed record PlayerSkillApplication(
    PlayerSkillApplicationStatus Status,
    PlayerBuffChange? Buff);

public sealed record ActiveBuffStat(
    MapleBuffStat Stat,
    int Value,
    int SourceId,
    byte SkillLevel,
    DateTimeOffset StartedAt,
    int DurationMilliseconds)
{
    public DateTimeOffset? ExpiresAt
        => DurationMilliseconds > 0 ? StartedAt.AddMilliseconds(DurationMilliseconds) : null;
}

public sealed record ActiveSkillCooldown(
    int SkillId,
    DateTimeOffset StartedAt,
    int DurationMilliseconds)
{
    public DateTimeOffset ExpiresAt => StartedAt.AddMilliseconds(DurationMilliseconds);
}
