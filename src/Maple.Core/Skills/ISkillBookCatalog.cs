namespace Maple.Core.Skills;

public interface ISkillBookCatalog
{
    SkillBookDefinition? GetByItemId(int itemId);
}
