using LiteDB;
using Maple.Core.Guilds.Bbs;
using Maple.Persistence.Guilds;

namespace Maple.Persistence.Tests;

public sealed class GuildBbsRepositoryTests
{
    [Fact]
    public async Task LiteDbRepository_RoundTripsThreadRepliesAndPerGuildCounters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maple-bbs-{Guid.NewGuid():N}.db");
        try
        {
            using var database = new LiteDatabase(path);
            var repository = new LiteDbGuildBbsRepository(database);

            var guildOneThreadId = await repository.GetNextThreadIdAsync(100);
            var guildTwoThreadId = await repository.GetNextThreadIdAsync(200);
            Assert.Equal(1, guildOneThreadId);
            Assert.Equal(1, guildTwoThreadId);

            var replyId = await repository.GetNextReplyIdAsync(100);
            var thread = new BbsThread
            {
                GuildId = 100,
                ThreadId = guildOneThreadId,
                AuthorCharacterId = 10,
                IsNotice = true,
                Title = "notice",
                Body = "body",
                Emoticon = 1,
                TimestampUnixMillis = 60000,
            };
            thread.AddReply(replyId, 11, "reply", 120000);
            await repository.UpsertThreadAsync(thread);

            var loaded = await repository.FindThreadAsync(100, guildOneThreadId);

            Assert.NotNull(loaded);
            Assert.Equal("notice", loaded.Title);
            Assert.Equal(1, Assert.Single(loaded.Replies).ReplyId);
            Assert.Equal(2, await repository.GetNextThreadIdAsync(100));
            Assert.Equal(2, await repository.GetNextReplyIdAsync(100));

            var secondNotice = new BbsThread
            {
                GuildId = 100,
                ThreadId = 2,
                AuthorCharacterId = 12,
                IsNotice = true,
                Title = "new notice",
                Body = "body",
                Emoticon = 2,
                TimestampUnixMillis = 180000,
            };
            await repository.UpsertThreadAsync(secondNotice);
            await repository.DeleteOtherNoticeThreadsAsync(100, keepThreadId: 2);

            var all = await repository.GetThreadsAsync(100);

            Assert.Single(all);
            Assert.Equal(2, all[0].ThreadId);
            Assert.True(all[0].IsNotice);
            Assert.Equal("new notice", all[0].Title);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
