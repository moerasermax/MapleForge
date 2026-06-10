namespace Maple.Core.Characters;

/// <summary>玩家技能宏文件模型；封包欄位排列由版本 adapter 負責。</summary>
public sealed class SkillMacroRecord
{
    public int Position { get; set; }

    public string Name { get; set; } = string.Empty;

    public byte Shout { get; set; }

    public int Skill1 { get; set; }

    public int Skill2 { get; set; }

    public int Skill3 { get; set; }
}
