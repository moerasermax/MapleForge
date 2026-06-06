namespace Maple.Core.Characters;

/// <summary>Persistent skill state embedded in a character document.</summary>
public sealed class CharacterSkill
{
    public int SkillId { get; set; }

    public byte Level { get; set; }

    public byte MasterLevel { get; set; }

    public long Expiration { get; set; } = -1;
}
