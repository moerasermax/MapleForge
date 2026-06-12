using Maple.Application.Guilds.Bbs;
using Maple.Core.Characters;
using Maple.Core.Guilds;
using Maple.Core.Guilds.Bbs;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.Guilds;

public sealed class GuildBbsServiceTests
{
    [Fact]
    public async Task HeadlessFlow_AddListShowReplyDelete_PersistsThreads()
    {
        var repository = new FakeGuildBbsRepository();
        var service = new GuildBbsService(repository, new FixedTimeProvider(60000));
        var player = Player(1, "Poster", guildId: 100);

        var added = await service.AddOrEditThreadAsync(
            player,
            isEdit: false,
            threadId: 0,
            isNotice: false,
            title: "hello",
            body: "body",
            emoticon: 1);

        Assert.True(added.Succeeded);
        Assert.Equal(1, added.Thread!.ThreadId);
        Assert.Equal("hello", added.Thread.Title);

        var listed = await service.ListThreadsAsync(player, start: 0);

        Assert.True(listed.Succeeded);
        Assert.Null(listed.ThreadList!.Notice);
        Assert.Equal(1, listed.ThreadList.TotalCount);
        Assert.Equal(1, Assert.Single(listed.ThreadList.Threads).ThreadId);

        var shown = await service.ShowThreadAsync(player, added.Thread.ThreadId);

        Assert.True(shown.Succeeded);
        Assert.Equal("body", shown.Thread!.Body);

        var replied = await service.AddReplyAsync(player, added.Thread.ThreadId, "reply text");

        Assert.True(replied.Succeeded);
        var reply = Assert.Single(replied.Thread!.Replies);
        Assert.Equal(1, reply.ReplyId);
        Assert.Equal("reply text", reply.Body);

        var deletedReply = await service.DeleteReplyAsync(player, added.Thread.ThreadId, reply.ReplyId);

        Assert.True(deletedReply.Succeeded);
        Assert.Empty(deletedReply.Thread!.Replies);

        var deletedThread = await service.DeleteThreadAsync(player, added.Thread.ThreadId);

        Assert.True(deletedThread.Succeeded);
        Assert.Equal(GuildBbsStatus.ThreadNotFound, (await service.ShowThreadAsync(player, added.Thread.ThreadId)).Status);
    }

    [Fact]
    public async Task Notice_IsListedSeparatelyAndNewNoticeReplacesOldNotice()
    {
        var repository = new FakeGuildBbsRepository();
        var service = new GuildBbsService(repository, new FixedTimeProvider(120000));
        var player = Player(1, "Poster", guildId: 100);

        var normal = await service.AddOrEditThreadAsync(player, false, 0, false, "normal", "body", 1);
        var firstNotice = await service.AddOrEditThreadAsync(player, false, 0, true, "notice1", "body", 1);
        var secondNotice = await service.AddOrEditThreadAsync(player, false, 0, true, "notice2", "body", 1);

        var listed = await service.ListThreadsAsync(player, start: 0);

        Assert.Equal(secondNotice.Thread!.ThreadId, listed.ThreadList!.Notice!.ThreadId);
        Assert.Equal("notice2", listed.ThreadList.Notice.Title);
        Assert.Equal(2, listed.ThreadList.TotalCount);
        Assert.Equal(normal.Thread!.ThreadId, Assert.Single(listed.ThreadList.Threads).ThreadId);
        Assert.Equal(GuildBbsStatus.ThreadNotFound, (await service.ShowThreadAsync(player, firstNotice.Thread!.ThreadId)).Status);
    }

    [Fact]
    public async Task EditAndReplyDelete_RequireAuthorOrOfficer()
    {
        var repository = new FakeGuildBbsRepository();
        var service = new GuildBbsService(repository, new FixedTimeProvider(180000));
        var owner = Player(1, "Owner", guildId: 100);
        var member = Player(2, "Member", guildId: 100);
        var officer = Player(3, "Officer", guildId: 100, rank: Guild.JuniorMasterRank);

        var added = await service.AddOrEditThreadAsync(owner, false, 0, false, "topic", "body", 1);
        var deniedEdit = await service.AddOrEditThreadAsync(member, true, added.Thread!.ThreadId, false, "bad", "bad", 1);

        Assert.Equal(GuildBbsStatus.NotAuthorized, deniedEdit.Status);
        Assert.Equal("topic", deniedEdit.Thread!.Title);

        var officerEdit = await service.AddOrEditThreadAsync(officer, true, added.Thread.ThreadId, false, "ok", "edited", 2);

        Assert.True(officerEdit.Succeeded);
        Assert.Equal("ok", officerEdit.Thread!.Title);

        var replied = await service.AddReplyAsync(owner, added.Thread.ThreadId, "reply");
        var replyId = Assert.Single(replied.Thread!.Replies).ReplyId;
        var deniedDelete = await service.DeleteReplyAsync(member, added.Thread.ThreadId, replyId);

        Assert.Equal(GuildBbsStatus.NotAuthorized, deniedDelete.Status);
        Assert.Single(deniedDelete.Thread!.Replies);

        var officerDelete = await service.DeleteReplyAsync(officer, added.Thread.ThreadId, replyId);

        Assert.True(officerDelete.Succeeded);
        Assert.Empty(officerDelete.Thread!.Replies);
    }

