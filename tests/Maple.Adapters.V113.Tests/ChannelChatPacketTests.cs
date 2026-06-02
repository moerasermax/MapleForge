using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

/// <summary>CHATTEXT(0x9B) 封包結構測試（對照 Java getChatText）。</summary>
public class ChannelChatPacketTests
{
    [Fact]
    public void ChatText_BuildsCorrectStructure()
    {
        var pkt = V113MapPackets.ChatText(5, "hi", show: 0);
        var r = new PacketReader(pkt);

        Assert.Equal(V113ChannelSendOp.ChatText, r.ReadShort()); // opcode 0x9B
        Assert.Equal(5, r.ReadInt());                            // cidfrom
        Assert.Equal((byte)0, r.ReadByte());                     // whiteBG
        Assert.Equal("hi", r.ReadMapleString());                 // text
        Assert.Equal((byte)0, r.ReadByte());                     // show
        Assert.Equal(0, r.Remaining);                            // 精確
    }
}
