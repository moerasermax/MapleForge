namespace Maple.Core.Skills;

/// <summary>角色持久技能資料；對齊 Java SkillEntry 的 level/masterLevel/expiration。</summary>
public sealed class CharacterSkillRecord
{
    public int SkillId { get; set; }

    public byte Level { get; set; }

    public byte MasterLevel { get; set; }

    public long Expiration { get; set; } = -1;
}
