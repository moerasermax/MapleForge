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

    /// <summary>WZ <c>prop</c>（預設 100；Java <c>makeChanceResult</c>：100 或 nextInt(99) &lt; prop）。</summary>
    public int Prop { get; init; } = 100;

    /// <summary>WZ <c>damage</c>（預設 100）。</summary>
    public int Damage { get; init; } = 100;

    /// <summary>P111：攻擊命中時附帶的怪物異常狀態與數值（Java <c>MapleStatEffect.monsterStatus</c>）。</summary>
    public IReadOnlyDictionary<World.MonsterStatus, int> MonsterStatuses { get; init; } = new Dictionary<World.MonsterStatus, int>();

    /// <summary>P113：對照 Java <c>MapleStatEffect.isPoison</c>（列舉技能且為技能效果）——中毒狀態是否計算持續傷害。</summary>
    public bool IsPoison => IsSkill && SourceId is 2111003 or 2101005 or 2111006 or 2121003 or 2221003
        or 3111003 or 5211004 or 12111005 or 22161002;

    public bool HasBuffStats => Statups.Count > 0;

    public DateTimeOffset? GetExpiresAt(DateTimeOffset startedAt)
        => DurationMilliseconds > 0 ? startedAt.AddMilliseconds(DurationMilliseconds) : null;
}
