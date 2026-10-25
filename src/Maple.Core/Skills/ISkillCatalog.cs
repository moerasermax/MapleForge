namespace Maple.Core.Skills;

public interface ISkillCatalog
{
    MapleSkill? GetSkill(int skillId);

    /// <summary>P167：Java <c>SkillFactory.getSkillsByJob(job)</c>（Skill.wz/&lt;job&gt;.img/skill 的所有技能 ID）。</summary>
    IReadOnlyList<int> GetSkillIdsByJob(int job) => [];
}
