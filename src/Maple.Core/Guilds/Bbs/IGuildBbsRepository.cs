namespace Maple.Core.Guilds.Bbs;

public interface IGuildBbsRepository
{
    Task<IReadOnlyList<BbsThread>> GetThreadsAsync(int guildId, CancellationToken ct = default);

    Task<BbsThread?> FindThreadAsync(int guildId, int threadId, CancellationToken ct = default);

    Task<int> GetNextThreadIdAsync(int guildId, CancellationToken ct = default);

    Task<int> GetNextReplyIdAsync(int guildId, CancellationToken ct = default);

    Task UpsertThreadAsync(BbsThread thread, CancellationToken ct = default);

    Task DeleteThreadAsync(int guildId, int threadId, CancellationToken ct = default);

    Task DeleteOtherNoticeThreadsAsync(int guildId, int keepThreadId, CancellationToken ct = default);
}