    [Fact]
    public async Task CashIcon_RequiresMatchingCashItem()
    {
        var repository = new FakeGuildBbsRepository();
        var service = new GuildBbsService(repository, new FixedTimeProvider(240000));
        var player = Player(1, "Poster", guildId: 100);

        var denied = await service.AddOrEditThreadAsync(player, false, 0, false, "topic", "body", 0x64);

        Assert.Equal(GuildBbsStatus.InvalidIcon, denied.Status);

        player.GainItem(InventoryType.Cash, 5290000);
        var added = await service.AddOrEditThreadAsync(player, false, 0, false, "topic", "body", 0x64);

        Assert.True(added.Succeeded);
        Assert.Equal(0x64, added.Thread!.Emoticon);
    }

    private static Player Player(int id, string name, int guildId, byte rank = Guild.DefaultMemberRank) =>
        new(new Character
        {
            Id = id,
            Name = name,
            Level = 30,
            Job = 100,
            GuildId = guildId,
            GuildRank = rank,
        }, new Position(0, 0, 0, 0));

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(long unixMillis)
        {
            _now = DateTimeOffset.FromUnixTimeMilliseconds(unixMillis);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeGuildBbsRepository : IGuildBbsRepository
    {
        private readonly Dictionary<string, BbsThread> _threads = new();
        private readonly Dictionary<int, int> _threadCounters = new();
        private readonly Dictionary<int, int> _replyCounters = new();

        public Task<IReadOnlyList<BbsThread>> GetThreadsAsync(int guildId, CancellationToken ct = default)
        {
            var threads = _threads.Values
                .Where(t => t.GuildId == guildId)
                .Select(static t => t.Clone())
                .ToArray();
            return Task.FromResult<IReadOnlyList<BbsThread>>(threads);
        }

        public Task<BbsThread?> FindThreadAsync(int guildId, int threadId, CancellationToken ct = default)
        {
            var id = BbsThread.CreateDocumentId(guildId, threadId);
            return Task.FromResult(_threads.TryGetValue(id, out var thread) ? thread.Clone() : null);
        }

        public Task<int> GetNextThreadIdAsync(int guildId, CancellationToken ct = default)
        {
            var next = _threadCounters.GetValueOrDefault(guildId) + 1;
            _threadCounters[guildId] = next;
            return Task.FromResult(next);
        }

        public Task<int> GetNextReplyIdAsync(int guildId, CancellationToken ct = default)
        {
            var next = _replyCounters.GetValueOrDefault(guildId) + 1;
            _replyCounters[guildId] = next;
            return Task.FromResult(next);
        }

        public Task UpsertThreadAsync(BbsThread thread, CancellationToken ct = default)
        {
            var copy = thread.Clone();
            copy.NormalizeDocumentId();
            _threads[copy.Id] = copy;
            _threadCounters[copy.GuildId] = Math.Max(_threadCounters.GetValueOrDefault(copy.GuildId), copy.ThreadId);
            if (copy.Replies.Count > 0)
            {
                _replyCounters[copy.GuildId] = Math.Max(_replyCounters.GetValueOrDefault(copy.GuildId), copy.Replies.Max(static r => r.ReplyId));
            }

            return Task.CompletedTask;
        }

        public Task DeleteThreadAsync(int guildId, int threadId, CancellationToken ct = default)
        {
            _threads.Remove(BbsThread.CreateDocumentId(guildId, threadId));
            return Task.CompletedTask;
        }

        public Task DeleteOtherNoticeThreadsAsync(int guildId, int keepThreadId, CancellationToken ct = default)
        {
            var remove = _threads.Values
                .Where(t => t.GuildId == guildId && t.IsNotice && t.ThreadId != keepThreadId)
                .Select(static t => t.Id)
                .ToArray();

            foreach (var id in remove)
            {
                _threads.Remove(id);
            }

            return Task.CompletedTask;
        }
    }
}
