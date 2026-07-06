using Maple.Application.Parties;
using Maple.Application.PlayerShops;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.PlayerShops;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed record V113HiredMerchantHandleResult(
    bool Handled,
    bool CharacterMutated,
    IReadOnlyList<byte[]> SelfPackets,
    IReadOnlyList<byte[]> MapPackets)
{
    public static V113HiredMerchantHandleResult Empty { get; } =
        new(false, false, Array.Empty<byte[]>(), Array.Empty<byte[]>());
}

internal sealed record V113HiredMerchantCreateRequest(string Title, short CashSlot, int ItemId);

public sealed class V113HiredMerchantHandler
{
    private const int FredrickNpcId = 9030000;
    private const int RemoteControlItemId = 5470000;
    private const int MerchantItemSeriesStart = 5030000;
    private const int MerchantItemSeriesEnd = 5039999;
    private const int FreeMarketEntranceMapId = 910000000;
    private const int MerchantRoomFirstMapId = 910000001;
    private const int MerchantRoomLastMapId = 910000022;

    private readonly PlayerShopService _shops;
    private readonly IHiredMerchantRepository _merchants;
    private readonly IPartyRegistry _parties;

    public V113HiredMerchantHandler(
        PlayerShopService shops,
        IHiredMerchantRepository merchants,
        IPartyRegistry parties)
    {
        _shops = shops;
        _merchants = merchants;
        _parties = parties;
    }

    public async Task<V113HiredMerchantHandleResult> HandleRemoteControlAsync(
        PacketReader reader,
        Player player,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        byte action;
        try
        {
            action = reader.ReadByte();
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly();
        }

        if (action != 3 ||
            !player.HasItem(InventoryType.Cash, RemoteControlItemId) ||
            player.Character.MapId != FreeMarketEntranceMapId ||
            _parties.IsCharacterInParty(player.Character.Id))
        {
            return EnableActionsOnly();
        }

        var merchant = await _merchants
            .FindOpenByOwnerAsync(player.Character.AccountId, player.Character.Id, ct)
            .ConfigureAwait(false);
        if (merchant is not null)
        {
            merchant.EnterMaintenance();
            await _merchants.UpsertAsync(merchant, ct).ConfigureAwait(false);
            return SelfOnly(V113HiredMerchantPackets.OpenHiredMerchant(player, merchant, firstTime: false, now));
        }

        var claimable = await _merchants
            .FindClaimableByOwnerAsync(player.Character.AccountId, player.Character.Id, ct)
            .ConfigureAwait(false);
        if (claimable is not null)
        {
            return SelfOnly(V113HiredMerchantPackets.MerchItemStoreItemData(claimable));
        }

        return EnableActionsOnly();
    }

    public async Task<V113HiredMerchantHandleResult> HandleUseHiredMerchantAsync(
        PacketReader reader,
        Player player,
        byte channel,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (!IsMerchantRoomMap(player.Character.MapId))
        {
            return EnableActionsOnly();
        }

        var claimable = await _merchants
            .FindClaimableByOwnerAsync(player.Character.AccountId, player.Character.Id, ct)
            .ConfigureAwait(false);
        if (claimable is not null)
        {
            return SelfOnly(V113HiredMerchantPackets.MerchItemStore(V113HiredMerchantPackets.MerchItemStoreNoPackage));
        }

        var active = await _merchants
            .FindOpenByOwnerAsync(player.Character.AccountId, player.Character.Id, ct)
            .ConfigureAwait(false);
        if (active is not null)
        {
            return SelfOnly(V113HiredMerchantPackets.ShowMerchItemStore(FredrickNpcId, active.MapId, active.Channel));
        }

        var raw = reader.ReadBytes(reader.Remaining);
        if (TryParseCreateRequest(raw, out var create))
        {
            return await CreateMerchantAsync(player, create, channel, now, ct).ConfigureAwait(false);
        }

        return HasMerchantPermit(player)
            ? SelfOnly(V113HiredMerchantPackets.TitleBox())
            : EnableActionsOnly();
    }

    public async Task<V113HiredMerchantHandleResult> HandleMerchItemStoreAsync(
        PacketReader reader,
        Player player,
        CancellationToken ct = default)
    {
        byte operation;
        try
        {
            operation = reader.ReadByte();
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly();
        }

        return operation switch
        {
            20 => await OpenMerchantItemStoreAsync(reader, player, ct).ConfigureAwait(false),
            25 => SelfOnly(V113HiredMerchantPackets.MerchItemStore(V113HiredMerchantPackets.MerchItemStoreConfirmTakeOut)),
            26 => await ClaimMerchantPackageAsync(player, ct).ConfigureAwait(false),
            27 => V113HiredMerchantHandleResult.Empty,
            _ => EnableActionsOnly(),
        };
    }

    public async Task<IReadOnlyList<byte[]>> SpawnOpenMerchantPacketsAsync(
        byte channel,
        int mapId,
        Position fallbackPosition,
        CancellationToken ct = default)
    {
        if (!IsMerchantRoomMap(mapId))
        {
            return Array.Empty<byte[]>();
        }

        var merchants = await _merchants.FindOpenByMapAsync(channel, mapId, ct).ConfigureAwait(false);
        return merchants
            .Select(merchant => V113HiredMerchantPackets.SpawnHiredMerchant(merchant, fallbackPosition))
            .ToArray();
    }

