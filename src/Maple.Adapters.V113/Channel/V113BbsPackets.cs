using Maple.Application.Guilds.Bbs;
using Maple.Core.Guilds.Bbs;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal enum V113BbsOperation : byte
{
    AddThread = 0,
    DeleteThread = 1,
    ListThread = 2,
    DisplayThread = 3,
    AddReply = 4,
    DeleteReply = 5,
}

internal sealed record V113BbsAddOrEditThreadRequest(
    bool IsEdit,
    int ThreadId,
    bool IsNotice,
    string Title,
    string Body,
    int Emoticon);

internal sealed record V113BbsAddReplyRequest(int ThreadId, string Body);

internal sealed record V113BbsDeleteReplyRequest(int ThreadId, int ReplyId);

internal static class V113BbsPackets
{
    public const short RecvBbsOperationOpcode = 0x94;
    public const short SendBbsOperationOpcode = 0x68;
    public const byte ThreadListCode = 0x06;
    public const byte ShowThreadCode = 0x07;

    private const long KoreanFileTimeOffset = 116444592000000000L;
    private const long MaxTime = 150842304000000000L;

    public static V113BbsOperation ReadOperation(PacketReader reader) =>
        (V113BbsOperation)reader.ReadByte();

    public static V113BbsAddOrEditThreadRequest ParseAddOrEditThread(PacketReader reader)
    {
        var isEdit = reader.ReadByte() > 0;
        var threadId = isEdit ? reader.ReadInt() : 0;
        var isNotice = reader.ReadByte() > 0;
        var title = reader.ReadMapleString();
        var body = reader.ReadMapleString();
        var emoticon = reader.ReadInt();
        return new V113BbsAddOrEditThreadRequest(isEdit, threadId, isNotice, title, body, emoticon);
    }

    public static int ParseThreadId(PacketReader reader) => reader.ReadInt();

    public static int ParseListStart(PacketReader reader) => reader.ReadInt() * GuildBbsService.PageSize;

    public static V113BbsAddReplyRequest ParseAddReply(PacketReader reader)
    {
        var threadId = reader.ReadInt();
        var body = reader.ReadMapleString();
        return new V113BbsAddReplyRequest(threadId, body);
    }

    public static V113BbsDeleteReplyRequest ParseDeleteReply(PacketReader reader)
    {
        var threadId = reader.ReadInt();
        var replyId = reader.ReadInt();
        return new V113BbsDeleteReplyRequest(threadId, replyId);
    }

    public static byte[] ThreadList(GuildBbsThreadList list)
    {
        var w = new PacketWriter();
        w.WriteShort(SendBbsOperationOpcode);
        w.WriteByte(ThreadListCode);

        if (list.Notice is null)
        {
            w.WriteByte(0);
        }
        else
        {
            w.WriteByte(1);
            AddThread(w, list.Notice);
        }

        w.WriteInt(list.TotalCount);
        w.WriteInt(list.Threads.Count);
        foreach (var thread in list.Threads)
        {
            AddThread(w, thread);
        }

        return w.ToArray();
    }

    public static byte[] ShowThread(BbsThread thread)
    {
        var w = new PacketWriter();
        w.WriteShort(SendBbsOperationOpcode);
        w.WriteByte(ShowThreadCode);
        w.WriteInt(thread.ThreadId);
        w.WriteInt(thread.AuthorCharacterId);
        w.WriteLong(GetKoreanTimestamp(thread.TimestampUnixMillis));
        w.WriteMapleString(thread.Title);
        w.WriteMapleString(thread.Body);
        w.WriteInt(thread.Emoticon);
        w.WriteInt(thread.ReplyCount);

        foreach (var reply in thread.Replies.OrderBy(static r => r.ReplyId))
        {
            w.WriteInt(reply.ReplyId);
            w.WriteInt(reply.AuthorCharacterId);
            w.WriteLong(GetKoreanTimestamp(reply.TimestampUnixMillis));
            w.WriteMapleString(reply.Body);
        }

        return w.ToArray();
    }

    private static void AddThread(PacketWriter w, BbsThread thread)
    {
        w.WriteInt(thread.ThreadId);
        w.WriteInt(thread.AuthorCharacterId);
        w.WriteMapleString(thread.Title);
        w.WriteLong(GetKoreanTimestamp(thread.TimestampUnixMillis));
        w.WriteInt(thread.Emoticon);
        w.WriteInt(thread.ReplyCount);
    }

    private static long GetKoreanTimestamp(long unixMillis)
    {
        if (unixMillis == -1)
        {
            return MaxTime;
        }

        var minutes = unixMillis / 1000 / 60;
        return (minutes * 600000000) + KoreanFileTimeOffset;
    }
}
