using Maple.Application.OnlinePlayers;
using Maple.Core.Inventory;
using Maple.Core.Trade;
using Maple.Core.World;

namespace Maple.Application.Trades;

public enum TradeServiceStatus
{
    Success,
    NoTrade,
    AlreadyTrading,
    TargetOffline,
    TargetBusy,
    TargetNotInSameMap,
    InvalidAction,
    InvalidItem,
    TradeRestricted,
    OfferFull,
    NotEnoughMeso,
    InventoryFull,
}

public enum TradeNoticeKind
{
    Start,
    Invite,
    PartnerAdd,
    ItemAdd,
    MesoSet,
    Confirmation,
    Completion,
    Cancel,
    Chat,
}

public enum TradeNoticeView : byte
{
    Self = 0,
    Partner = 1,
}

public sealed record TradeNotice(
    int RecipientCharacterId,
    TradeNoticeKind Kind,
    MapleTrade Trade,
    TradeSide RecipientSide,
    TradeNoticeView View = TradeNoticeView.Self,
    Player? Subject = null,
    TradeOfferItem? OfferItem = null,
    int Meso = 0,
    TradeCancelReason CancelReason = TradeCancelReason.Canceled,
    string Message = "");

public sealed record TradeOperationResult(
    TradeServiceStatus Status,
    IReadOnlyList<TradeNotice> Notices)
{
    public static TradeOperationResult Empty(TradeServiceStatus status)
        => new(status, Array.Empty<TradeNotice>());

    public bool Success => Status == TradeServiceStatus.Success;
}

public sealed class TradeService
{
    private readonly IOnlinePlayerRegistry _onlinePlayers;
    private readonly object _gate = new();
    private readonly Dictionary<int, RegisteredTradePlayer> _players = [];
    private readonly Dictionary<int, MapleTrade> _activeTrades = [];

    public TradeService(IOnlinePlayerRegistry onlinePlayers)
    {
        _onlinePlayers = onlinePlayers;
    }

    public void RegisterPlayer(
        Player player,
        int channel,
        Func<byte[], CancellationToken, Task> sendPacket,
        object token)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(sendPacket);
        ArgumentNullException.ThrowIfNull(token);

