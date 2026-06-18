using Maple.Application.Social;
using Maple.Core.IO;
using Maple.Core.World;
using Maple.Net;

namespace Maple.Adapters.V113.Channel;

public sealed class V113NoteHandler
{
    private readonly NoteService _notes;

    public V113NoteHandler(NoteService notes)
    {
        _notes = notes;
    }

    public async Task ShowNotesAsync(Player player, MapleSession session, CancellationToken ct)
    {
        var notes = await _notes.GetNotesAsync(player.Character.Name, ct).ConfigureAwait(false);
        await session.SendAsync(V113NotePackets.ShowNotes(notes), ct).ConfigureAwait(false);
    }

    public async Task HandleNoteActionAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        V113NoteAction action;
        try
        {
            action = V113NotePackets.ParseAction(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct).ConfigureAwait(false);
            return;
        }

        switch (action.Type)
        {
            case 0:
                await HandleSendAsync(action, player, session, ct).ConfigureAwait(false);
                break;

            case 1:
                await HandleDeleteAsync(action, player, session, ct).ConfigureAwait(false);
                break;

            default:
                await session.SendAsync(V113StatsPackets.EnableActions(), ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleSendAsync(
        V113NoteAction action,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        if (action.SendRequest is not { } request)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct).ConfigureAwait(false);
            return;
        }

        var result = await _notes
            .SendNoteAsync(player.Character.Name, request.ReceiverName, request.Message, request.Fame, ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct).ConfigureAwait(false);
        }
    }

    private async Task HandleDeleteAsync(
        V113NoteAction action,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        foreach (var entry in action.DeleteEntries)
        {
            var result = await _notes.DeleteNoteAsync(entry.Id, entry.GainFame, ct).ConfigureAwait(false);
            if (result.FameDelta <= 0)
            {
                continue;
            }

            player.Character.Fame = (short)Math.Clamp(
                player.Character.Fame + result.FameDelta,
                short.MinValue,
                short.MaxValue);

            await session.SendAsync(
                V113StatsPackets.UpdateStats(new[]
                {
                    new PlayerStatUpdate(PlayerStatKind.Fame, player.Character.Fame),
                }),
                ct).ConfigureAwait(false);

            await session.SendAsync(V113NotePackets.ShowFameGain(result.FameDelta), ct).ConfigureAwait(false);
        }
    }
}
