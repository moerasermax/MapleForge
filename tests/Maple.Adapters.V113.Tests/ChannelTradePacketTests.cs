using Maple.Adapters.V113.Channel;
using Maple.Application.Trades;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Trade;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelTradePacketTests
{
    [Fact]
    public void TradeInvite_WritesJavaLayout()
    {
        var inviter = NewPlayer(1, "Alice");
        var r = new PacketReader(V113TradePackets.TradeInvite(inviter));

        Assert.Equal(0x146, r.ReadShort());
        Assert.Equal(V113TradePackets.ActionInvite, r.ReadByte());
        Assert.Equal(3, r.ReadByte());
        Assert.Equal("Alice", r.ReadMapleString());
        Assert.Equal(0, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void TradeMesoSet_WritesViewAndMeso()
    {
        var r = new PacketReader(V113TradePackets.TradeMesoSet(TradeNoticeView.Partner, 1234));

        Assert.Equal(0x146, r.ReadShort());
        Assert.Equal(V113TradePackets.ActionSetMeso, r.ReadByte());
        Assert.Equal((byte)TradeNoticeView.Partner, r.ReadByte());
        Assert.Equal(1234, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void TradeItemAdd_WritesPositionThenSharedItemInfo()
    {
        var item = new Item
        {
            ItemId = 2000000,
            Slot = 3,
            Quantity = 2,
            Expiration = -1,
        };
        var r = new PacketReader(V113TradePackets.TradeItemAdd(TradeNoticeView.Self, item));

        Assert.Equal(0x146, r.ReadShort());
        Assert.Equal(V113TradePackets.ActionSetItems, r.ReadByte());
        Assert.Equal((byte)TradeNoticeView.Self, r.ReadByte());
        Assert.Equal(3, r.ReadByte());
        Assert.Equal(2, r.ReadByte());
        Assert.Equal(2000000, r.ReadInt());
        Assert.Equal(0, r.ReadByte());
        r.Skip(8);
        Assert.Equal(2, r.ReadShort());
        Assert.Equal(string.Empty, r.ReadMapleString());
        Assert.Equal(0, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void TradeConfirmationAndCancel_WriteJavaActions()
    {
        Assert.Equal(new byte[] { 0x46, 0x01, 0x10 }, V113TradePackets.TradeConfirmation());
        Assert.Equal(new byte[] { 0x46, 0x01, 0x0A, 0x01, 0x09 }, V113TradePackets.TradeCancel(TradeSide.Invitee, TradeCancelReason.InventoryFull));
        Assert.Equal(new byte[] { 0x46, 0x01, 0x0A, 0x00, 0x08 }, V113TradePackets.TradeCompletion(TradeSide.Initiator));
    }

    [Fact]
    public void TradeStart_ForInvitee_IncludesPartnerThenSelf()
    {
        var alice = NewPlayer(1, "Alice");
        var bob = NewPlayer(2, "Bob");
        var trade = new MapleTrade(alice);
        Assert.True(trade.TryAttachInvitee(bob));

        var r = new PacketReader(V113TradePackets.TradeStart(bob, trade, TradeSide.Invitee));

        Assert.Equal(0x146, r.ReadShort());
        Assert.Equal(5, r.ReadByte());
        Assert.Equal(3, r.ReadByte());
        Assert.Equal(2, r.ReadByte());
        Assert.Equal((byte)TradeSide.Invitee, r.ReadByte());
        Assert.Equal(0, r.ReadByte());
        SkipCharLook(r);
        Assert.Equal("Alice", r.ReadMapleString());
        Assert.Equal((byte)TradeSide.Invitee, r.ReadByte());
        SkipCharLook(r);
        Assert.Equal("Bob", r.ReadMapleString());
        Assert.Equal(0xFF, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    private static Player NewPlayer(int id, string name)
        => new(
            new Character
            {
                Id = id,
                Name = name,
                SkinColor = 0,
                Face = 20000,
                Hair = 30000,
            },
            new Position(0, 0, 0, 0));

    private static void SkipCharLook(PacketReader r)
    {
        r.ReadByte();
        r.ReadByte();
        r.ReadInt();
        r.ReadByte();
        r.ReadInt();
        Assert.Equal(0xFF, r.ReadByte());
        Assert.Equal(0xFF, r.ReadByte());
        r.ReadInt();
        r.ReadInt();
        r.Skip(8);
    }
}
