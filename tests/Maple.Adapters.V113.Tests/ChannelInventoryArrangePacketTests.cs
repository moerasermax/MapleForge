using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelInventoryArrangePacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x3F, V113ChannelRecvOp.ItemSort);
        Assert.Equal(0x40, V113ChannelRecvOp.ItemGather);
        Assert.Equal(0x32, V113ChannelSendOp.GatherItemResult);
        Assert.Equal(0x33, V113ChannelSendOp.SortItemResult);
    }

    [Fact]
    public void ParseArrange_ReadsTickAndInventoryType()
    {
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteByte((byte)InventoryType.Etc)
            .ToArray();

        var request = V113InventoryPackets.ParseArrange(new PacketReader(body));

        Assert.Equal(1234, request.Tick);
        Assert.True(request.IsValidBagType);
        Assert.Equal(InventoryType.Etc, request.Type);
    }

    [Fact]
    public void FinishedSortAndGather_WriteJavaLayouts()
    {
        Assert.Equal(
            new byte[] { 0x32, 0x00, 0x01, 0x04 },
            V113InventoryPackets.FinishedSort((byte)InventoryType.Etc));
        Assert.Equal(
            new byte[] { 0x33, 0x00, 0x01, 0x04 },
            V113InventoryPackets.FinishedGather((byte)InventoryType.Etc));
    }
}
