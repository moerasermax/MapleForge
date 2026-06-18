namespace Maple.Core.Families;

public interface IFamilyRepository
{
    Task<Family?> FindByIdAsync(int familyId, CancellationToken ct = default);

    Task SaveAsync(Family family, CancellationToken ct = default);

    Task DeleteAsync(int familyId, CancellationToken ct = default);
}
