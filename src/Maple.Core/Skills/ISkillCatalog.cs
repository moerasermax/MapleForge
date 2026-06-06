namespace Maple.Core.Skills;

public interface ISkillCatalog
{
    MapleSkill? GetSkill(int skillId);
}
