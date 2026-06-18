namespace Maple.Core.Skills;

public sealed record SkillBookDefinition(
    int ItemId,
    int[] SkillIds,
    int SuccessRate,
    int ReqSkillLevel,
    int MasterLevel);
