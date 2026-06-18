using System.Collections.Concurrent;
using Maple.Core.Families;

namespace Maple.Application.Families;

public sealed class InMemoryFamilyRepository : IFamilyRepository
{
    private readonly ConcurrentDictionary<int, Family> _families = new();

    public Task<Family?> FindByIdAsync(int familyId, CancellationToken ct = default) =>
        Task.FromResult(_families.TryGetValue(familyId, out var family) ? family : null);

    public Task SaveAsync(Family family, CancellationToken ct = default)
    {
        _families[family.Id] = family;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int familyId, CancellationToken ct = default)
    {
        _families.TryRemove(familyId, out _);
        return Task.CompletedTask;
    }
}
