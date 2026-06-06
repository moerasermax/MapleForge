namespace Maple.Core.Guilds;

public interface IGuildRepository
{
    Task<IReadOnlyList<Guild>> GetAllAsync(CancellationToken ct = default);

    Task<Guild?> FindByIdAsync(int guildId, CancellationToken ct = default);

    Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default);

    Task AddAsync(Guild guild, CancellationToken ct = default);

    Task UpdateAsync(Guild guild, CancellationToken ct = default);

    Task DeleteAsync(int guildId, CancellationToken ct = default);
}
