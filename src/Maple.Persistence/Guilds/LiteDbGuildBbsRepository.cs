using LiteDB;
using Maple.Core.Guilds.Bbs;

namespace Maple.Persistence.Guilds;

public sealed class LiteDbGuildBbsRepository : IGuildBbsRepository
{
    private readonly ILiteCollection<BbsThread> _threads;
    private readonly ILiteCollection<LiteDbGuildBbsCounter> _counters;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LiteDbGuildBbsRepository(LiteDatabase database)
    {
        _threads = database.GetCollection<BbsThread>("guild_bbs_threads");
        _threads.EnsureIndex(t => t.Id, unique: true);
        _threads.EnsureIndex(t => t.GuildId);
        _threads.EnsureIndex(t => t.ThreadId);

        _counters = database.GetCollection<LiteDbGuildBbsCounter>("guild_bbs_counters");
    }

    public Task<IReadOnlyList<BbsThread>> GetThreadsAsync(int guildId, CancellationToken ct = default)
    {
        var threads = _threads
            .Find(t => t.GuildId == guildId)
            .Select(static t => t.Clone())
            .ToList();
        return Task.FromResult<IReadOnlyList<BbsThread>>(threads);
    }

    public Task<BbsThread?> FindThreadAsync(int guildId, int threadId, CancellationToken ct = default)
    {
        var thread = _threads.FindOne(t => t.GuildId == guildId && t.ThreadId == threadId);
        return Task.FromResult(thread?.Clone());
    }

    public Task<int> GetNextThreadIdAsync(int guildId, CancellationToken ct = default) =>
        NextCounterAsync($"thread:{guildId}", guildId, reply: false, ct);

    public Task<int> GetNextReplyIdAsync(int guildId, CancellationToken ct = default) =>
        NextCounterAsync($"reply:{guildId}", guildId, reply: true, ct);

    public Task UpsertThreadAsync(BbsThread thread, CancellationToken ct = default)
    {
        var copy = thread.Clone();
        copy.NormalizeDocumentId();
        _threads.Upsert(copy);
        return Task.CompletedTask;
    }

    public Task DeleteThreadAsync(int guildId, int threadId, CancellationToken ct = default)
    {
        _threads.DeleteMany(t => t.GuildId == guildId && t.ThreadId == threadId);
        return Task.CompletedTask;
    }

    public Task DeleteOtherNoticeThreadsAsync(int guildId, int keepThreadId, CancellationToken ct = default)
    {
        _threads.DeleteMany(t => t.GuildId == guildId && t.IsNotice && t.ThreadId != keepThreadId);
        return Task.CompletedTask;
    }

    private async Task<int> NextCounterAsync(string name, int guildId, bool reply, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var currentMax = reply ? CurrentMaxReplyId(guildId) : CurrentMaxThreadId(guildId);
            var counter = _counters.FindById(name) ?? new LiteDbGuildBbsCounter { Id = name };
            if (counter.Value < currentMax)
            {
                counter.Value = currentMax;
            }

            counter.Value++;
            _counters.Upsert(counter);
            return counter.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private int CurrentMaxThreadId(int guildId)
    {
        return _threads
            .Find(t => t.GuildId == guildId)
            .Select(static t => t.ThreadId)
            .DefaultIfEmpty(0)
            .Max();
    }

    private int CurrentMaxReplyId(int guildId)
    {
        return _threads
            .Find(t => t.GuildId == guildId)
            .SelectMany(static t => t.Replies)
            .Select(static r => r.ReplyId)
            .DefaultIfEmpty(0)
            .Max();
    }

    private sealed class LiteDbGuildBbsCounter
    {
        public string Id { get; set; } = string.Empty;

        public int Value { get; set; }
    }
}
