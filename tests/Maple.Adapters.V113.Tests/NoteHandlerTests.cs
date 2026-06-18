using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.Social;

namespace Maple.Adapters.V113.Tests;

public sealed class NoteHandlerTests
{
    [Fact]
    public void ParseSendNote_ReadsNameMessageFameUnknownAndCashId()
    {
        var body = new PacketWriter()
            .WriteByte(0)
            .WriteMapleString("Receiver")
            .WriteMapleString("hello")
            .WriteByte(1)
            .WriteInt(0)
            .WriteLong(1_234_567_890_123)
            .ToArray();

        var action = V113NotePackets.ParseAction(new PacketReader(body));

        Assert.Equal(0, action.Type);
        Assert.NotNull(action.SendRequest);
        Assert.Equal("Receiver", action.SendRequest.Value.ReceiverName);
        Assert.Equal("hello", action.SendRequest.Value.Message);
        Assert.True(action.SendRequest.Value.Fame);
        Assert.Equal(0, action.SendRequest.Value.Unknown);
        Assert.Equal(1_234_567_890_123, action.SendRequest.Value.CashId);
        Assert.Empty(action.DeleteEntries);
    }

    [Fact]
    public void ShowNotes_WritesJavaShowNotesLayout()
    {
        var packet = V113NotePackets.ShowNotes(new[]
        {
            new Note
            {
                Id = 77,
                SenderName = "Sender",
                ReceiverName = "Receiver",
                Message = "gift",
                Timestamp = 60_000,
                Fame = 1,
                Read = false,
            },
        });

        var reader = new PacketReader(packet);

        Assert.Equal(V113NotePackets.SendShowNotes, reader.ReadShort());
        Assert.Equal(V113NotePackets.ShowNotesMode, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(77, reader.ReadInt());
        Assert.Equal("Sender", reader.ReadMapleString());
        Assert.Equal("gift", reader.ReadMapleString());
        Assert.Equal(KoreanTimestamp(60_000), ReadLong(reader));
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    private static long ReadLong(PacketReader reader)
    {
        var low = (uint)reader.ReadInt();
        var high = (uint)reader.ReadInt();
        return unchecked((long)(((ulong)high << 32) | low));
    }

    private static long KoreanTimestamp(long unixMillis) =>
        ((unixMillis / 1000 / 60) * 600000000) + 116444592000000000L;
}
