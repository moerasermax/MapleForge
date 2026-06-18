using LiteDB;
using Maple.Core.Social;

namespace Maple.Persistence.Notes;

public sealed class LiteDbNoteRepository : INoteRepository
{
    private readonly ILiteCollection<Note> _collection;

    public LiteDbNoteRepository(LiteDatabase database)
    {
        _collection = database.GetCollection<Note>("notes");
        _collection.EnsureIndex(n => n.ReceiverName);
    }

    public Task<IReadOnlyList<Note>> GetNotesForCharacterAsync(string name, CancellationToken ct = default)
    {
        var notes = _collection
            .Find(n => n.ReceiverName == name)
            .OrderBy(static n => n.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<Note>>(notes);
    }

    public Task<Note> AddNoteAsync(Note note, CancellationToken ct = default)
    {
        note.Fame = note.Fame > 0 ? 1 : 0;
        var id = _collection.Insert(note);
        if (note.Id == 0 && id.IsInt32)
        {
            note.Id = id.AsInt32;
        }

        return Task.FromResult(note);
    }

    public Task<Note?> DeleteNoteAsync(int id, CancellationToken ct = default)
    {
        var note = _collection.FindById(id);
        if (note is null)
        {
            return Task.FromResult<Note?>(null);
        }

        _collection.Delete(id);
        return Task.FromResult<Note?>(note);
    }
}
