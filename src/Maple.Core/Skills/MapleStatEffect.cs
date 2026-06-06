namespace Maple.Core.Skills;

/// <summary>技能單一等級效果；欄位名稱對齊 Java MapleStatEffect.loadFromData。</summary>
public sealed class MapleStatEffect
{
    public int SourceId { get; init; }

    public byte Level { get; init; }

    public bool IsSkill { get; init; } = true;

    public bool IsOverTime { get; init; }

    public int DurationMilliseconds { get; init; }

    public short Hp { get; init; }

    public short Mp { get; init; }

    public double HpRate { get; init; }

    public double MpRate { get; init; }

    public short HpCon { get; init; }

    public short MpCon { get; init; }

    public short Watk { get; init; }

    public short Wdef { get; init; }

    public short Matk { get; init; }

    public short Mdef { get; init; }

    public short Acc { get; init; }

    public short Avoid { get; init; }

    public short Speed { get; init; }

    public short Jump { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Z { get; init; }

    public int CooldownSeconds { get; init; }

    public int MoveTo { get; init; } = -1;

    public IReadOnlyList<BuffStatValue> Statups { get; init; } = Array.Empty<BuffStatValue>();

    public bool IsCombo { get; init; }

    public bool IsFinalAttack { get; init; }

    public bool IsFieldObjectSkill { get; init; }

    public bool HasBuffStats => Statups.Count > 0;

    public DateTimeOffset? GetExpiresAt(DateTimeOffset startedAt)
        => DurationMilliseconds > 0 ? startedAt.AddMilliseconds(DurationMilliseconds) : null;
}
