namespace Maple.Core.Skills;

public sealed class MapleSkill
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int MasterLevel { get; init; }

    public bool IsChargeSkill { get; init; }

    public bool IsTimeLimited { get; init; }

    /// <summary>P167：WZ <c>invisible</c>（Java <c>Skill.isInvisible</c>）。</summary>
    public bool IsInvisible { get; init; }

    /// <summary>P114：WZ <c>elemAttr</c> 首字元（Java <c>Skill.element</c>）。</summary>
    public World.Element Element { get; init; } = World.Element.Neutral;

    public IReadOnlyList<MapleStatEffect> Effects { get; init; } = Array.Empty<MapleStatEffect>();

    public int MaxLevel => Effects.Count;

    public bool IsFourthJob => IsFourthJobSkillId(Id, MasterLevel);

    public MapleStatEffect? GetEffect(int level)
    {
        if (Effects.Count == 0)
        {
            return null;
        }

        if (level <= 0)
        {
            return Effects[0];
        }

        return level > Effects.Count ? Effects[^1] : Effects[level - 1];
    }

    public static bool IsFourthJobSkillId(int skillId, int masterLevel = 0)
    {
        var skillJob = skillId / 10000;
        if (skillJob >= 2212 && skillJob < 3000)
        {
            return skillJob % 10 >= 7;
        }

        if (skillJob is >= 430 and <= 434)
        {
            return skillJob % 10 == 4 || masterLevel > 0;
        }

        return skillJob % 10 == 2;
    }
}
