using Maple.Core.Characters;
using MongoDB.Driver;

namespace Maple.Persistence.Characters;

/// <summary>MongoDB-backed character repository.</summary>
public sealed class MongoCharacterRepository : ICharacterRepository
{
    private const string CollectionName = "characters";
    private const string SequenceName = "characters";

    private readonly IMongoCollection<Character> _collection;
    private readonly MongoSequenceGenerator _sequences;

    public MongoCharacterRepository(IMongoDatabase database, MongoSequenceGenerator sequences)
    {
        _collection = database.GetCollection<Character>(CollectionName);
        _sequences = sequences;

        var nameIndex = new CreateIndexModel<Character>(
            Builders<Character>.IndexKeys.Ascending(c => c.Name),
            new CreateIndexOptions { Unique = true, Name = "ux_characters_name" });
        var accountIdIndex = new CreateIndexModel<Character>(
            Builders<Character>.IndexKeys.Ascending(c => c.AccountId),
            new CreateIndexOptions { Name = "ix_characters_accountId" });

        _collection.Indexes.CreateMany(new[] { nameIndex, accountIdIndex });
    }

    public async Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default)
    {
        return await _collection
            .Find(c => c.AccountId == accountId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default)
    {
        return await _collection
            .Find(c => c.Id == characterId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Character?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        return await _collection
            .Find(c => c.Name == name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(Character character, CancellationToken ct = default)
    {
        await AssignIdIfNeededAsync(character, ct).ConfigureAwait(false);
        await _collection.InsertOneAsync(character, cancellationToken: ct).ConfigureAwait(false);
        await _sequences.EnsureAtLeastAsync(SequenceName, character.Id, ct).ConfigureAwait(false);
    }

    public Task UpdateAsync(Character character, CancellationToken ct = default)
    {
        return _collection.ReplaceOneAsync(
            c => c.Id == character.Id,
            character,
            new ReplaceOptions { IsUpsert = false },
            ct);
    }

    private async Task AssignIdIfNeededAsync(Character character, CancellationToken ct)
    {
        if (character.Id > 0)
        {
            return;
        }

        var currentMax = await _collection
            .Find(Builders<Character>.Filter.Empty)
            .SortByDescending(c => c.Id)
            .Limit(1)
            .Project(c => c.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        character.Id = await _sequences.NextAsync(SequenceName, currentMax, ct).ConfigureAwait(false);
    }
}
