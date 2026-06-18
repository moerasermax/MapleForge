using Maple.Core.Social;

namespace Maple.Application.Social;

public sealed record NoteSendResult(bool Success, Note? Note = null);

public sealed record NoteDeleteResult(bool Success, int FameDelta = 0);

public sealed class NoteService
{
    private readonly INoteRepository _notes;
    private readonly TimeProvider _timeProvider;

    public NoteService(INoteRepository notes, TimeProvider? timeProvider = null)
    {
        _notes = notes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<NoteSendResult> SendNoteAsync(
        string senderName,
        string receiverName,
        string message,
        bool fame,
        CancellationToken ct = default)
    {
        var sender = senderName.Trim();
        var receiver = receiverName.Trim();
        if (string.IsNullOrWhiteSpace(sender) ||
            string.IsNullOrWhiteSpace(receiver) ||
            string.IsNullOrWhiteSpace(message))
        {
            return new NoteSendResult(false);
        }

        var note = new Note
        {
            SenderName = sender,
            ReceiverName = receiver,
            Message = message,
            Fame = fame ? 1 : 0,
            Timestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
            Read = false,
        };

        var added = await _notes.AddNoteAsync(note, ct).ConfigureAwait(false);
        return new NoteSendResult(true, added);
    }

    public Task<IReadOnlyList<Note>> GetNotesAsync(string characterName, CancellationToken ct = default)
    {
        var name = characterName.Trim();
        return string.IsNullOrWhiteSpace(name)
            ? Task.FromResult<IReadOnlyList<Note>>(Array.Empty<Note>())
            : _notes.GetNotesForCharacterAsync(name, ct);
    }

    public async Task<NoteDeleteResult> DeleteNoteAsync(
        int noteId,
        bool gainFame,
        CancellationToken ct = default)
    {
        if (noteId <= 0)
        {
            return new NoteDeleteResult(false);
        }

        var deleted = await _notes.DeleteNoteAsync(noteId, ct).ConfigureAwait(false);
        if (deleted is null)
        {
            return new NoteDeleteResult(false);
        }

        var fameDelta = gainFame && deleted.Fame > 0 ? 1 : 0;
        return new NoteDeleteResult(true, fameDelta);
    }
}
