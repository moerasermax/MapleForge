using Maple.Core.Guilds;
using MongoDB.Driver;

namespace Maple.Persistence.Guilds;

public sealed class MongoGuildRepository : IGuildRepository
{
    private const string CollectionName = "guilds";
    private const string SequenceName = "guilds";

    private readonly IMongoCollection<Guild> _collection;
    private readonly MongoSequenceGenerator _sequences;

    public MongoGuildRepository(IMongoDatabase database, MongoSequenceGenerator sequences)
    {
        _collection = database.GetCollection<Guild>(CollectionName);
        _sequences = sequences;

        var nameIndex = new CreateIndexModel<Guild>(
            Builders<Guild>.IndexKeys.Ascending(g => g.Name),
            new CreateIndexOptions { Unique = true, Name = "ux_guilds_name" });
        _collection.Indexes.CreateOne(nameIndex);
    }

    public async Task<IReadOnlyList<Guild>> GetAllAsync(CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<Guild>.Filter.Empty)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Guild?> FindByIdAsync(int guildId, CancellationToken ct = default)
    {
        return await _collection
            .Find(g => g.Id == guildId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        return await _collection
            .Find(g => g.Name == name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(Guild guild, CancellationToken ct = default)
    {
        await AssignIdIfNeededAsync(guild, ct).ConfigureAwait(false);
        await _collection.InsertOneAsync(guild, cancellationToken: ct).ConfigureAwait(false);
        await _sequences.EnsureAtLeastAsync(SequenceName, guild.Id, ct).ConfigureAwait(false);
    }

    public Task UpdateAsync(Guild guild, CancellationToken ct = default)
    {
        return _collection.ReplaceOneAsync(
            g => g.Id == guild.Id,
            guild,
            new ReplaceOptions { IsUpsert = false },
            ct);
    }

    public Task DeleteAsync(int guildId, CancellationToken ct = default)
    {
        return _collection.DeleteOneAsync(g => g.Id == guildId, ct);
    }

    private async Task AssignIdIfNeededAsync(Guild guild, CancellationToken ct)
    {
        if (guild.Id > 0)
        {
            return;
        }

        var currentMax = await _collection
            .Find(Builders<Guild>.Filter.Empty)
            .SortByDescending(g => g.Id)
            .Limit(1)
            .Project(g => g.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        guild.Id = await _sequences.NextAsync(SequenceName, currentMax, ct).ConfigureAwait(false);
    }
}
