using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelItemUsePacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x45, V113ItemUsePackets.RecvUseSummonBag);
        Assert.Equal(0x47, V113ItemUsePackets.RecvUseMountFood);
        Assert.Equal(0x4B, V113ItemUsePackets.RecvUseCatchItem);
        Assert.Equal(0x4F, V113ItemUsePackets.RecvUseReturnScroll);
        Assert.Equal(0x2D, V113ItemUsePackets.SendSetTamingMobInfo);
        Assert.Equal(unchecked((short)0xF5), V113ItemUsePackets.SendCatchMonster);
    }

    [Fact]
    public void ParseUseInventoryItem_ReadsTickSlotAndItemId()
    {
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteShort(2)
            .WriteInt(2260000)
            .ToArray();

        var request = V113ItemUsePackets.ParseUseInventoryItem(new PacketReader(body));

        Assert.Equal(1234, request.Tick);
        Assert.Equal(2, request.Slot);
        Assert.Equal(2260000, request.ItemId);
    }

    [Fact]
    public void ParseUseCatchItem_ReadsMobObjectId()
    {
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteShort(2)
            .WriteInt(2270004)
            .WriteInt(100001)
            .ToArray();

        var request = V113ItemUsePackets.ParseUseCatchItem(new PacketReader(body));

        Assert.Equal(1234, request.Tick);
        Assert.Equal(2, request.Slot);
        Assert.Equal(2270004, request.ItemId);
        Assert.Equal(100001, request.MobObjectId);
    }

    [Fact]
    public void UpdateMount_WritesJavaLayout()
    {
        var mount = new PlayerMountState(itemId: 1902000, skillId: 1004, level: 2, exp: 6, fatigue: 10);
        var r = new PacketReader(V113ItemUsePackets.UpdateMount(1, mount, levelUp: true));

        Assert.Equal(0x2D, r.ReadShort());
        Assert.Equal(1, r.ReadInt());
        Assert.Equal(2, r.ReadInt());
        Assert.Equal(6, r.ReadInt());
        Assert.Equal(10, r.ReadInt());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void CatchMonster_WritesJavaLayout()
    {
        byte[] expected =
        {
            0xF5, 0x00,
            0x75, 0xE5, 0x0D, 0x00,
            0x34, 0xA3, 0x22, 0x00,
            0x01,
        };

        Assert.Equal(expected, V113ItemUsePackets.CatchMonster(910709, 2270004, 1));
    }

    [Fact]
    public void ModifyInventoryQuantity_Remove_WritesMode3()
    {
        var mutation = new InventoryQuantityMutation(InventoryType.Use, 1, 2260000, 1, 0);
        byte[] expected =
        {
            0x1B, 0x00,
            0x00,
            0x01,
            0x03,
            0x02,
            0x01, 0x00,
        };

        Assert.Equal(expected, V113ItemUsePackets.ModifyInventoryQuantity(mutation));
    }
}
