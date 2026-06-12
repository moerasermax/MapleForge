using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.Trade;

public enum TradeSide : byte
{
    Initiator = 0,
    Invitee = 1,
}

public enum TradeCancelReason
{
    Canceled,
    InventoryFull,
    PickupRestricted,
}

public enum TradeOfferItemStatus
{
    Success,
    Closed,
    NoPartner,
    Locked,
    OfferFull,
    InvalidQuantity,
    ItemMissing,
    TradeRestricted,
}

public enum TradeOfferMesoStatus
{
    Success,
    Closed,
    NoPartner,
    Locked,
    InvalidAmount,
    NotEnoughMeso,
}

public enum TradeConfirmStatus
{
    WaitingForPartner,
    Completed,
    Canceled,
    Closed,
    NoPartner,
    Locked,
}

public sealed record TradeOfferItemResult(
    TradeOfferItemStatus Status,
    TradeOfferItem? OfferItem = null);

public sealed record TradeOfferMesoResult(
    TradeOfferMesoStatus Status,
    int TotalMeso = 0);

public sealed record TradeConfirmResult(
    TradeConfirmStatus Status,
    TradeCancelReason CancelReason = TradeCancelReason.Canceled);

public sealed record TradeOfferItem(
    InventoryType InventoryType,
    short OriginalSlot,
    byte TradeSlot,
    Item Item);

public sealed class TradeOffer
{
    private readonly List<TradeOfferItem> _items = [];

    internal TradeOffer(Player owner, TradeSide side)
    {
        Owner = owner;
        Side = side;
    }

    public Player Owner { get; }

    public TradeSide Side { get; }

    public IReadOnlyList<TradeOfferItem> Items => _items;

    public int Meso { get; private set; }

    public bool Locked { get; internal set; }

    internal bool IsFull => _items.Count >= 9;

    internal byte NextTradeSlot()
    {
        if (_items.Count >= 9)
        {
            return 0;
        }

        byte slot = 1;
        while (_items.Any(item => item.TradeSlot == slot))
        {
            slot++;
        }

        return slot;
    }

    internal bool ContainsTradeSlot(byte slot) => _items.Any(item => item.TradeSlot == slot);

    internal void AddItem(TradeOfferItem item) => _items.Add(item);

    internal void AddMeso(int meso) => Meso += meso;

    internal void Clear()
    {
        _items.Clear();
        Meso = 0;
        Locked = false;
    }
}

public sealed class MapleTrade
{
    private readonly TradeOffer _initiatorOffer;
    private TradeOffer? _inviteeOffer;

    public MapleTrade(Player initiator)
    {
        ArgumentNullException.ThrowIfNull(initiator);

        Initiator = initiator;
        _initiatorOffer = new TradeOffer(initiator, TradeSide.Initiator);
        initiator.AttachTrade(this);
    }

    public Player Initiator { get; }

    public Player? Invitee { get; private set; }

    public bool InviteeJoined { get; private set; }

    public bool IsClosed { get; private set; }

    public TradeOffer InitiatorOffer => _initiatorOffer;

    public TradeOffer? InviteeOffer => _inviteeOffer;

    public bool HasBothParticipants => Invitee is not null && _inviteeOffer is not null;

    public bool TryAttachInvitee(Player invitee)
    {
        ArgumentNullException.ThrowIfNull(invitee);

        if (IsClosed || Invitee is not null || invitee.IsTrading)
        {
            return false;
        }

        Invitee = invitee;
        _inviteeOffer = new TradeOffer(invitee, TradeSide.Invitee);
        invitee.AttachTrade(this);
        return true;
    }

    public void MarkJoined(Player player)
    {
        if (IsClosed)
        {
            return;
        }

        if (ReferenceEquals(player, Invitee))
        {
            InviteeJoined = true;
        }
    }

    public bool HasParticipant(Player player)
        => ReferenceEquals(player, Initiator) || ReferenceEquals(player, Invitee);

    public TradeSide GetSide(Player player)
    {
        if (ReferenceEquals(player, Initiator))
        {
            return TradeSide.Initiator;
        }

        if (ReferenceEquals(player, Invitee))
        {
            return TradeSide.Invitee;
        }

        throw new InvalidOperationException("Player is not part of this trade.");
    }

    public TradeOffer GetOffer(Player player)
        => GetSide(player) == TradeSide.Initiator
            ? _initiatorOffer
            : _inviteeOffer ?? throw new InvalidOperationException("Trade invitee is not attached.");

    public TradeOffer GetOffer(TradeSide side)
        => side == TradeSide.Initiator
            ? _initiatorOffer
            : _inviteeOffer ?? throw new InvalidOperationException("Trade invitee is not attached.");

    public Player? GetPartner(Player player)
        => GetSide(player) == TradeSide.Initiator ? Invitee : Initiator;

