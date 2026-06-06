using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelDropPacketTests
{
    [Fact]
    public void ParseItemPickup_ReadsJavaPickupLayout()
    {
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteByte(0)
            .WriteShort(30)
            .WriteShort(40)
            .WriteInt(1_000_000)
            .ToArray();

        var req = V113DropPackets.ParseItemPickup(new PacketReader(body));

        Assert.Equal(1234, req.Tick);
        Assert.Equal((short)30, req.ClientPosition.X);
        Assert.Equal((short)40, req.ClientPosition.Y);
        Assert.Equal(1_000_000, req.ObjectId);
    }

    [Fact]
    public void DropItemFromMapObject_Item_WritesJavaLayout()
    {
        var drop = MapDrop.ForItem(
            1_000_000,
            new Item { ItemId = 4000000, Quantity = 2 },
            new Position(30, 40, 0, 7),
            new Position(10, 20, 0, 7),
            sourceObjectId: 100001,
            ownerId: 1,
            dropType: 0);

        var r = new PacketReader(V113DropPackets.DropItemFromMapObject(drop));

        Assert.Equal(V113DropPackets.SendDropItemFromMapObject, r.ReadShort());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(1_000_000, r.ReadInt());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(4000000, r.ReadInt());
        Assert.Equal(1, r.ReadInt());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(30, r.ReadShort());
        Assert.Equal(40, r.ReadShort());
        Assert.Equal(1, r.ReadInt());
        Assert.Equal(10, r.ReadShort());
        Assert.Equal(20, r.ReadShort());
        Assert.Equal(0, r.ReadShort());
        r.Skip(8);
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void RemoveItemFromMap_Pickup_WritesAnimationAndCharacter()
    {
        byte[] expected =
        {
            0x08, 0x01,
            0x02,
            0x40, 0x42, 0x0F, 0x00,
            0x01, 0x00, 0x00, 0x00,
        };

        Assert.Equal(expected, V113DropPackets.RemoveItemFromMap(1_000_000, animation: 2, characterId: 1));
    }

    [Fact]
    public void ShowExpGainMonster_WritesShowStatusInfoSubtype3()
    {
        var r = new PacketReader(V113DropPackets.ShowExpGainMonster(7));

        Assert.Equal(V113DropPackets.SendShowStatusInfo, r.ReadShort());
        Assert.Equal(3, r.ReadByte());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(7, r.ReadInt());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void UpdateExp_WritesSingleExpStat()
    {
        byte[] expected =
        {
            0x1D, 0x00,
            0x00,
            0x00, 0x00, 0x01, 0x00,
            0x2A, 0x00, 0x00, 0x00,
        };

        Assert.Equal(expected, V113DropPackets.UpdateExp(42));
    }

    [Fact]
    public void ShowItemGain_WritesNonChatStatusInfo()
    {
        byte[] expected =
        {
            0x25, 0x00,
            0x00, 0x00,
            0x00, 0x09, 0x3D, 0x00,
            0x02, 0x00, 0x00, 0x00,
        };

        Assert.Equal(expected, V113DropPackets.ShowItemGain(4000000, 2));
    }

    [Fact]
    public void ModifyInventoryAdd_ReusesJavaMode0ItemInfo()
    {
        var pkt = V113DropPackets.ModifyInventoryAdd(
            InventoryType.Etc,
            new Item { ItemId = 4000000, Quantity = 2, Slot = 1 });
        var r = new PacketReader(pkt);

        Assert.Equal(V113DropPackets.SendModifyInventoryItem, r.ReadShort());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal((byte)InventoryType.Etc, r.ReadByte());
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(2, r.ReadByte());
        Assert.Equal(4000000, r.ReadInt());
        Assert.Equal(0, r.ReadByte());
        r.Skip(8);
        Assert.Equal(2, r.ReadShort());
    }
}
