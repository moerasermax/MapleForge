using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelKeymapPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x7F, V113ChannelRecvOp.ChangeKeymap);
        Assert.Equal(0x163, V113ChannelSendOp.Keymap);
    }

    [Fact]
    public void ParseChangeKeymap_ReadsJavaGeneralLayout()
    {
        var body = new PacketWriter()
            .WriteInt(123456)
            .WriteInt(2)
            .WriteInt(2).WriteByte(4).WriteInt(10)
            .WriteInt(29).WriteByte(5).WriteInt(52)
            .ToArray();

        var request = V113KeymapPackets.ParseChangeKeymap(new PacketReader(body));

        Assert.False(request.IsPetAutoPot);
        Assert.Equal(123456, request.Tick);
        Assert.Equal(2, request.Changes.Count);
        Assert.Equal(new V113KeymapChange(2, 4, 10), request.Changes[0]);
        Assert.Equal(new V113KeymapChange(29, 5, 52), request.Changes[1]);
    }

    [Fact]
    public void ParseChangeKeymap_ShortPacket_IsPetAutoPotBranch()
    {
        var body = new PacketWriter()
            .WriteInt(1)
            .WriteInt(2000000)
            .ToArray();

        var request = V113KeymapPackets.ParseChangeKeymap(new PacketReader(body));

        Assert.True(request.IsPetAutoPot);
        Assert.Equal(1, request.PetAutoPotType);
        Assert.Equal(2000000, request.PetAutoPotItemId);
        Assert.Empty(request.Changes);
    }

    [Fact]
    public void Keymap_WritesNinetySlotsInJavaLayout()
    {
        var character = new Character();
        character.ChangeKeyBinding(2, type: 4, action: 10);
        character.ChangeKeyBinding(29, type: 5, action: 52);

        var packet = V113KeymapPackets.Keymap(character);
        var reader = new PacketReader(packet);

        Assert.Equal(V113ChannelSendOp.Keymap, reader.ReadShort());
        Assert.Equal((byte)0, reader.ReadByte());
        for (var key = 0; key < 90; key++)
        {
            var type = reader.ReadByte();
            var action = reader.ReadInt();
            if (key == 2)
            {
                Assert.Equal((byte)4, type);
                Assert.Equal(10, action);
            }
            else if (key == 29)
            {
                Assert.Equal((byte)5, type);
                Assert.Equal(52, action);
            }
            else
            {
                Assert.Equal((byte)0, type);
                Assert.Equal(0, action);
            }
        }

        Assert.Equal(0, reader.Remaining);
    }
}
