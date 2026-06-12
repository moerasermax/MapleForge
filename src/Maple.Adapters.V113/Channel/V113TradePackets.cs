using Maple.Application.Trades;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Trade;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal static class V113TradePackets
{
    public const short SendPlayerInteraction = 0x146;

    public const byte ActionInvite = 0x02;
    public const byte ActionVisit = 0x04;
    public const byte ActionChat = 0x06;
    public const byte ActionExit = 0x0A;
    public const byte ActionSetItems = 0x0E;
    public const byte ActionSetMeso = 0x0F;
    public const byte ActionConfirm = 0x10;

    public static byte[]? Encode(TradeNotice notice)
        => notice.Kind switch
        {
            TradeNoticeKind.Start => TradeStart(GetRecipient(notice), notice.Trade, notice.RecipientSide),
            TradeNoticeKind.Invite when notice.Subject is not null => TradeInvite(notice.Subject),
            TradeNoticeKind.PartnerAdd when notice.Subject is not null => TradePartnerAdd(notice.Subject),
            TradeNoticeKind.ItemAdd when notice.OfferItem is not null => TradeItemAdd(notice.View, notice.OfferItem.Item),
            TradeNoticeKind.MesoSet => TradeMesoSet(notice.View, notice.Meso),
            TradeNoticeKind.Confirmation => TradeConfirmation(),
            TradeNoticeKind.Completion => TradeCompletion(notice.RecipientSide),
            TradeNoticeKind.Cancel => TradeCancel(notice.RecipientSide, notice.CancelReason),
            TradeNoticeKind.Chat when notice.Subject is not null => TradeChat($"{notice.Subject.Character.Name} : {notice.Message}", 1),
            _ => null,
        };

    public static byte[] TradePartnerAdd(Player player)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(ActionVisit);
        w.WriteByte(1);
        AddCharLook(w, player.Character);
        w.WriteMapleString(player.Character.Name);
        return w.ToArray();
    }

    public static byte[] TradeInvite(Player inviter)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(ActionInvite);
        w.WriteByte(3);
        w.WriteMapleString(inviter.Character.Name);
        w.WriteInt(0);
        return w.ToArray();
    }

    public static byte[] TradeMesoSet(TradeNoticeView view, int meso)
    {
        var w = new PacketWriter(8);
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(ActionSetMeso);
        w.WriteByte((byte)view);
        w.WriteInt(meso);
        return w.ToArray();
    }

    public static byte[] TradeItemAdd(TradeNoticeView view, Item item)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(ActionSetItems);
        w.WriteByte((byte)view);
        AddTradeItemInfo(w, item);
        return w.ToArray();
    }

    public static byte[] TradeStart(Player recipient, MapleTrade trade, TradeSide side)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(5);
        w.WriteByte(3);
        w.WriteByte(2);
        w.WriteByte((byte)side);

        if (side == TradeSide.Invitee)
        {
            var partner = trade.Initiator;
            w.WriteByte(0);
            AddCharLook(w, partner.Character);
            w.WriteMapleString(partner.Character.Name);
        }

        w.WriteByte((byte)side);
        AddCharLook(w, recipient.Character);
        w.WriteMapleString(recipient.Character.Name);
        w.WriteByte(0xFF);
        return w.ToArray();
    }

    public static byte[] TradeConfirmation()
    {
        var w = new PacketWriter(3);
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(ActionConfirm);
        return w.ToArray();
    }

    public static byte[] TradeCompletion(TradeSide side)
    {
        var w = new PacketWriter(5);
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(ActionExit);
        w.WriteByte((byte)side);
        w.WriteByte(0x08);
        return w.ToArray();
    }

    public static byte[] TradeCancel(TradeSide side, TradeCancelReason reason)
    {
        var w = new PacketWriter(5);
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(ActionExit);
        w.WriteByte((byte)side);
        w.WriteByte(reason switch
        {
            TradeCancelReason.InventoryFull => 9,
            TradeCancelReason.PickupRestricted => 10,
            _ => 2,
        });
        return w.ToArray();
    }

    public static byte[] TradeChat(string message, int slot)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPlayerInteraction);
        w.WriteByte(ActionChat);
        w.WriteByte(9);
        w.WriteByte(slot);
        w.WriteMapleString(message);
        return w.ToArray();
    }

    private static Player GetRecipient(TradeNotice notice)
        => notice.RecipientSide == TradeSide.Initiator
            ? notice.Trade.Initiator
            : notice.Trade.Invitee ?? throw new InvalidOperationException("Trade invitee is missing.");

    private static void AddTradeItemInfo(PacketWriter w, Item item)
    {
        w.WriteByte(item.Slot);

        var encodedInventoryAdd = V113ShopPackets.ModifyInventoryAdd(Player.InventoryTypeOf(item.ItemId), item);
        w.WriteBytes(encodedInventoryAdd.AsSpan(8));
    }

    private static void AddCharLook(PacketWriter w, Character chr)
    {
        w.WriteByte(chr.Gender);
        w.WriteByte(chr.SkinColor);
        w.WriteInt(chr.Face);
        w.WriteByte(0);
        w.WriteInt(chr.Hair);

        foreach (var equip in chr.Equips.Where(static e => e.Position < 0 && e.Position > -100))
        {
            w.WriteByte((byte)(-equip.Position));
            w.WriteInt(equip.ItemId);
        }

        w.WriteByte(0xFF);
        w.WriteByte(0xFF);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteLong(0);
    }
}