    private async Task<V113HiredMerchantHandleResult> OpenMerchantItemStoreAsync(
        PacketReader reader,
        Player player,
        CancellationToken ct)
    {
        TryConsumeSecondPassword(reader);

        var active = await _merchants
            .FindOpenByOwnerAsync(player.Character.AccountId, player.Character.Id, ct)
            .ConfigureAwait(false);
        if (active is not null)
        {
            return SelfOnly(V113HiredMerchantPackets.ShowMerchItemStore(FredrickNpcId, active.MapId, active.Channel));
        }

        var claimable = await _merchants
            .FindClaimableByOwnerAsync(player.Character.AccountId, player.Character.Id, ct)
            .ConfigureAwait(false);
        return claimable is null
            ? SelfOnly(V113HiredMerchantPackets.MerchItemStore(V113HiredMerchantPackets.MerchItemStoreNoPackage))
            : SelfOnly(V113HiredMerchantPackets.MerchItemStoreItemData(claimable));
    }

    private async Task<V113HiredMerchantHandleResult> ClaimMerchantPackageAsync(Player player, CancellationToken ct)
    {
        var result = await _shops.ClaimAsync(player, ct).ConfigureAwait(false);
        if (result.Status != PlayerShopServiceStatus.Success)
        {
            var packet = result.Status is PlayerShopServiceStatus.InventoryFull or PlayerShopServiceStatus.MesoOverflow
                ? V113HiredMerchantPackets.MerchItemMessage(V113HiredMerchantPackets.MerchItemClaimInventoryFull)
                : V113HiredMerchantPackets.MerchItemStore(V113HiredMerchantPackets.MerchItemStoreNoPackage);
            return SelfOnly(packet);
        }

        var packets = new List<byte[]>();
        foreach (var (type, item) in result.Items)
        {
            packets.Add(V113ShopPackets.ModifyInventoryAdd(type, item));
        }

        packets.Add(V113ShopPackets.UpdateMeso(player.Character.Meso, itemReaction: true));
        packets.Add(V113HiredMerchantPackets.MerchItemMessage(V113HiredMerchantPackets.MerchItemClaimSuccess));
        return new V113HiredMerchantHandleResult(true, true, packets, Array.Empty<byte[]>());
    }

    private async Task<V113HiredMerchantHandleResult> CreateMerchantAsync(
        Player player,
        V113HiredMerchantCreateRequest request,
        byte channel,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!IsMerchantItem(request.ItemId))
        {
            return EnableActionsOnly();
        }

        var permit = player.Inventory.By(InventoryType.Cash).Get(request.CashSlot);
        if (permit is null || permit.ItemId != request.ItemId || permit.Quantity <= 0)
        {
            return EnableActionsOnly();
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? player.Character.Name : request.Title.Trim();
        var create = await _shops
            .CreateHiredMerchantAsync(player, request.ItemId, title, player.Character.MapId, channel, now, cancellationToken: ct)
            .ConfigureAwait(false);
        if (create.Status != PlayerShopServiceStatus.Success || create.Merchant is null)
        {
            return EnableActionsOnly();
        }

        var open = await _shops.OpenMerchantAsync(create.Merchant.StoreId, player, now, ct).ConfigureAwait(false);
        var merchant = open.Merchant ?? create.Merchant;
        return new V113HiredMerchantHandleResult(
            true,
            false,
            new[] { V113HiredMerchantPackets.OpenHiredMerchant(player, merchant, firstTime: true, now) },
            new[] { V113HiredMerchantPackets.SpawnHiredMerchant(merchant, player.Position) });
    }

    private static bool TryParseCreateRequest(byte[] body, out V113HiredMerchantCreateRequest request)
    {
        request = new V113HiredMerchantCreateRequest(string.Empty, 0, 0);
        if (body.Length < 9)
        {
            return false;
        }

        try
        {
            var reader = new PacketReader(body);
            var title = reader.ReadMapleString();
            if (title.Length > 60 || reader.Remaining < 7)
            {
                return false;
            }

            _ = reader.ReadByte();
            var slot = reader.ReadShort();
            var itemId = reader.ReadInt();
            if (reader.Remaining != 0 || slot <= 0 || !IsMerchantItem(itemId))
            {
                return false;
            }

            request = new V113HiredMerchantCreateRequest(title, slot, itemId);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static void TryConsumeSecondPassword(PacketReader reader)
    {
        if (reader.Remaining < 2)
        {
            return;
        }

        try
        {
            _ = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            // MapleForge does not yet validate second password for Fredrick; malformed optional data is ignored.
        }
    }

    private static bool HasMerchantPermit(Player player)
        => player.Inventory.By(InventoryType.Cash).Items.Any(item => IsMerchantItem(item.ItemId) && item.Quantity > 0);

    private static bool IsMerchantItem(int itemId)
        => itemId is >= MerchantItemSeriesStart and <= MerchantItemSeriesEnd;

    private static bool IsMerchantRoomMap(int mapId)
        => mapId is >= MerchantRoomFirstMapId and <= MerchantRoomLastMapId;

    private static V113HiredMerchantHandleResult EnableActionsOnly()
        => SelfOnly(V113StatsPackets.EnableActions());

    private static V113HiredMerchantHandleResult SelfOnly(byte[] packet)
        => new(true, false, new[] { packet }, Array.Empty<byte[]>());
}
