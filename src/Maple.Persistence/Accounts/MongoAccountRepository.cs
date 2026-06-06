using Maple.Core.Accounts;
using MongoDB.Driver;

namespace Maple.Persistence.Accounts;

/// <summary>MongoDB-backed account repository.</summary>
public sealed class MongoAccountRepository : IAccountRepository
{
    private const string CollectionName = "accounts";
    private const string SequenceName = "accounts";

    private readonly IMongoCollection<Account> _collection;
    private readonly MongoSequenceGenerator _sequences;

    public MongoAccountRepository(IMongoDatabase database, MongoSequenceGenerator sequences)
    {
        _collection = database.GetCollection<Account>(CollectionName);
        _sequences = sequences;

        var accountNameIndex = new CreateIndexModel<Account>(
            Builders<Account>.IndexKeys.Ascending(a => a.AccountName),
            new CreateIndexOptions { Unique = true, Name = "ux_accounts_accountName" });

        _collection.Indexes.CreateOne(accountNameIndex);
    }

    public async Task<Account?> FindByIdAsync(int accountId, CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(a => a.Id == accountId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Account?> FindByNameAsync(string accountName, CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(a => a.AccountName == accountName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        await AssignIdIfNeededAsync(account, cancellationToken).ConfigureAwait(false);
        await _collection.InsertOneAsync(account, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _sequences.EnsureAtLeastAsync(SequenceName, account.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryAddAsync(Account account, CancellationToken cancellationToken = default)
    {
        try
        {
            await AddAsync(account, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        return _collection.ReplaceOneAsync(
            a => a.Id == account.Id,
            account,
            new ReplaceOptions { IsUpsert = false },
            cancellationToken);
    }

    private async Task AssignIdIfNeededAsync(Account account, CancellationToken ct)
    {
        if (account.Id > 0)
        {
            return;
        }

        var currentMax = await _collection
            .Find(Builders<Account>.Filter.Empty)
            .SortByDescending(a => a.Id)
            .Limit(1)
            .Project(a => a.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        account.Id = await _sequences.NextAsync(SequenceName, currentMax, ct).ConfigureAwait(false);
    }
}
