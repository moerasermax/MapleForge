using Maple.Core.Families;
using Maple.Core.World;

namespace Maple.Application.Families;

public interface IFamilyRegistry
{
    FamilyState? GetFamilyForCharacter(int characterId);

    FamilyState? GetFamily(int familyId);

    void Register(Family family);

    void Register(Player player, int channel = 1);

    void Unregister(int characterId);
}
