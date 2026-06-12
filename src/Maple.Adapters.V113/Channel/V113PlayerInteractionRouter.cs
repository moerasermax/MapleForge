using Maple.Application.Trades;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed class V113PlayerInteractionRouter
{
    public const short RecvPlayerInteraction = 0x73;

    private const byte Create = 0x00;
    private const byte InviteTrade = 0x02;
    private const byte DenyTrade = 0x03;
    private const byte Visit = 0x04;
    private const byte Chat = 0x06;
    private const byte Exit = 0x0A;
    private const byte SetItems = 0x0E;
    private const byte SetMeso = 0x0F;
    private const byte ConfirmTrade = 0x10;

    private readonly TradeService _trades;

    public V113PlayerInteractionRouter(TradeService trades)
    {
        _trades = trades;
    }

    public async Task HandleAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        TradeOperationResult result;
        try
        {
            var action = reader.ReadByte();
            result = action switch
            {
                Create => HandleCreate(reader, player),
                InviteTrade => _trades.InviteTrade(player, reader.ReadInt()),
                DenyTrade => _trades.DenyTrade(player),
                Visit => _trades.VisitTrade(player),
                Chat => _trades.Chat(player, reader.ReadMapleString()),
                Exit => _trades.CancelTrade(player),
                SetItems => HandleSetItems(reader, player),
                SetMeso => _trades.OfferMeso(player, reader.ReadInt()),
                ConfirmTrade => _trades.ConfirmTrade(player),

                // TODO(batch-5 central): player shop / hired merchant / omok / match-card actions share PLAYER_INTERACTION.
                _ => TradeOperationResult.Empty(TradeServiceStatus.InvalidAction),
            };
        }
        catch (InvalidDataException)
        {
            return;
        }

        try
        {
            // best-effort：派送交易通知會送往對手 session。對手故障不可把例外往上拋穿 RunAsync
            // 回呼、連帶斷掉本人連線（本人交易狀態已在 TradeService 內更新完成）。
            await DispatchTradeNoticesAsync(_trades, result, ct);
        }
        catch { /* 對手 session 送包失敗：忽略 */ }

        if (result.Status == TradeServiceStatus.TradeRestricted &&
            _trades.TryGetSender(player.Character.Id, out var sendSelf))
        {
            await sendSelf(V113StatsPackets.EnableActions(), ct);
        }
    }

    public static async Task DispatchTradeNoticesAsync(
        TradeService trades,
        TradeOperationResult result,
        CancellationToken ct)
    {
        foreach (var notice in result.Notices)
        {
            var packet = V113TradePackets.Encode(notice);
            if (packet is null || !trades.TryGetSender(notice.RecipientCharacterId, out var send))
            {
                continue;
            }

            await send(packet, ct);
        }
    }

    private TradeOperationResult HandleCreate(PacketReader reader, Player player)
    {
        var createType = reader.ReadByte();
        if (createType == 3)
        {
            return _trades.StartTrade(player);
        }

        // TODO(batch-5 central): createType 1/2/4/5 belongs to mini-game/player-shop/hired-merchant.
        return TradeOperationResult.Empty(TradeServiceStatus.InvalidAction);
    }

    private TradeOperationResult HandleSetItems(PacketReader reader, Player player)
    {
        var rawType = reader.ReadByte();
        var sourceSlot = reader.ReadShort();
        var quantity = reader.ReadShort();
        var targetSlot = unchecked((sbyte)reader.ReadByte());

        if (!InventoryTypes.IsValid(rawType))
        {
            return TradeOperationResult.Empty(TradeServiceStatus.InvalidItem);
        }

        return _trades.OfferItem(player, (InventoryType)rawType, sourceSlot, quantity, targetSlot);
    }
}
