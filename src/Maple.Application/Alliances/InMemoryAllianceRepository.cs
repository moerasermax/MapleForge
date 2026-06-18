using System.Collections.Concurrent;
using Maple.Core.Alliances;

namespace Maple.Application.Alliances;

public sealed class InMemoryAllianceRepository : IAllianceRepository
{
    private readonly ConcurrentDictionary<int, Alliance> _alliances = new();

    public Task<Alliance?> FindByIdAsync(int allianceId, CancellationToken ct = default)
        => Task.FromResult(_alliances.TryGetValue(allianceId, out var a) ? a : null);

    public Task SaveAsync(Alliance alliance, CancellationToken ct = default)
    {
        _alliances[alliance.Id] = alliance;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int allianceId, CancellationToken ct = default)
    {
        _alliances.TryRemove(allianceId, out _);
        return Task.CompletedTask;
    }
}
