using Maple.Application.Guilds.Bbs;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed class V113BbsHandler
{
    private readonly GuildBbsService _bbs;

    public V113BbsHandler(GuildBbsService bbs)
    {
        _bbs = bbs;
    }

    public async Task HandleBbsOperationAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(sendSelf);

        V113BbsOperation operation;
        try
        {
            operation = V113BbsPackets.ReadOperation(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        switch (operation)
        {
            case V113BbsOperation.AddThread:
                await HandleAddOrEditThreadAsync(reader, player, sendSelf, ct);
                break;

            case V113BbsOperation.DeleteThread:
                await HandleDeleteThreadAsync(reader, player, ct);
                break;

            case V113BbsOperation.ListThread:
                await HandleListThreadAsync(reader, player, sendSelf, ct);
                break;

            case V113BbsOperation.DisplayThread:
                await HandleDisplayThreadAsync(reader, player, sendSelf, ct);
                break;

            case V113BbsOperation.AddReply:
                await HandleAddReplyAsync(reader, player, sendSelf, ct);
                break;

            case V113BbsOperation.DeleteReply:
                await HandleDeleteReplyAsync(reader, player, sendSelf, ct);
                break;
        }
    }

    private async Task HandleAddOrEditThreadAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        V113BbsAddOrEditThreadRequest request;
        try
        {
            request = V113BbsPackets.ParseAddOrEditThread(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _bbs.AddOrEditThreadAsync(
            player,
            request.IsEdit,
            request.ThreadId,
            request.IsNotice,
            request.Title,
            request.Body,
            request.Emoticon,
            ct).ConfigureAwait(false);

        if (result.Thread is not null)
        {
            await sendSelf(V113BbsPackets.ShowThread(result.Thread), ct).ConfigureAwait(false);
        }
    }

    private async Task HandleDeleteThreadAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        int threadId;
        try
        {
            threadId = V113BbsPackets.ParseThreadId(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        await _bbs.DeleteThreadAsync(player, threadId, ct).ConfigureAwait(false);
    }

    private async Task HandleListThreadAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int start;
        try
        {
            start = V113BbsPackets.ParseListStart(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _bbs.ListThreadsAsync(player, start, ct).ConfigureAwait(false);
        if (result.ThreadList is not null)
        {
            await sendSelf(V113BbsPackets.ThreadList(result.ThreadList), ct).ConfigureAwait(false);
        }
    }

    private async Task HandleDisplayThreadAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        int threadId;
        try
        {
            threadId = V113BbsPackets.ParseThreadId(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _bbs.ShowThreadAsync(player, threadId, ct).ConfigureAwait(false);
        if (result.Thread is not null)
        {
            await sendSelf(V113BbsPackets.ShowThread(result.Thread), ct).ConfigureAwait(false);
        }
    }

    private async Task HandleAddReplyAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        V113BbsAddReplyRequest request;
        try
        {
            request = V113BbsPackets.ParseAddReply(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _bbs.AddReplyAsync(player, request.ThreadId, request.Body, ct).ConfigureAwait(false);
        if (result.Thread is not null)
        {
            await sendSelf(V113BbsPackets.ShowThread(result.Thread), ct).ConfigureAwait(false);
        }
    }

    private async Task HandleDeleteReplyAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        V113BbsDeleteReplyRequest request;
        try
        {
            request = V113BbsPackets.ParseDeleteReply(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _bbs.DeleteReplyAsync(player, request.ThreadId, request.ReplyId, ct).ConfigureAwait(false);
        if (result.Thread is not null)
        {
            await sendSelf(V113BbsPackets.ShowThread(result.Thread), ct).ConfigureAwait(false);
        }
    }
}
