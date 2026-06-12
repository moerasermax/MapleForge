namespace Maple.Core.Guilds.Bbs;

public sealed class BbsThread
{
    public string Id { get; set; } = string.Empty;

    public int GuildId { get; set; }

    public int ThreadId { get; set; }

    public int AuthorCharacterId { get; set; }

    public bool IsNotice { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public int Emoticon { get; set; }

    public long TimestampUnixMillis { get; set; }

    public List<BbsReply> Replies { get; set; } = new();

    public int ReplyCount => Replies.Count;

    public void NormalizeDocumentId() => Id = CreateDocumentId(GuildId, ThreadId);

    public void Edit(string title, string body, int emoticon, long timestampUnixMillis)
    {
        Title = title;
        Body = body;
        Emoticon = emoticon;
        TimestampUnixMillis = timestampUnixMillis;
    }

    public bool CanModerate(int characterId, byte guildRank) =>
        AuthorCharacterId == characterId || guildRank <= Guild.JuniorMasterRank;

    public BbsReply AddReply(int replyId, int authorCharacterId, string body, long timestampUnixMillis)
    {
        var reply = new BbsReply
        {
            ReplyId = replyId,
            AuthorCharacterId = authorCharacterId,
            Body = body,
            TimestampUnixMillis = timestampUnixMillis,
        };
        Replies.Add(reply);
        SortReplies();
        return reply;
    }

    public BbsReply? GetReply(int replyId) =>
        Replies.FirstOrDefault(r => r.ReplyId == replyId);

    public bool CanModerateReply(int replyId, int characterId, byte guildRank)
    {
        var reply = GetReply(replyId);
        return reply is not null && (reply.AuthorCharacterId == characterId || guildRank <= Guild.JuniorMasterRank);
    }

    public bool RemoveReply(int replyId)
    {
        var index = Replies.FindIndex(r => r.ReplyId == replyId);
        if (index < 0)
        {
            return false;
        }

        Replies.RemoveAt(index);
        return true;
    }

    public BbsThread Clone()
    {
        return new BbsThread
        {
            Id = Id,
            GuildId = GuildId,
            ThreadId = ThreadId,
            AuthorCharacterId = AuthorCharacterId,
            IsNotice = IsNotice,
            Title = Title,
            Body = Body,
            Emoticon = Emoticon,
            TimestampUnixMillis = TimestampUnixMillis,
            Replies = Replies.Select(static r => r.Clone()).ToList(),
        };
    }

    public static string CreateDocumentId(int guildId, int threadId) =>
        $"{guildId}:{threadId}";

    private void SortReplies() =>
        Replies.Sort(static (left, right) => left.ReplyId.CompareTo(right.ReplyId));
}

public sealed class BbsReply
{
    public int ReplyId { get; set; }

    public int AuthorCharacterId { get; set; }

    public string Body { get; set; } = string.Empty;

    public long TimestampUnixMillis { get; set; }

    public BbsReply Clone()
    {
        return new BbsReply
        {
            ReplyId = ReplyId,
            AuthorCharacterId = AuthorCharacterId,
            Body = Body,
            TimestampUnixMillis = TimestampUnixMillis,
        };
    }
}
