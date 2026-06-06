using MongoDB.Bson;
using MongoDB.Driver;

namespace Maple.Persistence;

public sealed class MongoSequenceGenerator
{
    private readonly IMongoCollection<BsonDocument> _counters;

    public MongoSequenceGenerator(IMongoDatabase database)
    {
        _counters = database.GetCollection<BsonDocument>("counters");
    }

    public async Task<int> NextAsync(string name, int currentMax, CancellationToken ct)
    {
        await EnsureAtLeastAsync(name, currentMax, ct).ConfigureAwait(false);

        var filter = Builders<BsonDocument>.Filter.Eq("_id", name);
        var update = Builders<BsonDocument>.Update.Inc("value", 1);
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After,
        };

        var doc = await _counters.FindOneAndUpdateAsync(filter, update, options, ct).ConfigureAwait(false);
        return doc["value"].ToInt32();
    }

    public Task EnsureAtLeastAsync(string name, int value, CancellationToken ct)
    {
        if (value <= 0)
        {
            return Task.CompletedTask;
        }

        var filter = Builders<BsonDocument>.Filter.Eq("_id", name);
        var update = Builders<BsonDocument>.Update.Max("value", value);
        return _counters.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
    }
}