    public TradeOfferItemResult OfferItem(
        Player player,
        InventoryType type,
        short sourceSlot,
        short quantity,
        sbyte requestedTradeSlot)
    {
        if (IsClosed)
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.Closed);
        }

        if (!HasBothParticipants)
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.NoPartner);
        }

        var offer = GetOffer(player);
        if (offer.Locked)
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.Locked);
        }

        if (offer.IsFull)
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.OfferFull);
        }

        if (sourceSlot <= 0 || quantity <= 0)
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.InvalidQuantity);
        }

        var bag = player.Inventory.By(type);
        var source = bag.Get(sourceSlot);
        if (source is null)
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.ItemMissing);
        }

        if (IsTradeRestricted(source))
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.TradeRestricted);
        }

        if ((source.IsEquip || type == InventoryType.Equip || type == InventoryType.Cash) && quantity != 1)
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.InvalidQuantity);
        }

        if (!bag.TryTake(sourceSlot, quantity, out var taken) || taken is null)
        {
            return new TradeOfferItemResult(TradeOfferItemStatus.ItemMissing);
        }

        var tradeSlot = ResolveTradeSlot(offer, requestedTradeSlot);
        if (tradeSlot == 0)
        {
            RestoreItem(player, new TradeOfferItem(type, sourceSlot, 0, taken));
            return new TradeOfferItemResult(TradeOfferItemStatus.OfferFull);
        }

        taken.Slot = tradeSlot;
        var offerItem = new TradeOfferItem(type, sourceSlot, tradeSlot, taken);
        offer.AddItem(offerItem);
        player.FlushInventory();
        return new TradeOfferItemResult(TradeOfferItemStatus.Success, offerItem);
    }

    public TradeOfferMesoResult OfferMeso(Player player, int meso)
    {
        if (IsClosed)
        {
            return new TradeOfferMesoResult(TradeOfferMesoStatus.Closed);
        }

        if (!HasBothParticipants)
        {
            return new TradeOfferMesoResult(TradeOfferMesoStatus.NoPartner);
        }

        var offer = GetOffer(player);
        if (offer.Locked)
        {
            return new TradeOfferMesoResult(TradeOfferMesoStatus.Locked, offer.Meso);
        }

        if (meso <= 0 || offer.Meso + meso <= 0)
        {
            return new TradeOfferMesoResult(TradeOfferMesoStatus.InvalidAmount, offer.Meso);
        }

        if (player.Character.Meso < meso)
        {
            return new TradeOfferMesoResult(TradeOfferMesoStatus.NotEnoughMeso, offer.Meso);
        }

        player.GainMeso(-meso);
        offer.AddMeso(meso);
        return new TradeOfferMesoResult(TradeOfferMesoStatus.Success, offer.Meso);
    }

    public TradeConfirmResult Confirm(Player player)
    {
        if (IsClosed)
        {
            return new TradeConfirmResult(TradeConfirmStatus.Closed);
        }

        if (!HasBothParticipants)
        {
            return new TradeConfirmResult(TradeConfirmStatus.NoPartner);
        }

        var offer = GetOffer(player);
        if (offer.Locked)
        {
            return new TradeConfirmResult(TradeConfirmStatus.Locked);
        }

        offer.Locked = true;
        var partnerOffer = GetOffer(GetSide(player) == TradeSide.Initiator ? TradeSide.Invitee : TradeSide.Initiator);
        if (!partnerOffer.Locked)
        {
            return new TradeConfirmResult(TradeConfirmStatus.WaitingForPartner);
        }

        var failure = CheckCompletionFailure();
        if (failure is not null)
        {
            Cancel(failure.Value);
            return new TradeConfirmResult(TradeConfirmStatus.Canceled, failure.Value);
        }

        Complete();
        return new TradeConfirmResult(TradeConfirmStatus.Completed);
    }

    public void Cancel(TradeCancelReason reason = TradeCancelReason.Canceled)
    {
        if (IsClosed)
        {
            return;
        }

        RestoreOffer(_initiatorOffer);
        if (_inviteeOffer is not null)
        {
            RestoreOffer(_inviteeOffer);
        }

        Close();
    }

    private void Complete()
    {
        var invitee = Invitee ?? throw new InvalidOperationException("Cannot complete a trade without invitee.");
        var inviteeOffer = _inviteeOffer ?? throw new InvalidOperationException("Cannot complete a trade without invitee offer.");

        TransferOffer(_initiatorOffer, invitee);
        TransferOffer(inviteeOffer, Initiator);

        Initiator.FlushInventory();
        invitee.FlushInventory();
        Close();
    }

    private TradeCancelReason? CheckCompletionFailure()
    {
        var invitee = Invitee;
        var inviteeOffer = _inviteeOffer;
        if (invitee is null || inviteeOffer is null)
        {
            return TradeCancelReason.Canceled;
        }

        if (!CanReceive(Initiator, inviteeOffer) || !CanReceive(invitee, _initiatorOffer))
        {
            return TradeCancelReason.InventoryFull;
        }

        return null;
    }

    private static bool CanReceive(Player receiver, TradeOffer incoming)
    {
        var mesoGain = TradeTax.GetNetMeso(incoming.Meso);
        if (mesoGain > 0 && (long)receiver.Character.Meso + mesoGain > int.MaxValue)
        {
            return false;
        }

        foreach (var group in incoming.Items.GroupBy(item => item.InventoryType))
        {
            var bag = receiver.Inventory.By(group.Key);
            var freeSlots = bag.SlotLimit - bag.Items.Count;
            if (freeSlots < group.Count())
            {
                return false;
            }
        }

        return true;
    }

    private static void TransferOffer(TradeOffer offer, Player receiver)
    {
        foreach (var offerItem in offer.Items)
        {
            var item = offerItem.Item.Copy();
            item.Slot = 0;
            TradeItemFlags.ClearKarmaFlags(item);
            if (receiver.Inventory.By(offerItem.InventoryType).Gain(item) is null)
            {
                throw new InvalidOperationException("Trade receive precheck failed.");
            }
        }

        var mesoGain = TradeTax.GetNetMeso(offer.Meso);
        if (mesoGain > 0)
        {
            receiver.GainMeso(mesoGain);
        }

        offer.Clear();
    }

    private static void RestoreOffer(TradeOffer offer)
    {
        foreach (var item in offer.Items)
        {
            RestoreItem(offer.Owner, item);
        }

        if (offer.Meso > 0)
        {
            offer.Owner.GainMeso(offer.Meso);
        }

        offer.Owner.FlushInventory();
        offer.Clear();
    }

    private static void RestoreItem(Player owner, TradeOfferItem offerItem)
    {
        var bag = owner.Inventory.By(offerItem.InventoryType);
        var item = offerItem.Item.Copy();
        item.Slot = offerItem.OriginalSlot;

        var originalSlot = bag.Get(offerItem.OriginalSlot);
        if (originalSlot is not null &&
            !originalSlot.IsEquip &&
            !item.IsEquip &&
            originalSlot.ItemId == item.ItemId)
        {
            originalSlot.Quantity = (short)(originalSlot.Quantity + item.Quantity);
            return;
        }

        if (bag.TryPut(offerItem.OriginalSlot, item))
        {
            return;
        }

        item.Slot = 0;
        if (bag.Gain(item) is null)
        {
            throw new InvalidOperationException("Unable to restore trade item to owner inventory.");
        }
    }

    private void Close()
    {
        IsClosed = true;
        Initiator.ClearTrade(this);
        Invitee?.ClearTrade(this);
    }

    private static byte ResolveTradeSlot(TradeOffer offer, sbyte requestedTradeSlot)
    {
        if (requestedTradeSlot is <= 0 or > 9)
        {
            return offer.NextTradeSlot();
        }

        var slot = (byte)requestedTradeSlot;
        return offer.ContainsTradeSlot(slot) ? offer.NextTradeSlot() : slot;
    }

    private static bool IsTradeRestricted(Item item)
        => TradeItemFlags.HasLock(item) || TradeItemFlags.HasUntradeable(item);
}

