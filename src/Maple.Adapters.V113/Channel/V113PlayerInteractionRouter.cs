using Maple.Application.PlayerShops;
using Maple.Application.Trades;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.PlayerShops;
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
    private const byte Open = 0x0B;
    private const byte SetItems = 0x0E;
    private const byte SetMeso = 0x0F;
    private const byte ConfirmTrade = 0x10;
    private const byte PlayerShopAddItem = 0x13;
    private const byte BuyItemPlayerShop = 0x14;
    private const byte RemoveItemPlayerShop = 0x18;
    private const byte AddItem = 0x1E;
    private const byte BuyItemStore = 0x1F;
    private const byte BuyItemHiredMerchant = 0x21;
    private const byte RemoveItem = 0x23;
    private const byte MaintenanceOff = 0x24;
    private const byte CloseMerchant = 0x26;

    private const byte HiredMerchantCreateType = 5;
    private const int MerchantItemSeriesStart = 5030000;
    private const int MerchantItemSeriesEnd = 5039999;
    private const int MerchantRoomFirstMapId = 910000001;
    private const int MerchantRoomLastMapId = 910000022;

    private readonly TradeService _trades;
    private readonly PlayerShopService? _shops;
    private readonly IHiredMerchantRepository? _merchants;
    private readonly IHiredMerchantSessionDispatcher? _merchantSessions;

    public V113PlayerInteractionRouter(TradeService trades)
        : this(trades, null, null, null)
    {
    }

    public V113PlayerInteractionRouter(
        TradeService trades,
        PlayerShopService? shops,
        IHiredMerchantRepository? merchants,
        IHiredMerchantSessionDispatcher? merchantSessions = null)
    {
        _trades = trades;
        _shops = shops;
        _merchants = merchants;
        _merchantSessions = merchantSessions;
    }

    public Task<bool> HandleAsync(PacketReader reader, Player player, CancellationToken ct)
        => HandleAsync(reader, player, null, null, channel: 0, DateTimeOffset.UtcNow, ct);

    public async Task<bool> HandleAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        Func<byte[], CancellationToken, Task>? broadcastMap,
        byte channel,
        DateTimeOffset now,
        CancellationToken ct)
    {
        TradeOperationResult tradeResult;
        try
        {
            var action = reader.ReadByte();
            switch (action)
            {
                case Create:
                    return await HandleCreateAsync(reader, player, sendSelf, channel, now, ct).ConfigureAwait(false);
                case InviteTrade:
                    tradeResult = _trades.InviteTrade(player, reader.ReadInt());
                    break;
                case DenyTrade:
                    tradeResult = _trades.DenyTrade(player);
                    break;
                case Visit:
                    if (player.IsTrading)
                    {
                        tradeResult = _trades.VisitTrade(player);
                        break;
                    }

                    return await HandleVisitMerchantAsync(reader, player, sendSelf, broadcastMap, channel, now, ct)
                        .ConfigureAwait(false);
                case Chat:
                    if (player.IsTrading)
                    {
                        tradeResult = _trades.Chat(player, reader.ReadMapleString());
                        break;
                    }

                    return await HandleMerchantChatAsync(reader, player, sendSelf, broadcastMap, ct)
                        .ConfigureAwait(false);
                case Exit:
                    if (player.IsTrading)
                    {
                        tradeResult = _trades.CancelTrade(player);
                        break;
                    }

                    return await HandleMerchantExitAsync(player, sendSelf, broadcastMap, ct).ConfigureAwait(false);
                case Open:
                case MaintenanceOff:
                    return await HandleOpenMerchantAsync(player, sendSelf, broadcastMap, now, ct).ConfigureAwait(false);
                case SetItems:
                    tradeResult = HandleSetItems(reader, player);
                    break;
                case SetMeso:
                    tradeResult = _trades.OfferMeso(player, reader.ReadInt());
                    break;
                case ConfirmTrade:
                    tradeResult = _trades.ConfirmTrade(player);
                    break;
                case PlayerShopAddItem:
                case AddItem:
                    return await HandleAddMerchantItemAsync(reader, player, sendSelf, ct).ConfigureAwait(false);
                case BuyItemPlayerShop:
                case BuyItemStore:
                case BuyItemHiredMerchant:
                    if (player.IsTrading)
                    {
                        tradeResult = _trades.ConfirmTrade(player);
                        break;
                    }

                    return await HandleBuyMerchantItemAsync(reader, player, sendSelf, broadcastMap, now, ct)
                        .ConfigureAwait(false);
                case RemoveItemPlayerShop:
                case RemoveItem:
                    return await HandleRemoveMerchantItemAsync(reader, player, sendSelf, ct).ConfigureAwait(false);
                case CloseMerchant:
                    return await HandleCloseMerchantAsync(player, sendSelf, broadcastMap, now, ct).ConfigureAwait(false);
                default:
                    tradeResult = TradeOperationResult.Empty(TradeServiceStatus.InvalidAction);
                    break;
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }

        try
        {
            // best-effort：派送交易通知會送往對手 session。對手故障不可把例外往上拋穿 RunAsync
            // 回呼、連帶斷掉本人連線（本人交易狀態已在 TradeService 內更新完成）。
            await DispatchTradeNoticesAsync(_trades, tradeResult, ct);
        }
        catch { /* 對手 session 送包失敗：忽略 */ }

        if (tradeResult.Status == TradeServiceStatus.TradeRestricted &&
            _trades.TryGetSender(player.Character.Id, out var tradeSelf))
        {
            await tradeSelf(V113StatsPackets.EnableActions(), ct);
        }

        return false;
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

    private async Task<bool> HandleCreateAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        byte channel,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var createType = reader.ReadByte();
        if (createType == 3)
        {
            var result = _trades.StartTrade(player);
            await DispatchTradeNoticesAsync(_trades, result, ct).ConfigureAwait(false);
            return false;
        }

        if (createType != HiredMerchantCreateType || !MerchantServicesAvailable)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var title = reader.ReadMapleString();
        _ = reader.ReadByte();
        var cashSlot = reader.ReadShort();
        var itemId = reader.ReadInt();

        if (!IsMerchantRoomMap(player.Character.MapId) || !IsMerchantItem(itemId))
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var permit = player.Inventory.By(InventoryType.Cash).Get(cashSlot);
        if (permit is null || permit.ItemId != itemId || permit.Quantity <= 0)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var create = await _shops!.CreateHiredMerchantAsync(
                player,
                itemId,
                string.IsNullOrWhiteSpace(title) ? player.Character.Name : title.Trim(),
                player.Character.MapId,
                channel,
                now,
                position: player.Position,
                cancellationToken: ct)
            .ConfigureAwait(false);
        if (create.Status != PlayerShopServiceStatus.Success || create.Merchant is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        player.OpenShop(create.Merchant.StoreId);
        RegisterMerchantSession(player, create.Merchant.StoreId, sendSelf);
        await SendAsync(sendSelf, V113HiredMerchantPackets.OpenHiredMerchant(player, create.Merchant, firstTime: true, now), ct)
            .ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandleVisitMerchantAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        Func<byte[], CancellationToken, Task>? broadcastMap,
        byte channel,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!MerchantServicesAvailable)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var objectId = reader.ReadInt();
        var merchant = await FindOpenMerchantByMapObjectAsync(channel, player.Character.MapId, objectId, ct)
            .ConfigureAwait(false);
        if (merchant is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        if (merchant.IsOwner(player.Character.Id, player.Character.Name))
        {
            var maintenance = await _shops!.EnterMaintenanceAsync(merchant.StoreId, player, ct).ConfigureAwait(false);
            if (maintenance.Status != PlayerShopServiceStatus.Success || maintenance.Merchant is null)
            {
                await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
                return false;
            }

            player.OpenShop(maintenance.Merchant.StoreId);
            RegisterMerchantSession(player, maintenance.Merchant.StoreId, sendSelf);
            await SendAsync(sendSelf, V113HiredMerchantPackets.OpenHiredMerchant(player, maintenance.Merchant, firstTime: false, now), ct)
                .ConfigureAwait(false);
            await BroadcastAsync(broadcastMap, V113HiredMerchantPackets.DestroyHiredMerchant(maintenance.Merchant.OwnerId), ct)
                .ConfigureAwait(false);
            return false;
        }

        var visit = await _shops!.EnterMerchantAsync(merchant.StoreId, player, now, ct).ConfigureAwait(false);
        if (visit.Status != PlayerShopServiceStatus.Success || visit.Merchant is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        player.OpenShop(visit.Merchant.StoreId);
        RegisterMerchantSession(player, visit.Merchant.StoreId, sendSelf);
        await SendAsync(sendSelf, V113HiredMerchantPackets.OpenHiredMerchant(player, visit.Merchant, firstTime: false, now), ct)
            .ConfigureAwait(false);
        await SendToMerchantParticipantsAsync(
                visit.Merchant,
                V113HiredMerchantPackets.ShopVisitorAdd(player.Character, visit.Slot),
                sendSelf,
                ct,
                exceptCharacterId: player.Character.Id)
            .ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandleMerchantChatAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        Func<byte[], CancellationToken, Task>? broadcastMap,
        CancellationToken ct)
    {
        if (!MerchantServicesAvailable || player.ActiveShopId is not { } storeId)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var message = reader.ReadMapleString();
        var merchant = await _merchants!.FindByStoreIdAsync(storeId, ct).ConfigureAwait(false);
        if (merchant is null || !CanUseMerchant(player, merchant))
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        RegisterMerchantSession(player, storeId, sendSelf);
        var slot = merchant.IsOwner(player.Character.Id, player.Character.Name)
            ? 0
            : merchant.State.Visitors.FirstOrDefault(v => v.CharacterId == player.Character.Id)?.Slot ?? 0;
        var packet = V113HiredMerchantPackets.ShopChat($"{player.Character.Name} : {message}", slot);
        await SendToMerchantParticipantsAsync(merchant, packet, sendSelf, ct).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandleMerchantExitAsync(
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        Func<byte[], CancellationToken, Task>? broadcastMap,
        CancellationToken ct)
    {
        if (!MerchantServicesAvailable || player.ActiveShopId is not { } storeId)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var merchant = await _merchants!.FindByStoreIdAsync(storeId, ct).ConfigureAwait(false);
        if (merchant is null)
        {
            player.CloseShop();
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var leave = await _shops!.LeaveMerchantAsync(storeId, player, ct).ConfigureAwait(false);
        if (leave.Status == PlayerShopServiceStatus.Success && leave.Merchant is not null && leave.Slot > 0)
        {
            await SendToMerchantParticipantsAsync(
                    leave.Merchant,
                    V113HiredMerchantPackets.ShopVisitorLeave(leave.Slot),
                    sendSelf,
                    ct,
                    exceptCharacterId: player.Character.Id)
                .ConfigureAwait(false);
            _merchantSessions?.Deregister(storeId, player.Character.Id);
        }

        player.CloseShop();
        await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandleOpenMerchantAsync(
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        Func<byte[], CancellationToken, Task>? broadcastMap,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!MerchantServicesAvailable || player.ActiveShopId is not { } storeId)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var open = await _shops!.OpenMerchantAsync(storeId, player, now, ct).ConfigureAwait(false);
        if (open.Status != PlayerShopServiceStatus.Success || open.Merchant is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        player.CloseShop();
        _merchantSessions?.Clear(open.Merchant.StoreId);
        await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
        await BroadcastAsync(broadcastMap, V113HiredMerchantPackets.SpawnHiredMerchant(open.Merchant, open.Merchant.Position), ct)
            .ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandleAddMerchantItemAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        CancellationToken ct)
    {
        if (!MerchantServicesAvailable || player.ActiveShopId is not { } storeId)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var rawType = reader.ReadByte();
        var slot = reader.ReadShort();
        var bundles = reader.ReadShort();
        var perBundle = reader.ReadShort();
        var price = reader.ReadInt();
        if (!InventoryTypes.IsValid(rawType))
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var type = (InventoryType)rawType;
        var source = player.Inventory.By(type).Get(slot);
        if (source is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var result = await _shops!.AddListingAsync(storeId, player, type, slot, source.ItemId, bundles, perBundle, price, ct)
            .ConfigureAwait(false);
        if (result.Status != PlayerShopServiceStatus.Success || result.Merchant is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        if (result.Mutation is not null)
        {
            await SendAsync(sendSelf, V113ShopPackets.ModifyInventoryQuantity(result.Mutation), ct).ConfigureAwait(false);
        }

        RegisterMerchantSession(player, storeId, sendSelf);
        await SendToMerchantParticipantsAsync(
                result.Merchant,
                V113HiredMerchantPackets.ShopItemUpdate(result.Merchant),
                sendSelf,
                ct)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandleBuyMerchantItemAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        Func<byte[], CancellationToken, Task>? broadcastMap,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!MerchantServicesAvailable || player.ActiveShopId is not { } storeId)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var itemIndex = reader.ReadByte();
        var quantity = reader.ReadShort();
        var result = await _shops!.BuyAsync(storeId, player, itemIndex, quantity, now, ct).ConfigureAwait(false);
        if (result.Status != PlayerShopServiceStatus.Success || result.Merchant is null || result.GainedItem is null || result.InventoryType is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        await SendAsync(sendSelf, V113ShopPackets.ModifyInventoryAdd(result.InventoryType.Value, result.GainedItem), ct)
            .ConfigureAwait(false);
        await SendAsync(sendSelf, V113ShopPackets.UpdateMeso(player.Character.Meso, itemReaction: true), ct)
            .ConfigureAwait(false);
        RegisterMerchantSession(player, storeId, sendSelf);
        await SendToMerchantParticipantsAsync(
                result.Merchant,
                V113HiredMerchantPackets.ShopItemUpdate(result.Merchant),
                sendSelf,
                ct)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandleRemoveMerchantItemAsync(
        PacketReader reader,
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        CancellationToken ct)
    {
        if (!MerchantServicesAvailable || player.ActiveShopId is not { } storeId)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var listingIndex = reader.ReadShort();
        var result = await _shops!.TakeListingAsync(storeId, player, listingIndex, ct).ConfigureAwait(false);
        if (result.Status != PlayerShopServiceStatus.Success ||
            result.Merchant is null ||
            result.ReturnedInventoryType is null ||
            result.ReturnedItem is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        await SendAsync(sendSelf, V113ShopPackets.ModifyInventoryAdd(result.ReturnedInventoryType.Value, result.ReturnedItem), ct)
            .ConfigureAwait(false);
        RegisterMerchantSession(player, storeId, sendSelf);
        await SendToMerchantParticipantsAsync(
                result.Merchant,
                V113HiredMerchantPackets.ShopItemUpdate(result.Merchant),
                sendSelf,
                ct)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandleCloseMerchantAsync(
        Player player,
        Func<byte[], CancellationToken, Task>? sendSelf,
        Func<byte[], CancellationToken, Task>? broadcastMap,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!MerchantServicesAvailable || player.ActiveShopId is not { } storeId)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        var result = await _shops!.CloseForClaimAsync(storeId, player, now, ct).ConfigureAwait(false);
        if (result.Status != PlayerShopServiceStatus.Success || result.Merchant is null)
        {
            await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
            return false;
        }

        player.CloseShop();
        _merchantSessions?.Clear(result.Merchant.StoreId);
        await SendAsync(sendSelf, V113HiredMerchantPackets.ShopErrorMessage(0x15, 0), ct).ConfigureAwait(false);
        await SendEnableActionsAsync(sendSelf, ct).ConfigureAwait(false);
        await BroadcastAsync(broadcastMap, V113HiredMerchantPackets.DestroyHiredMerchant(result.Merchant.OwnerId), ct)
            .ConfigureAwait(false);
        return false;
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

    private async Task<HiredMerchant?> FindOpenMerchantByMapObjectAsync(
        byte channel,
        int mapId,
        int objectId,
        CancellationToken ct)
    {
        var merchants = await _merchants!.FindOpenByMapAsync(channel, mapId, ct).ConfigureAwait(false);
        return merchants.FirstOrDefault(m => m.OwnerId == objectId || m.StoreId == objectId);
    }

    private bool MerchantServicesAvailable => _shops is not null && _merchants is not null;

    private static bool CanUseMerchant(Player player, HiredMerchant merchant)
        => merchant.IsOwner(player.Character.Id, player.Character.Name) ||
           merchant.State.Visitors.Any(v => v.CharacterId == player.Character.Id);

    private static bool IsMerchantItem(int itemId)
        => itemId is >= MerchantItemSeriesStart and <= MerchantItemSeriesEnd;

    private static bool IsMerchantRoomMap(int mapId)
        => mapId is >= MerchantRoomFirstMapId and <= MerchantRoomLastMapId;

    private void RegisterMerchantSession(
        Player player,
        int storeId,
        Func<byte[], CancellationToken, Task>? sendSelf)
    {
        if (sendSelf is not null)
        {
            _merchantSessions?.Register(storeId, player.Character.Id, sendSelf);
        }
    }

    private async Task SendToMerchantParticipantsAsync(
        HiredMerchant merchant,
        byte[] packet,
        Func<byte[], CancellationToken, Task>? fallbackSelf,
        CancellationToken ct,
        int? exceptCharacterId = null)
    {
        if (_merchantSessions is not null)
        {
            await _merchantSessions
                .SendToParticipantsAsync(merchant, packet, ct, exceptCharacterId)
                .ConfigureAwait(false);
            return;
        }

        await SendAsync(fallbackSelf, packet, ct).ConfigureAwait(false);
    }

    private static Task SendEnableActionsAsync(
        Func<byte[], CancellationToken, Task>? sendSelf,
        CancellationToken ct)
        => SendAsync(sendSelf, V113StatsPackets.EnableActions(), ct);

    private static async Task SendAsync(
        Func<byte[], CancellationToken, Task>? send,
        byte[] packet,
        CancellationToken ct)
    {
        if (send is not null)
        {
            await send(packet, ct).ConfigureAwait(false);
        }
    }

    private static async Task BroadcastAsync(
        Func<byte[], CancellationToken, Task>? broadcastMap,
        byte[] packet,
        CancellationToken ct)
    {
        if (broadcastMap is not null)
        {
            await broadcastMap(packet, ct).ConfigureAwait(false);
        }
    }
}
