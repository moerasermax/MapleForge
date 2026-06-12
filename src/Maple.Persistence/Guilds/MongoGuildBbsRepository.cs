using Maple.Core.Guilds.Bbs;
using MongoDB.Driver;

namespace Maple.Persistence.Guilds;

public sealed class MongoGuildBbsRepository : IGuildBbsRepository
{
    private const string CollectionName = "guild_bbs_threads";
    private const string ThreadSequencePrefix = "guild_bbs_thread";
    private const string ReplySequencePrefix = "guild_bbs_reply";

    private readonly IMongoCollection<BbsThread> _threads;
    private readonly MongoSequenceGenerator _sequences;

    public MongoGuildBbsRepository(IMongoDatabase database, MongoSequenceGenerator sequences)
    {
        _threads = database.GetCollection<BbsThread>(CollectionName);
        _sequences = sequences;

        _threads.Indexes.CreateOne(new CreateIndexModel<BbsThread>(
            Builders<BbsThread>.IndexKeys.Ascending(t => t.Id),
            new CreateIndexOptions { Unique = true, Name = "ux_guild_bbs_id" }));
        _threads.Indexes.CreateOne(new CreateIndexModel<BbsThread>(
            Builders<BbsThread>.IndexKeys.Ascending(t => t.GuildId).Ascending(t => t.ThreadId),
            new CreateIndexOptions { Unique = true, Name = "ux_guild_bbs_guild_thread" }));
        _threads.Indexes.CreateOne(new CreateIndexModel<BbsThread>(
            Builders<BbsThread>.IndexKeys.Ascending(t => t.GuildId).Ascending(t => t.IsNotice),
            new CreateIndexOptions { Name = "ix_guild_bbs_guild_notice" }));
    }

    public async Task<IReadOnlyList<BbsThread>> GetThreadsAsync(int guildId, CancellationToken ct = default)
    {
        var threads = await _threads
            .Find(t => t.GuildId == guildId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return threads.Select(static t => t.Clone()).ToArray();
    }

    public async Task<BbsThread?> FindThreadAsync(int guildId, int threadId, CancellationToken ct = default)
    {
        var thread = await _threads
            .Find(t => t.GuildId == guildId && t.ThreadId == threadId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return thread?.Clone();
    }

    public async Task<int> GetNextThreadIdAsync(int guildId, CancellationToken ct = default)
    {
        var currentMax = await _threads
            .Find(t => t.GuildId == guildId)
            .SortByDescending(t => t.ThreadId)
            .Limit(1)
            .Project(t => t.ThreadId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return await _sequences.NextAsync($"{ThreadSequencePrefix}:{guildId}", currentMax, ct).ConfigureAwait(false);
    }

    public async Task<int> GetNextReplyIdAsync(int guildId, CancellationToken ct = default)
    {
        var replies = await _threads
            .Find(t => t.GuildId == guildId)
            .Project(t => t.Replies)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var currentMax = replies
            .SelectMany(static r => r)
            .Select(static r => r.ReplyId)
            .DefaultIfEmpty(0)
            .Max();

        return await _sequences.NextAsync($"{ReplySequencePrefix}:{guildId}", currentMax, ct).ConfigureAwait(false);
    }

    public Task UpsertThreadAsync(BbsThread thread, CancellationToken ct = default)
    {
        var copy = thread.Clone();
        copy.NormalizeDocumentId();
        return _threads.ReplaceOneAsync(
            t => t.Id == copy.Id,
            copy,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public Task DeleteThreadAsync(int guildId, int threadId, CancellationToken ct = default)
    {
        return _threads.DeleteOneAsync(t => t.GuildId == guildId && t.ThreadId == threadId, ct);
    }

    public Task DeleteOtherNoticeThreadsAsync(int guildId, int keepThreadId, CancellationToken ct = default)
    {
        var filter = Builders<BbsThread>.Filter.And(
            Builders<BbsThread>.Filter.Eq(t => t.GuildId, guildId),
            Builders<BbsThread>.Filter.Eq(t => t.IsNotice, true),
            Builders<BbsThread>.Filter.Ne(t => t.ThreadId, keepThreadId));

        return _threads.DeleteManyAsync(filter, ct);
    }
}
