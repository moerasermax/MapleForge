namespace Maple.Core.World;

/// <summary>怪物身上的技能（WZ Mob.wz info/skill/{i}/skill、level）。</summary>
public sealed record MobSkillRef(int SkillId, int Level);

/// <summary>怪物技能作用範圍（WZ lt/rb，相對怪物座標；面向右時 Java 會左右翻轉）。</summary>
public sealed record MobSkillArea(short LtX, short LtY, short RbX, short RbY);

/// <summary>
/// 怪物技能定義（P122，對照 Java <c>MobSkill</c>／<c>DumpMobSkills</c>：Skill.wz MobSkill.img/{id}/level/{lv}）。
/// 時間單位已換成毫秒（Java interval×1000、time×1000）；Prop 為 0~1（Java prop/100f）。
/// </summary>
public sealed record MobSkillDefinition(
    int SkillId,
    int Level,
    int Hp,
    int MpCon,
    int X,
    int Y,
    int DurationMilliseconds,
    int CoolTimeMilliseconds,
    double Prop,
    int Limit,
    int SpawnEffect,
    IReadOnlyList<int> Summons,
    MobSkillArea? Area,
    bool SummonOnce);

public interface IMobSkillCatalog
{
    MobSkillDefinition? Get(int skillId, int level);
}
