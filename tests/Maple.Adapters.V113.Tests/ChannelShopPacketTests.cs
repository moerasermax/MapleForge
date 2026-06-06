using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Shops;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelShopPacketTests
{
    [Fact]
    public void OpenNpcShop_WritesJavaLayout()
    {
        var shop = new ShopDefinition(
            35,
            1033002,
            new[]
            {
                new ShopItem(2000000, 50, 1000, 0, 0, 25),
                new ShopItem(2000001, 160, 1000, 0, 0, 80),
            });

        var r = new PacketReader(V113ShopPackets.OpenNpcShop(shop));

        Assert.Equal(V113ShopPackets.SendOpenNpcShop, r.ReadShort());
        Assert.Equal(1033002, r.ReadInt());
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(2000000, r.ReadInt());
        Assert.Equal(50, r.ReadInt());
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(1000, r.ReadShort());
        Assert.Equal(2000001, r.ReadInt());
        Assert.Equal(160, r.ReadInt());
        Assert.Equal(1, r.ReadShort());
        Assert.Equal(1000, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ParseNpcShopBuy_SkipsJavaPadding()
    {
        var w = new PacketWriter();
        w.WriteByte((byte)V113NpcShopAction.Buy);
        w.WriteShort(0);
        w.WriteInt(2000000);
        w.WriteShort(2);

        var request = V113ShopPackets.ParseNpcShop(new PacketReader(w.ToArray()));

        Assert.Equal(V113NpcShopAction.Buy, request.Action);
        Assert.Equal(2000000, request.ItemId);
        Assert.Equal(2, request.Quantity);
    }

    [Fact]
    public void ParseNpcShopSell_ReadsSlotItemQuantity()
    {
        var w = new PacketWriter();
        w.WriteByte((byte)V113NpcShopAction.Sell);
        w.WriteShort(3);
        w.WriteInt(2000000);
        w.WriteShort(2);

        var request = V113ShopPackets.ParseNpcShop(new PacketReader(w.ToArray()));

        Assert.Equal(V113NpcShopAction.Sell, request.Action);
        Assert.Equal(3, request.Slot);
        Assert.Equal(2000000, request.ItemId);
        Assert.Equal(2, request.Quantity);
    }

    [Fact]
    public void ConfirmShopTransaction_WritesCode()
    {
        byte[] expected =
        {
            0x3E, 0x01,
            0x08,
        };

        Assert.Equal(expected, V113ShopPackets.ConfirmShopTransaction(V113ShopPackets.ConfirmSell));
    }

    [Fact]
    public void UpdateMeso_WritesSingleMesoStat()
    {
        var r = new PacketReader(V113ShopPackets.UpdateMeso(900));

        Assert.Equal(V113ShopPackets.SendUpdateStats, r.ReadShort());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0x40000, r.ReadInt());
        Assert.Equal(900, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ModifyInventoryQuantity_Remove_WritesMode3()
    {
        var mutation = new ShopInventoryMutation(InventoryType.Use, 1, 2000000, 1, 0);
        byte[] expected =
        {
            0x1B, 0x00,
            0x00,
            0x01,
            0x03,
            0x02,
            0x01, 0x00,
        };

        Assert.Equal(expected, V113ShopPackets.ModifyInventoryQuantity(mutation));
    }
}
