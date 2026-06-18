using Maple.Core.IO;
using Maple.Core.Social;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113NoteSendRequest(
    string ReceiverName,
    string Message,
    bool Fame,
    int Unknown,
    long CashId);

internal readonly record struct V113NoteDeleteEntry(int Id, bool GainFame);

internal readonly record struct V113NoteAction(
    byte Type,
    V113NoteSendRequest? SendRequest,
    IReadOnlyList<V113NoteDeleteEntry> DeleteEntries);

internal static class V113NotePackets
{
    public const short RecvNoteAction = 0x7B;
    public const short SendShowNotes = 0x26;
    public const short SendShowStatusInfo = 0x25;
    public const byte ShowNotesMode = 3;

    private const long KoreanFileTimeOffset = 116444592000000000L;
    private const long MaxTime = 150842304000000000L;

    public static V113NoteAction ParseAction(PacketReader reader)
    {
        var type = reader.ReadByte();
        return type switch
        {
            0 => ParseSend(reader, type),
            1 => ParseDelete(reader, type),
            _ => new V113NoteAction(type, null, Array.Empty<V113NoteDeleteEntry>()),
        };
    }

    public static byte[] ShowNotes(IReadOnlyList<Note> notes)
    {
        var count = Math.Min(notes.Count, byte.MaxValue);
        var w = new PacketWriter(8 + count * 64);
        w.WriteShort(SendShowNotes);
        w.WriteByte(ShowNotesMode);
        w.WriteByte(count);

        for (var i = 0; i < count; i++)
        {
            var note = notes[i];
            w.WriteInt(note.Id);
            w.WriteMapleString(note.SenderName);
            w.WriteMapleString(note.Message);
            w.WriteLong(GetKoreanTimestamp(note.Timestamp));
            w.WriteByte(note.Fame > 0 ? 1 : 0);
        }

        return w.ToArray();
    }

    public static byte[] ShowFameGain(int fame)
    {
        var w = new PacketWriter(7);
        w.WriteShort(SendShowStatusInfo);
        w.WriteByte(4);
        w.WriteInt(fame);
        return w.ToArray();
    }

    private static V113NoteAction ParseSend(PacketReader reader, byte type)
    {
        var request = new V113NoteSendRequest(
            reader.ReadMapleString(),
            reader.ReadMapleString(),
            reader.ReadByte() > 0,
            reader.ReadInt(),
            ReadLong(reader));

        return new V113NoteAction(type, request, Array.Empty<V113NoteDeleteEntry>());
    }

    private static V113NoteAction ParseDelete(PacketReader reader, byte type)
    {
        var count = reader.ReadByte();
        SkipChecked(reader, 2);
        var entries = new V113NoteDeleteEntry[count];
        for (var i = 0; i < count; i++)
        {
            entries[i] = new V113NoteDeleteEntry(reader.ReadInt(), reader.ReadByte() > 0);
        }

        return new V113NoteAction(type, null, entries);
    }

    private static void SkipChecked(PacketReader reader, int count)
    {
        if (reader.Remaining < count)
        {
            throw new InvalidDataException($"封包不足：需 {count} bytes，剩餘 {reader.Remaining}");
        }

        reader.Skip(count);
    }

    private static long ReadLong(PacketReader reader)
    {
        var low = (uint)reader.ReadInt();
        var high = (uint)reader.ReadInt();
        return unchecked((long)(((ulong)high << 32) | low));
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
