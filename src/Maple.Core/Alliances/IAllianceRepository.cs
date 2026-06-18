namespace Maple.Core.Alliances;

public interface IAllianceRepository
{
    Task<Alliance?> FindByIdAsync(int allianceId, CancellationToken ct = default);

    Task SaveAsync(Alliance alliance, CancellationToken ct = default);

    Task DeleteAsync(int allianceId, CancellationToken ct = default);
}
