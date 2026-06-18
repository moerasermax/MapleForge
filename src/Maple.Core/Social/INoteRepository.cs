namespace Maple.Core.Social;

public interface INoteRepository
{
    Task<IReadOnlyList<Note>> GetNotesForCharacterAsync(string name, CancellationToken ct = default);

    Task<Note> AddNoteAsync(Note note, CancellationToken ct = default);

    Task<Note?> DeleteNoteAsync(int id, CancellationToken ct = default);
}