        lock (_gate)
        {
            _players[player.Character.Id] = new RegisteredTradePlayer(player, channel, sendPacket, token);
        }
    }

    public TradeOperationResult DeregisterPlayer(Player player, object token)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(token);

        lock (_gate)
        {
            if (!_players.TryGetValue(player.Character.Id, out var registered) ||
                !ReferenceEquals(registered.Token, token))
            {
                return TradeOperationResult.Empty(TradeServiceStatus.InvalidAction);
            }

            var result = player.Trade is null
                ? TradeOperationResult.Empty(TradeServiceStatus.Success)
                : CancelTradeCore(player, TradeCancelReason.Canceled, includeSelf: false);

            _players.Remove(player.Character.Id);
            return result;
        }
    }

    public bool TryGetSender(int characterId, out Func<byte[], CancellationToken, Task> sendPacket)
    {
        lock (_gate)
        {
            if (_players.TryGetValue(characterId, out var player))
            {
                sendPacket = player.SendPacket;
                return true;
            }
        }

        sendPacket = static (_, _) => Task.CompletedTask;
        return false;
    }

    public TradeOperationResult StartTrade(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            if (player.Trade is not null)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.AlreadyTrading);
            }

            var trade = new MapleTrade(player);
            _activeTrades[player.Character.Id] = trade;
            return new TradeOperationResult(
                TradeServiceStatus.Success,
                new[] { Notice(player, TradeNoticeKind.Start, trade, TradeSide.Initiator) });
        }
    }

    public TradeOperationResult InviteTrade(Player inviter, int targetCharacterId)
    {
        ArgumentNullException.ThrowIfNull(inviter);

        lock (_gate)
        {
            if (inviter.Trade is not { } trade)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            var onlineTarget = _onlinePlayers.FindById(targetCharacterId);
            if (onlineTarget is null || !_players.TryGetValue(targetCharacterId, out var target))
            {
                return TradeOperationResult.Empty(TradeServiceStatus.TargetOffline);
            }

            if (target.Player.Trade is not null || target.Player.ActiveShopId is not null)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.TargetBusy);
            }

            if (onlineTarget.Character.MapId != inviter.Character.MapId)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.TargetNotInSameMap);
            }

            if (!trade.TryAttachInvitee(target.Player))
            {
                return TradeOperationResult.Empty(TradeServiceStatus.TargetBusy);
            }

            _activeTrades[targetCharacterId] = trade;
            return new TradeOperationResult(
                TradeServiceStatus.Success,
                new[] { Notice(target.Player, TradeNoticeKind.Invite, trade, TradeSide.Invitee, Subject: inviter) });
        }
    }

    public TradeOperationResult VisitTrade(Player visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        lock (_gate)
        {
            if (visitor.Trade is not { } trade || trade.Invitee is null)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            if (!ReferenceEquals(visitor, trade.Invitee))
            {
                return TradeOperationResult.Empty(TradeServiceStatus.InvalidAction);
            }

            trade.MarkJoined(visitor);
            return new TradeOperationResult(
                TradeServiceStatus.Success,
                new[]
                {
                    Notice(trade.Initiator, TradeNoticeKind.PartnerAdd, trade, TradeSide.Initiator, Subject: visitor),
                    Notice(visitor, TradeNoticeKind.Start, trade, TradeSide.Invitee),
                });
        }
    }

    public TradeOperationResult DenyTrade(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            return player.Trade is null
                ? TradeOperationResult.Empty(TradeServiceStatus.NoTrade)
                : CancelTradeCore(player, TradeCancelReason.Canceled, includeSelf: true);
        }
    }

    public TradeOperationResult CancelTrade(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            return player.Trade is null
                ? TradeOperationResult.Empty(TradeServiceStatus.NoTrade)
                : CancelTradeCore(player, TradeCancelReason.Canceled, includeSelf: true);
        }
    }

    public TradeOperationResult OfferItem(
        Player player,
        InventoryType type,
        short sourceSlot,
        short quantity,
        sbyte targetTradeSlot)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            if (player.Trade is not { } trade)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            var result = trade.OfferItem(player, type, sourceSlot, quantity, targetTradeSlot);
            if (result.Status != TradeOfferItemStatus.Success || result.OfferItem is null)
            {
                return TradeOperationResult.Empty(MapOfferItemStatus(result.Status));
            }

            var partner = trade.GetPartner(player);
            if (partner is null)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            return new TradeOperationResult(
                TradeServiceStatus.Success,
                new[]
                {
                    Notice(player, TradeNoticeKind.ItemAdd, trade, trade.GetSide(player), TradeNoticeView.Self, OfferItem: result.OfferItem),
                    Notice(partner, TradeNoticeKind.ItemAdd, trade, trade.GetSide(partner), TradeNoticeView.Partner, OfferItem: result.OfferItem),
                });
        }
    }

    public TradeOperationResult OfferMeso(Player player, int meso)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            if (player.Trade is not { } trade)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            var result = trade.OfferMeso(player, meso);
            if (result.Status != TradeOfferMesoStatus.Success)
            {
                return TradeOperationResult.Empty(MapOfferMesoStatus(result.Status));
            }

            var partner = trade.GetPartner(player);
            if (partner is null)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            return new TradeOperationResult(
                TradeServiceStatus.Success,
                new[]
                {
                    Notice(player, TradeNoticeKind.MesoSet, trade, trade.GetSide(player), TradeNoticeView.Self, Meso: result.TotalMeso),
                    Notice(partner, TradeNoticeKind.MesoSet, trade, trade.GetSide(partner), TradeNoticeView.Partner, Meso: result.TotalMeso),
                });
        }
    }

    public TradeOperationResult ConfirmTrade(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            if (player.Trade is not { } trade)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            var initiator = trade.Initiator;
            var invitee = trade.Invitee;
            if (invitee is null)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            var actorSide = trade.GetSide(player);
            var result = trade.Confirm(player);
            switch (result.Status)
            {
                case TradeConfirmStatus.WaitingForPartner:
                    var partner = actorSide == TradeSide.Initiator ? invitee : initiator;
                    return new TradeOperationResult(
                        TradeServiceStatus.Success,
                        new[] { Notice(partner, TradeNoticeKind.Confirmation, trade, trade.GetSide(partner)) });

                case TradeConfirmStatus.Completed:
                    RemoveActiveTrade(initiator, invitee);
                    return new TradeOperationResult(
                        TradeServiceStatus.Success,
                        new[]
                        {
                            Notice(initiator, TradeNoticeKind.Completion, trade, TradeSide.Initiator),
                            Notice(invitee, TradeNoticeKind.Completion, trade, TradeSide.Invitee),
                        });

                case TradeConfirmStatus.Canceled:
                    RemoveActiveTrade(initiator, invitee);
                    return new TradeOperationResult(
                        result.CancelReason == TradeCancelReason.InventoryFull
                            ? TradeServiceStatus.InventoryFull
                            : TradeServiceStatus.InvalidAction,
                        new[]
                        {
                            Notice(initiator, TradeNoticeKind.Cancel, trade, TradeSide.Initiator, CancelReason: result.CancelReason),
                            Notice(invitee, TradeNoticeKind.Cancel, trade, TradeSide.Invitee, CancelReason: result.CancelReason),
                        });

                case TradeConfirmStatus.Locked:
                case TradeConfirmStatus.Closed:
                case TradeConfirmStatus.NoPartner:
                default:
                    return TradeOperationResult.Empty(TradeServiceStatus.InvalidAction);
            }
        }
    }

    public TradeOperationResult Chat(Player player, string message)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(message) || player.Trade is not { } trade)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            var partner = trade.GetPartner(player);
            if (partner is null)
            {
                return TradeOperationResult.Empty(TradeServiceStatus.NoTrade);
            }

            return new TradeOperationResult(
                TradeServiceStatus.Success,
                new[]
                {
                    Notice(partner, TradeNoticeKind.Chat, trade, trade.GetSide(partner), TradeNoticeView.Partner, Subject: player, Message: message),
                });
        }
    }

    private TradeOperationResult CancelTradeCore(Player actor, TradeCancelReason reason, bool includeSelf)
    {
        var trade = actor.Trade ?? throw new InvalidOperationException("Actor is not trading.");
        var initiator = trade.Initiator;
        var invitee = trade.Invitee;
        var notices = new List<TradeNotice>(2);

        if (includeSelf || !ReferenceEquals(actor, initiator))
        {
            notices.Add(Notice(initiator, TradeNoticeKind.Cancel, trade, TradeSide.Initiator, CancelReason: reason));
        }

        if (invitee is not null && (includeSelf || !ReferenceEquals(actor, invitee)))
        {
            notices.Add(Notice(invitee, TradeNoticeKind.Cancel, trade, TradeSide.Invitee, CancelReason: reason));
        }

        trade.Cancel(reason);
        RemoveActiveTrade(initiator, invitee);
        return new TradeOperationResult(TradeServiceStatus.Success, notices);
    }

    private void RemoveActiveTrade(Player initiator, Player? invitee)
    {
        _activeTrades.Remove(initiator.Character.Id);
        if (invitee is not null)
        {
            _activeTrades.Remove(invitee.Character.Id);
        }
    }

    private static TradeNotice Notice(
        Player recipient,
        TradeNoticeKind kind,
        MapleTrade trade,
        TradeSide recipientSide,
        TradeNoticeView view = TradeNoticeView.Self,
        Player? Subject = null,
        TradeOfferItem? OfferItem = null,
        int Meso = 0,
        TradeCancelReason CancelReason = TradeCancelReason.Canceled,
        string Message = "")
        => new(recipient.Character.Id, kind, trade, recipientSide, view, Subject, OfferItem, Meso, CancelReason, Message);

    private static TradeServiceStatus MapOfferItemStatus(TradeOfferItemStatus status)
        => status switch
        {
            TradeOfferItemStatus.TradeRestricted => TradeServiceStatus.TradeRestricted,
            TradeOfferItemStatus.OfferFull => TradeServiceStatus.OfferFull,
            TradeOfferItemStatus.ItemMissing => TradeServiceStatus.InvalidItem,
            TradeOfferItemStatus.InvalidQuantity => TradeServiceStatus.InvalidItem,
            TradeOfferItemStatus.NoPartner => TradeServiceStatus.NoTrade,
            TradeOfferItemStatus.Locked => TradeServiceStatus.InvalidAction,
            TradeOfferItemStatus.Closed => TradeServiceStatus.NoTrade,
            _ => TradeServiceStatus.InvalidAction,
        };

    private static TradeServiceStatus MapOfferMesoStatus(TradeOfferMesoStatus status)
        => status switch
        {
            TradeOfferMesoStatus.NotEnoughMeso => TradeServiceStatus.NotEnoughMeso,
            TradeOfferMesoStatus.InvalidAmount => TradeServiceStatus.InvalidAction,
            TradeOfferMesoStatus.NoPartner => TradeServiceStatus.NoTrade,
            TradeOfferMesoStatus.Locked => TradeServiceStatus.InvalidAction,
            TradeOfferMesoStatus.Closed => TradeServiceStatus.NoTrade,
            _ => TradeServiceStatus.InvalidAction,
        };

    private sealed record RegisteredTradePlayer(
        Player Player,
        int Channel,
        Func<byte[], CancellationToken, Task> SendPacket,
        object Token);
}
