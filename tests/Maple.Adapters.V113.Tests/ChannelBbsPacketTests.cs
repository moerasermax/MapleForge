using Maple.Adapters.V113.Channel;
using Maple.Application.Guilds.Bbs;
using Maple.Core.Characters;
using Maple.Core.Guilds.Bbs;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelBbsPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaEnums()
    {
        Assert.Equal(0x94, V113BbsPackets.RecvBbsOperationOpcode);
        Assert.Equal(0x68, V113BbsPackets.SendBbsOperationOpcode);
    }

    [Fact]
    public void ParseAddOrEditThread_ReadsJavaOperationBody()
    {
        var body = new PacketWriter()
            .WriteByte(1)
            .WriteInt(42)
            .WriteByte(1)
            .WriteMapleString("title")
            .WriteMapleString("content")
            .WriteInt(2)
            .ToArray();

        var request = V113BbsPackets.ParseAddOrEditThread(new PacketReader(body));

        Assert.True(request.IsEdit);
        Assert.Equal(42, request.ThreadId);
        Assert.True(request.IsNotice);
        Assert.Equal("title", request.Title);
        Assert.Equal("content", request.Body);
        Assert.Equal(2, request.Emoticon);
    }

    [Fact]
    public void ThreadList_WritesNoticeAndPageRows()
    {
        var notice = Thread(2, "notice", notice: true);
        var normal = Thread(1, "normal", notice: false);
        var list = new GuildBbsThreadList(100, 0, 2, notice, new[] { normal });

        var reader = new PacketReader(V113BbsPackets.ThreadList(list));

        Assert.Equal(V113BbsPackets.SendBbsOperationOpcode, reader.ReadShort());
        Assert.Equal(V113BbsPackets.ThreadListCode, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        AssertThreadSummary(reader, notice);
        Assert.Equal(2, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        AssertThreadSummary(reader, normal);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ShowThread_WritesRepliesInReplyIdOrder()
    {
        var thread = Thread(3, "topic", notice: false);
        thread.Body = "body";
        thread.Replies =
        [
            new BbsReply { ReplyId = 2, AuthorCharacterId = 9, Body = "second", TimestampUnixMillis = 180000 },
            new BbsReply { ReplyId = 1, AuthorCharacterId = 8, Body = "first", TimestampUnixMillis = 120000 },
        ];

        var reader = new PacketReader(V113BbsPackets.ShowThread(thread));

        Assert.Equal(V113BbsPackets.SendBbsOperationOpcode, reader.ReadShort());
        Assert.Equal(V113BbsPackets.ShowThreadCode, reader.ReadByte());
        Assert.Equal(3, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(KoreanTimestamp(60000), ReadLong(reader));
        Assert.Equal("topic", reader.ReadMapleString());
        Assert.Equal("body", reader.ReadMapleString());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(2, reader.ReadInt());

        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(8, reader.ReadInt());
        Assert.Equal(KoreanTimestamp(120000), ReadLong(reader));
        Assert.Equal("first", reader.ReadMapleString());

        Assert.Equal(2, reader.ReadInt());
        Assert.Equal(9, reader.ReadInt());
        Assert.Equal(KoreanTimestamp(180000), ReadLong(reader));
        Assert.Equal("second", reader.ReadMapleString());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public async Task Handler_HeadlessFlow_SendsJavaListAndShowPackets()
    {
        var repository = new FakeGuildBbsRepository();
        var service = new GuildBbsService(repository, new FixedTimeProvider(60000));
        var handler = new V113BbsHandler(service);
        var player = Player(1, "Poster", guildId: 100);
        var sent = new List<byte[]>();

        await handler.HandleBbsOperationAsync(
            new PacketReader(new PacketWriter()
                .WriteByte((byte)V113BbsOperation.AddThread)
                .WriteByte(0)
                .WriteByte(0)
                .WriteMapleString("topic")
                .WriteMapleString("body")
                .WriteInt(1)
                .ToArray()),
            player,
            Send(sent),
            CancellationToken.None);

        Assert.Equal(V113BbsPackets.ShowThreadCode, sent.Single()[2]);

        await handler.HandleBbsOperationAsync(
            new PacketReader(new PacketWriter()
                .WriteByte((byte)V113BbsOperation.ListThread)
                .WriteInt(0)
                .ToArray()),
            player,
            Send(sent),
            CancellationToken.None);

        Assert.Equal(V113BbsPackets.ThreadListCode, sent.Last()[2]);

        await handler.HandleBbsOperationAsync(
            new PacketReader(new PacketWriter()
                .WriteByte((byte)V113BbsOperation.AddReply)
                .WriteInt(1)
                .WriteMapleString("reply")
                .ToArray()),
            player,
            Send(sent),
            CancellationToken.None);

        var replyPacket = new PacketReader(sent.Last());
        replyPacket.ReadShort();
        Assert.Equal(V113BbsPackets.ShowThreadCode, replyPacket.ReadByte());
        replyPacket.ReadInt();
        replyPacket.ReadInt();
        ReadLong(replyPacket);
        replyPacket.ReadMapleString();
        replyPacket.ReadMapleString();
        replyPacket.ReadInt();
        Assert.Equal(1, replyPacket.ReadInt());

        await handler.HandleBbsOperationAsync(
            new PacketReader(new PacketWriter()
                .WriteByte((byte)V113BbsOperation.DeleteReply)
                .WriteInt(1)
                .WriteInt(1)
                .ToArray()),
            player,
            Send(sent),
            CancellationToken.None);

        var deletedReplyPacket = new PacketReader(sent.Last());
        deletedReplyPacket.ReadShort();
        Assert.Equal(V113BbsPackets.ShowThreadCode, deletedReplyPacket.ReadByte());
        deletedReplyPacket.ReadInt();
        deletedReplyPacket.ReadInt();
        ReadLong(deletedReplyPacket);
        deletedReplyPacket.ReadMapleString();
        deletedReplyPacket.ReadMapleString();
        deletedReplyPacket.ReadInt();
        Assert.Equal(0, deletedReplyPacket.ReadInt());
    }

    private static Func<byte[], CancellationToken, Task> Send(List<byte[]> packets) =>
        (packet, _) =>
        {
            packets.Add(packet);
            return Task.CompletedTask;
        };

    private static void AssertThreadSummary(PacketReader reader, BbsThread expected)
    {
        Assert.Equal(expected.ThreadId, reader.ReadInt());
        Assert.Equal(expected.AuthorCharacterId, reader.ReadInt());
        Assert.Equal(expected.Title, reader.ReadMapleString());
        Assert.Equal(KoreanTimestamp(expected.TimestampUnixMillis), ReadLong(reader));
        Assert.Equal(expected.Emoticon, reader.ReadInt());
        Assert.Equal(expected.ReplyCount, reader.ReadInt());
    }

    private static BbsThread Thread(int threadId, string title, bool notice) =>
        new()
        {
            GuildId = 100,
            ThreadId = threadId,
            AuthorCharacterId = 1,
            IsNotice = notice,
            Title = title,
            Body = "body",
            Emoticon = 1,
            TimestampUnixMillis = 60000,
        };

    private static Player Player(int id, string name, int guildId) =>
        new(new Character
        {
            Id = id,
            Name = name,
            Level = 30,
            Job = 100,
            GuildId = guildId,
            GuildRank = 5,
        }, new Position(0, 0, 0, 0));

    private static long ReadLong(PacketReader reader)
    {
        var lo = (ulong)(uint)reader.ReadInt();
        var hi = (ulong)(uint)reader.ReadInt();
        return (long)(lo | (hi << 32));
    }

    private static long KoreanTimestamp(long unixMillis) =>
        ((unixMillis / 1000 / 60) * 600000000) + 116444592000000000L;

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

        public Task<IReadOnlyList<BbsThread>> GetThreadsAsync(int guildId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BbsThread>>(_threads.Values.Where(t => t.GuildId == guildId).Select(static t => t.Clone()).ToArray());

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
