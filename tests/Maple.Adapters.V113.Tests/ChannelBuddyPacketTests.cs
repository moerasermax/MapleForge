using Maple.Adapters.V113.Channel;
using Maple.Application.Buddies;
using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelBuddyPacketTests
{
    [Fact]
    public void UpdateBuddyList_WritesJavaLayout()
    {
        var packet = V113BuddyPackets.UpdateBuddyList([
            new BuddyEntry
            {
                CharacterId = 42,
                Name = "Buddy",
                Group = "Group",
                Channel = 3,
                Visible = true,
            },
        ]);
        var r = new PacketReader(packet);

        Assert.Equal(V113BuddyPackets.SendBuddyList, r.ReadShort());
        Assert.Equal(7, r.ReadByte());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(42, r.ReadInt());
        r.Skip(15);
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(2, r.ReadInt());
        r.Skip(17);
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void UpdateBuddyList_HidesInvisibleBuddyChannel()
    {
        var packet = V113BuddyPackets.UpdateBuddyList([
            new BuddyEntry
            {
                CharacterId = 42,
                Name = "Buddy",
                Group = "Group",
                Channel = 3,
                Visible = false,
            },
        ]);
        var r = new PacketReader(packet);

        r.Skip(2 + 1 + 1 + 4 + 15 + 1);

        Assert.Equal(-1, r.ReadInt());
    }

    [Fact]
    public void RequestBuddyListAdd_WritesJavaLayout()
    {
        var r = new PacketReader(V113BuddyPackets.RequestBuddyListAdd(7, "Sender"));

        Assert.Equal(V113BuddyPackets.SendBuddyList, r.ReadShort());
        Assert.Equal(9, r.ReadByte());
        Assert.Equal(7, r.ReadInt());
        Assert.Equal("Sender", r.ReadMapleString());
        Assert.Equal(7, r.ReadInt());
        r.Skip(15);
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(0, r.ReadInt());
        r.Skip(17);
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void UpdateBuddyChannel_WritesJavaLayout()
    {
        byte[] expected =
        [
            0x38, 0x00,
            0x14,
            0x2A, 0x00, 0x00, 0x00,
            0x00,
            0x01, 0x00, 0x00, 0x00,
        ];

        Assert.Equal(expected, V113BuddyPackets.UpdateBuddyChannel(42, 1));
    }

    [Fact]
    public void UpdateBuddyCapacity_WritesJavaLayout()
    {
        byte[] expected =
        [
            0x38, 0x00,
            0x15,
            25,
        ];

        Assert.Equal(expected, V113BuddyPackets.UpdateBuddyCapacity(25));
    }

    [Fact]
    public void ParseModify_AddAcceptDelete_ReadsClientPayloads()
    {
        var addWriter = new PacketWriter()
            .WriteByte(1)
            .WriteMapleString("Buddy")
            .WriteMapleString("Group");

        var add = V113BuddyPackets.ParseModify(new PacketReader(addWriter.ToArray()));

        Assert.Equal(BuddyModifyKind.Add, add.Kind);
        Assert.Equal("Buddy", add.BuddyName);
        Assert.Equal("Group", add.Group);

        var accept = V113BuddyPackets.ParseModify(new PacketReader(new PacketWriter().WriteByte(2).WriteInt(42).ToArray()));
        var delete = V113BuddyPackets.ParseModify(new PacketReader(new PacketWriter().WriteByte(3).WriteInt(43).ToArray()));

        Assert.Equal(BuddyModifyKind.Accept, accept.Kind);
        Assert.Equal(42, accept.BuddyCharacterId);
        Assert.Equal(BuddyModifyKind.Delete, delete.Kind);
        Assert.Equal(43, delete.BuddyCharacterId);
    }
}
