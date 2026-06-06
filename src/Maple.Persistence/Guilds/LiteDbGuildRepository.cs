using LiteDB;
using Maple.Core.Guilds;

namespace Maple.Persistence.Guilds;

public sealed class LiteDbGuildRepository : IGuildRepository
{
    private readonly ILiteCollection<Guild> _collection;

    public LiteDbGuildRepository(LiteDatabase database)
    {
        _collection = database.GetCollection<Guild>("guilds");
        _collection.EnsureIndex(g => g.Name, unique: true);
    }

    public Task<IReadOnlyList<Guild>> GetAllAsync(CancellationToken ct = default)
    {
        var guilds = _collection.FindAll().ToList();
        return Task.FromResult<IReadOnlyList<Guild>>(guilds);
    }

    public Task<Guild?> FindByIdAsync(int guildId, CancellationToken ct = default)
    {
        var guild = _collection.FindById(guildId);
        return Task.FromResult<Guild?>(guild);
    }

    public Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var guild = _collection.FindOne(g => g.Name == name);
        return Task.FromResult<Guild?>(guild);
    }

    public Task AddAsync(Guild guild, CancellationToken ct = default)
    {
        _collection.Insert(guild);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Guild guild, CancellationToken ct = default)
    {
        _collection.Update(guild);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int guildId, CancellationToken ct = default)
    {
        _collection.Delete(guildId);
        return Task.CompletedTask;
    }
}