public static class TradeTax
{
    public static int GetTaxAmount(int meso)
    {
        if (meso >= 100_000_000) return RoundJava(meso * 0.06d);
        if (meso >= 25_000_000) return RoundJava(meso * 0.05d);
        if (meso >= 10_000_000) return RoundJava(meso * 0.04d);
        if (meso >= 5_000_000) return RoundJava(meso * 0.03d);
        if (meso >= 1_000_000) return RoundJava(meso * 0.018d);
        if (meso >= 100_000) return RoundJava(meso * 0.008d);
        return 0;
    }

    public static int GetNetMeso(int meso)
        => meso <= 0 ? 0 : meso - GetTaxAmount(meso);

    private static int RoundJava(double value)
        => (int)Math.Floor(value + 0.5d);
}

public static class TradeItemFlags
{
    private const short Lock = 0x01;
    private const short KarmaUse = 0x02;
    private const short Untradeable = 0x08;
    private const short KarmaEquip = 0x10;

    public static bool HasLock(Item item) => (item.Flag & Lock) == Lock;

    public static bool HasUntradeable(Item item) => (item.Flag & Untradeable) == Untradeable;

    public static void ClearKarmaFlags(Item item)
    {
        if ((item.Flag & KarmaEquip) == KarmaEquip)
        {
            item.Flag = (short)(item.Flag - KarmaEquip);
        }
        else if ((item.Flag & KarmaUse) == KarmaUse)
        {
            item.Flag = (short)(item.Flag - KarmaUse);
        }
    }
}
