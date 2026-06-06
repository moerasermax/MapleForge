using Maple.Core.Skills;

namespace Maple.Application.Skills;

/// <summary>最小技能資料源；供測試、bootstrap 或 WZ 尚未可用時明確注入。</summary>
public sealed class InMemorySkillCatalog : ISkillCatalog
{
    private readonly IReadOnlyDictionary<int, MapleSkill> _skills;

    public InMemorySkillCatalog(IEnumerable<MapleSkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        _skills = skills.ToDictionary(static s => s.Id);
    }

    public MapleSkill? GetSkill(int skillId)
        => _skills.TryGetValue(skillId, out var skill) ? skill : null;
}
