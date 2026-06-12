using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.NpcItemServices;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelNpcItemServicePacketTests
{
    [Fact]
    public void RepairWindow_WritesJavaLayout()
    {
        var packet = V113RepairPackets.RepairWindow(2080000);

        Assert.Equal(new byte[]
        {
            0xD5, 0x00,
            0x22, 0x00, 0x00, 0x00,
            0x00, 0xBD, 0x1F, 0x00,
        }, packet);
    }

    [Fact]
    public void RepairConstants_RecordJavaRecvPropertiesConflict()
    {
        Assert.Equal(0x72, V113RepairPackets.CommentedRecvRepairAll);
        Assert.Equal(0x73, V113RepairPackets.CommentedRecvRepair);
        Assert.Equal(unchecked((short)0xFFFE), V113RepairPackets.EffectiveUnmappedRecvValue);
    }

    [Fact]
    public void ModifyInventoryRepair_WritesJavaUpdateMode()
    {
        var packet = V113RepairPackets.ModifyInventoryRepair(new EquipRepairMutation(-11, 1302000, 1, 100));

        Assert.Equal(new byte[]
        {
            0x1B, 0x00,
            0x00,
            0x01,
            0x01,
            0x01,
            0xF5, 0xFF,
            0x01, 0x00,
        }, packet);
    }

    [Fact]
    public void OwlOpen_WritesJavaHotItemLayout()
    {
        var reader = new PacketReader(V113OwlPackets.OwlOpen());

        Assert.Equal(V113OwlPackets.SendShopScannerResult, reader.ReadShort());
        Assert.Equal(7, reader.ReadByte());
        Assert.Equal(10, reader.ReadByte());
        Assert.Equal(1082002, reader.ReadInt());
    }

    [Fact]
    public void OwlSearched_EmptyResult_WritesMode6AndCountZero()
    {
        var reader = new PacketReader(V113OwlPackets.OwlSearched(2000000, Array.Empty<OwlSearchEntry>()));

        Assert.Equal(V113OwlPackets.SendShopScannerResult, reader.ReadShort());
        Assert.Equal(6, reader.ReadByte());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(2000000, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void OwlSearched_UseResult_WritesMerchantSummary()
    {
        var packet = V113OwlPackets.OwlSearched(
            2000000,
            new[]
            {
                new OwlSearchEntry(
                    "Seller",
                    910000001,
                    "FM shop",
                    Quantity: 2,
                    Bundles: 3,
                    Price: 1234,
                    ListingObjectId: 9001,
                    ChannelIndex: 0,
                    InventoryType.Use),
            });
        var reader = new PacketReader(packet);

        reader.Skip(11);
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal("Seller", reader.ReadMapleString());
        Assert.Equal(910000001, reader.ReadInt());
        Assert.Equal("FM shop", reader.ReadMapleString());
        Assert.Equal(2, reader.ReadInt());
        Assert.Equal(3, reader.ReadInt());
        Assert.Equal(1234, reader.ReadInt());
        Assert.Equal(9001, reader.ReadInt());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal((byte)InventoryType.Use, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }
}
