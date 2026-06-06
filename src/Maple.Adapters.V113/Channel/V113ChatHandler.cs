using Maple.Application.Chats;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed class V113ChatHandler
{
    private readonly ChatService _chats;
    private readonly IV113ChatSessionHook _sessions;

    public V113ChatHandler(ChatService chats, IV113ChatSessionHook sessions)
    {
        _chats = chats;
        _sessions = sessions;
    }

    public void OnPlayerLoggedIn(
        Player player,
        int channel,
        Func<byte[], CancellationToken, Task> sendPacket)
    {
        _chats.RegisterOnline(player.Character, channel, sendPacket);
    }

    public void OnPlayerLoggedOut(Player player)
    {
        _chats.DeregisterOnline(player.Character.Id);
    }

    public async Task HandleWhisperFindAsync(
        PacketReader reader,
        Player player,
        int channel,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        V113WhisperClientMode mode;
        try
        {
            mode = V113ChatPackets.ReadWhisperMode(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        switch (mode)
        {
            case V113WhisperClientMode.Find:
            case V113WhisperClientMode.BuddyFind:
                await HandleFindAsync(reader, channel, mode == V113WhisperClientMode.BuddyFind, sendSelf, ct);
                break;

            case V113WhisperClientMode.Whisper:
                await HandleWhisperAsync(reader, player, channel, sendSelf, ct);
                break;
        }
    }

    public async Task HandleGroupChatAsync(
        PacketReader reader,
        Player player,
        CancellationToken ct)
    {
        V113GroupChatRequest? request;
        try
        {
            request = V113ChatPackets.ReadGroupChat(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (request is null)
        {
            return;
        }

        var recipients = _chats.GetRecipients(
            player.Character,
            request.Kind,
            request.RecipientCharacterIds);
        if (recipients.Count == 0)
        {
            return;
        }

        var packet = V113ChatPackets.MultiChat(player.Character.Name, request.Text, request.Kind);
        foreach (var recipient in recipients)
        {
            try
            {
                await _sessions.TrySendToCharacterAsync(recipient.CharacterId, packet, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Chat fanout is best-effort; stale sessions are cleaned by the central registry hook.
            }
        }
    }

    private async Task HandleFindAsync(
        PacketReader reader,
        int channel,
        bool buddyFind,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        string targetName;
        try
        {
            targetName = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var target = _chats.FindOnlineByName(targetName);
        if (target is null)
        {
            await sendSelf(V113ChatPackets.WhisperReply(targetName, 0), ct);
            return;
        }

        var packet = target.Channel == channel
            ? V113ChatPackets.FindReplyWithMap(target.Name, target.Character.MapId, buddyFind)
            : V113ChatPackets.FindReplyWithChannel(target.Name, target.Channel, buddyFind);
        await sendSelf(packet, ct);
    }

    private async Task HandleWhisperAsync(
        PacketReader reader,
        Player player,
        int channel,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        string targetName;
        string text;
        try
        {
            targetName = reader.ReadMapleString();
            text = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var target = _chats.FindOnlineByName(targetName);
        if (target is null)
        {
            await sendSelf(V113ChatPackets.WhisperReply(targetName, 0), ct);
            return;
        }

        var sent = false;
        try
        {
            sent = await _sessions.TrySendToCharacterAsync(
                target.CharacterId,
                V113ChatPackets.Whisper(player.Character.Name, channel, text),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            sent = false;
        }

        await sendSelf(V113ChatPackets.WhisperReply(targetName, sent ? (byte)1 : (byte)0), ct);
    }
}
