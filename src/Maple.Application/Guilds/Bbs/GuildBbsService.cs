using Maple.Core.Guilds.Bbs;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Guilds.Bbs;

public enum GuildBbsStatus
{
    Success,
    NotInGuild,
    ThreadNotFound,
    ReplyNotFound,
    NotAuthorized,
    InvalidIcon,
    InvalidInput,
}

public sealed record GuildBbsThreadList(
    int GuildId,
    int Start,
    int TotalCount,
    BbsThread? Notice,
    IReadOnlyList<BbsThread> Threads);

public sealed record GuildBbsResult(
    GuildBbsStatus Status,
    BbsThread? Thread = null,
    GuildBbsThreadList? ThreadList = null)
{
    public bool Succeeded => Status == GuildBbsStatus.Success;
}

public sealed class GuildBbsService
{
    public const int PageSize = 10;
    public const int MaxTitleLength = 25;
    public const int MaxBodyLength = 600;
    public const int MaxReplyLength = 25;

    private readonly IGuildBbsRepository _repository;
    private readonly TimeProvider _timeProvider;

    public GuildBbsService(IGuildBbsRepository repository, TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<GuildBbsResult> AddOrEditThreadAsync(
        Player player,
        bool isEdit,
        int threadId,
        bool isNotice,
        string title,
        string body,
        int emoticon,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var guildId = player.Character.GuildId;
        if (guildId <= 0)
        {
            return new GuildBbsResult(GuildBbsStatus.NotInGuild);
        }

        if (!IsValidIcon(player, emoticon))
        {
            return new GuildBbsResult(GuildBbsStatus.InvalidIcon);
        }

        title = Limit(title, MaxTitleLength);
        body = Limit(body, MaxBodyLength);
        var now = NowUnixMillis();

        if (isEdit)
        {
            var existing = await _repository.FindThreadAsync(guildId, threadId, ct).ConfigureAwait(false);
            if (existing is null)
            {
                return new GuildBbsResult(GuildBbsStatus.ThreadNotFound);
            }

            if (!existing.CanModerate(player.Character.Id, player.Character.GuildRank))
            {
                return new GuildBbsResult(GuildBbsStatus.NotAuthorized, existing.Clone());
            }

            existing.Edit(title, body, emoticon, now);
            await _repository.UpsertThreadAsync(existing, ct).ConfigureAwait(false);
            return new GuildBbsResult(GuildBbsStatus.Success, existing.Clone());
        }

        var newThreadId = await _repository.GetNextThreadIdAsync(guildId, ct).ConfigureAwait(false);
        var thread = new BbsThread
        {
            GuildId = guildId,
            ThreadId = newThreadId,
            AuthorCharacterId = player.Character.Id,
            IsNotice = isNotice,
            Title = title,
            Body = body,
            Emoticon = emoticon,
            TimestampUnixMillis = now,
        };
        thread.NormalizeDocumentId();

        await _repository.UpsertThreadAsync(thread, ct).ConfigureAwait(false);
        if (isNotice)
        {
            await _repository.DeleteOtherNoticeThreadsAsync(guildId, thread.ThreadId, ct).ConfigureAwait(false);
        }

        return new GuildBbsResult(GuildBbsStatus.Success, thread.Clone());
    }

    public async Task<GuildBbsResult> DeleteThreadAsync(Player player, int threadId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var guildId = player.Character.GuildId;
        if (guildId <= 0)
        {
            return new GuildBbsResult(GuildBbsStatus.NotInGuild);
        }

        var thread = await _repository.FindThreadAsync(guildId, threadId, ct).ConfigureAwait(false);
        if (thread is null)
        {
            return new GuildBbsResult(GuildBbsStatus.ThreadNotFound);
        }

        if (!thread.CanModerate(player.Character.Id, player.Character.GuildRank))
        {
            return new GuildBbsResult(GuildBbsStatus.NotAuthorized, thread.Clone());
        }

        await _repository.DeleteThreadAsync(guildId, threadId, ct).ConfigureAwait(false);
        return new GuildBbsResult(GuildBbsStatus.Success, thread.Clone());
    }

    public async Task<GuildBbsResult> ListThreadsAsync(Player player, int start, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var guildId = player.Character.GuildId;
        if (guildId <= 0)
        {
            return new GuildBbsResult(GuildBbsStatus.NotInGuild);
        }

        var threads = await _repository.GetThreadsAsync(guildId, ct).ConfigureAwait(false);
        var ordered = threads
            .OrderByDescending(static t => t.ThreadId)
            .ToArray();
        var notice = ordered.FirstOrDefault(static t => t.IsNotice)?.Clone();
        var nonNotice = ordered
            .Where(static t => !t.IsNotice)
            .ToArray();

        if (start < 0 || ordered.Length < start)
        {
            start = 0;
        }

        var pageThreads = nonNotice
            .Skip(start)
            .Take(PageSize)
            .Select(static t => t.Clone())
            .ToArray();

        return new GuildBbsResult(
            GuildBbsStatus.Success,
            ThreadList: new GuildBbsThreadList(guildId, start, ordered.Length, notice, pageThreads));
    }

    public async Task<GuildBbsResult> ShowThreadAsync(Player player, int threadId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var guildId = player.Character.GuildId;
        if (guildId <= 0)
        {
            return new GuildBbsResult(GuildBbsStatus.NotInGuild);
        }

        var thread = await _repository.FindThreadAsync(guildId, threadId, ct).ConfigureAwait(false);
        return thread is null
            ? new GuildBbsResult(GuildBbsStatus.ThreadNotFound)
            : new GuildBbsResult(GuildBbsStatus.Success, thread.Clone());
    }

    public async Task<GuildBbsResult> AddReplyAsync(Player player, int threadId, string body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var guildId = player.Character.GuildId;
        if (guildId <= 0)
        {
            return new GuildBbsResult(GuildBbsStatus.NotInGuild);
        }

        var thread = await _repository.FindThreadAsync(guildId, threadId, ct).ConfigureAwait(false);
        if (thread is null)
        {
            return new GuildBbsResult(GuildBbsStatus.ThreadNotFound);
        }

        var replyId = await _repository.GetNextReplyIdAsync(guildId, ct).ConfigureAwait(false);
        thread.AddReply(replyId, player.Character.Id, Limit(body, MaxReplyLength), NowUnixMillis());
        await _repository.UpsertThreadAsync(thread, ct).ConfigureAwait(false);
        return new GuildBbsResult(GuildBbsStatus.Success, thread.Clone());
    }

    public async Task<GuildBbsResult> DeleteReplyAsync(Player player, int threadId, int replyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var guildId = player.Character.GuildId;
        if (guildId <= 0)
        {
            return new GuildBbsResult(GuildBbsStatus.NotInGuild);
        }

        var thread = await _repository.FindThreadAsync(guildId, threadId, ct).ConfigureAwait(false);
        if (thread is null)
        {
            return new GuildBbsResult(GuildBbsStatus.ThreadNotFound);
        }

        if (thread.GetReply(replyId) is null)
        {
            return new GuildBbsResult(GuildBbsStatus.ReplyNotFound, thread.Clone());
        }

        if (!thread.CanModerateReply(replyId, player.Character.Id, player.Character.GuildRank))
        {
            return new GuildBbsResult(GuildBbsStatus.NotAuthorized, thread.Clone());
        }

        thread.RemoveReply(replyId);
        await _repository.UpsertThreadAsync(thread, ct).ConfigureAwait(false);
        return new GuildBbsResult(GuildBbsStatus.Success, thread.Clone());
    }

    private long NowUnixMillis() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private static string Limit(string value, int maxLength)
    {
        value ??= string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool IsValidIcon(Player player, int icon)
    {
        if (icon is >= 0 and <= 2)
        {
            return true;
        }

        if (icon is < 0x64 or > 0x6A)
        {
            return false;
        }

        var cashIconItemId = 5290000 + icon - 0x64;
        return player.HasItem(InventoryType.Cash, cashIconItemId);
    }
}
