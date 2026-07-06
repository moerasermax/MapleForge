using System.Collections.Concurrent;
using Maple.Core.Inventory;
using Maple.Core.PlayerShops;
using Maple.Core.Shops;
using Maple.Core.World;

namespace Maple.Application.PlayerShops;

public enum PlayerShopServiceStatus
{
    Success,
    MerchantNotFound,
    DuplicateMerchant,
    OwnerMismatch,
    InvalidState,
    InvalidListing,
    InvalidQuantity,
    InvalidPrice,
    RestrictedItem,
    ItemMissing,
    InventoryFull,
    NotEnoughMeso,
    StoreMesoOverflow,
    MesoOverflow,
    Expired,
    Blacklisted,
}

public sealed record HiredMerchantCreateResult(PlayerShopServiceStatus Status, HiredMerchant? Merchant = null);

public sealed record PlayerShopListingResult(
    PlayerShopServiceStatus Status,
    HiredMerchant? Merchant = null,
    ShopInventoryMutation? Mutation = null,
    PlayerShopItemListing? Listing = null,
    InventoryType? ReturnedInventoryType = null,
    Item? ReturnedItem = null);

public sealed record PlayerShopPurchaseUseCaseResult(
    PlayerShopServiceStatus Status,
    HiredMerchant? Merchant = null,
    InventoryType? InventoryType = null,
    Item? GainedItem = null,
    int TotalPrice = 0,
    int BuyerMeso = 0,
    int MerchantMesos = 0);

public sealed record PlayerShopSettlementResult(
    PlayerShopServiceStatus Status,
    HiredMerchant? Merchant = null,
    IReadOnlyList<(InventoryType Type, Item Item)> Items = null!,
    int Mesos = 0)
{
    public IReadOnlyList<(InventoryType Type, Item Item)> Items { get; init; } =
        Items ?? Array.Empty<(InventoryType Type, Item Item)>();
}

public sealed class PlayerShopService
{
    public static readonly TimeSpan DefaultHiredMerchantDuration = TimeSpan.FromDays(1);

    private readonly IHiredMerchantRepository _hiredMerchants;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _merchantLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _ownerLocks = new();

    public PlayerShopService(IHiredMerchantRepository hiredMerchants)
    {
        _hiredMerchants = hiredMerchants;
    }

    public async Task<HiredMerchantCreateResult> CreateHiredMerchantAsync(
        Player owner,
        int itemId,
        string title,
        int mapId,
        byte channel,
        DateTimeOffset now,
        TimeSpan? duration = null,
        Position position = default,
        CancellationToken cancellationToken = default)
    {
        using var ownerLock = await EnterOwnerLockAsync(owner.Character.AccountId, owner.Character.Id, cancellationToken)
            .ConfigureAwait(false);

        if (await _hiredMerchants.FindOpenByOwnerAsync(owner.Character.AccountId, owner.Character.Id, cancellationToken)
                .ConfigureAwait(false) is not null ||
            await _hiredMerchants.FindClaimableByOwnerAsync(owner.Character.AccountId, owner.Character.Id, cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            return new HiredMerchantCreateResult(PlayerShopServiceStatus.DuplicateMerchant);
        }

        var merchant = HiredMerchant.Create(
            owner.Character.Id,
            owner.Character.AccountId,
            owner.Character.Name,
            itemId,
            title,
            mapId,
            channel,
            now,
            duration ?? DefaultHiredMerchantDuration,
            position);

        await _hiredMerchants.AddAsync(merchant, cancellationToken).ConfigureAwait(false);
        return new HiredMerchantCreateResult(PlayerShopServiceStatus.Success, merchant);
    }

    public async Task<HiredMerchantCreateResult> OpenMerchantAsync(
        int storeId,
        Player owner,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        using var merchantLock = await EnterMerchantLockAsync(storeId, cancellationToken).ConfigureAwait(false);

        var merchant = await _hiredMerchants.FindByStoreIdAsync(storeId, cancellationToken).ConfigureAwait(false);
        if (merchant is null)
        {
            return new HiredMerchantCreateResult(PlayerShopServiceStatus.MerchantNotFound);
        }

        if (!merchant.IsOwner(owner.Character.Id, owner.Character.Name))
        {
            return new HiredMerchantCreateResult(PlayerShopServiceStatus.OwnerMismatch);
        }

        merchant.Open(now);
        await _hiredMerchants.UpsertAsync(merchant, cancellationToken).ConfigureAwait(false);
        return new HiredMerchantCreateResult(PlayerShopServiceStatus.Success, merchant);
    }

    public async Task<PlayerShopListingResult> AddListingAsync(
        int storeId,
        Player owner,
        InventoryType type,
        short slot,
        int itemId,
        short bundles,
        short bundleQuantity,
        int price,
        CancellationToken cancellationToken = default)
    {
        using var merchantLock = await EnterMerchantLockAsync(storeId, cancellationToken).ConfigureAwait(false);

        var merchant = await _hiredMerchants.FindByStoreIdAsync(storeId, cancellationToken).ConfigureAwait(false);
        if (merchant is null)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.MerchantNotFound);
        }

        if (!merchant.IsOwner(owner.Character.Id, owner.Character.Name))
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.OwnerMismatch, merchant);
        }

        var source = owner.Inventory.By(type).Get(slot);
        if (source is null || source.ItemId != itemId)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.ItemMissing, merchant);
        }

        if (ItemFlags.Has(source.Flag, ItemFlags.Lock) || ItemFlags.Has(source.Flag, ItemFlags.Untradeable))
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.RestrictedItem, merchant);
        }

        var totalQuantity = (long)bundles * bundleQuantity;
        if (bundles <= 0 || bundleQuantity <= 0 || totalQuantity <= 0 || totalQuantity > short.MaxValue)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.InvalidQuantity, merchant);
        }

        if (price <= 0)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.InvalidPrice, merchant);
        }

        if (source.IsEquip && (bundles != 1 || bundleQuantity != 1))
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.InvalidQuantity, merchant);
        }

        if (source.IsEquip ? totalQuantity != 1 : source.Quantity < totalQuantity)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.ItemMissing, merchant);
        }

        if (merchant.Items.Count >= merchant.State.MaxListings)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.InvalidState, merchant);
        }

        var oldQuantity = source.IsEquip ? (short)1 : source.Quantity;
        if (!owner.Inventory.By(type).TryTake(slot, (short)totalQuantity, out var taken) || taken is null)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.ItemMissing, merchant);
        }

        var added = merchant.TryAddListing(type, taken, bundles, bundleQuantity, price);
        if (added.Status != PlayerShopAddListingStatus.Success)
        {
            owner.Inventory.By(type).Gain(taken);
            owner.FlushInventory();
            return new PlayerShopListingResult(MapAddListingStatus(added.Status), merchant);
        }

        owner.FlushInventory();
        await _hiredMerchants.UpsertAsync(merchant, cancellationToken).ConfigureAwait(false);
        var mutation = new ShopInventoryMutation(type, slot, itemId, oldQuantity, (short)(oldQuantity - totalQuantity));
        return new PlayerShopListingResult(PlayerShopServiceStatus.Success, merchant, mutation, added.Listing);
    }

    public async Task<PlayerShopPurchaseUseCaseResult> BuyAsync(
        int storeId,
        Player buyer,
        int listingIndex,
        short bundleCount,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        using var merchantLock = await EnterMerchantLockAsync(storeId, cancellationToken).ConfigureAwait(false);

        var merchant = await _hiredMerchants.FindByStoreIdAsync(storeId, cancellationToken).ConfigureAwait(false);
        if (merchant is null)
        {
            return new PlayerShopPurchaseUseCaseResult(PlayerShopServiceStatus.MerchantNotFound);
        }

        if (merchant.IsOwner(buyer.Character.Id, buyer.Character.Name))
        {
            return new PlayerShopPurchaseUseCaseResult(PlayerShopServiceStatus.OwnerMismatch, merchant);
        }

        var listing = listingIndex >= 0 && listingIndex < merchant.Items.Count ? merchant.Items[listingIndex] : null;
        var type = listing is null ? (InventoryType?)null : (InventoryType)listing.InventoryType;
        var canHold = type is { } inventoryType && buyer.CanGainItem(inventoryType);

        var bought = merchant.TryBuy(
            listingIndex,
            bundleCount,
            buyer.Character.Meso,
            canHold,
            buyer.Character.Name,
            now);

        if (bought.Status != PlayerShopPurchaseStatus.Success)
        {
            if (bought.Status == PlayerShopPurchaseStatus.Expired)
            {
                await _hiredMerchants.UpsertAsync(merchant, cancellationToken).ConfigureAwait(false);
            }

            return new PlayerShopPurchaseUseCaseResult(MapPurchaseStatus(bought.Status), merchant);
        }

        var gained = buyer.Inventory.By(bought.InventoryType!.Value).Gain(bought.Item!);
        if (gained is null)
        {
            return new PlayerShopPurchaseUseCaseResult(PlayerShopServiceStatus.InventoryFull, merchant);
        }

        buyer.GainMeso(-bought.TotalPrice);
        buyer.FlushInventory();
        await _hiredMerchants.UpsertAsync(merchant, cancellationToken).ConfigureAwait(false);
        return new PlayerShopPurchaseUseCaseResult(
            PlayerShopServiceStatus.Success,
            merchant,
            bought.InventoryType,
            gained,
            bought.TotalPrice,
            buyer.Character.Meso,
            merchant.Mesos);
    }

    public async Task<PlayerShopListingResult> TakeListingAsync(
        int storeId,
        Player owner,
        int listingIndex,
        CancellationToken cancellationToken = default)
    {
        using var merchantLock = await EnterMerchantLockAsync(storeId, cancellationToken).ConfigureAwait(false);

        var merchant = await _hiredMerchants.FindByStoreIdAsync(storeId, cancellationToken).ConfigureAwait(false);
        if (merchant is null)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.MerchantNotFound);
        }

        if (!merchant.IsOwner(owner.Character.Id, owner.Character.Name))
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.OwnerMismatch, merchant);
        }

        var listing = listingIndex >= 0 && listingIndex < merchant.Items.Count ? merchant.Items[listingIndex] : null;
        var type = listing is null ? (InventoryType?)null : (InventoryType)listing.InventoryType;
        var canHold = type is { } inventoryType && owner.CanGainItem(inventoryType);
        var taken = merchant.TryTakeListing(listingIndex, canHold);
        if (taken.Status != PlayerShopTakeItemStatus.Success)
        {
            return new PlayerShopListingResult(MapTakeItemStatus(taken.Status), merchant);
        }

        var returned = owner.Inventory.By(taken.InventoryType!.Value).Gain(taken.Item!);
        if (returned is null)
        {
            return new PlayerShopListingResult(PlayerShopServiceStatus.InventoryFull, merchant);
        }

        owner.FlushInventory();
        await _hiredMerchants.UpsertAsync(merchant, cancellationToken).ConfigureAwait(false);
        return new PlayerShopListingResult(
            PlayerShopServiceStatus.Success,
            merchant,
            ReturnedInventoryType: taken.InventoryType,
            ReturnedItem: returned);
    }

    public async Task<PlayerShopSettlementResult> CollectMesosAsync(
        int storeId,
        Player owner,
        CancellationToken cancellationToken = default)
    {
        using var merchantLock = await EnterMerchantLockAsync(storeId, cancellationToken).ConfigureAwait(false);

        var merchant = await _hiredMerchants.FindByStoreIdAsync(storeId, cancellationToken).ConfigureAwait(false);
        if (merchant is null)
        {
            return new PlayerShopSettlementResult(PlayerShopServiceStatus.MerchantNotFound);
        }

        if (!merchant.IsOwner(owner.Character.Id, owner.Character.Name))
        {
            return new PlayerShopSettlementResult(PlayerShopServiceStatus.OwnerMismatch, merchant);
        }

        if ((long)owner.Character.Meso + merchant.Mesos > int.MaxValue)
        {
            return new PlayerShopSettlementResult(PlayerShopServiceStatus.MesoOverflow, merchant);
        }

        var mesos = merchant.Mesos;
        owner.GainMeso(mesos);
        merchant.ClearMesos();
        await _hiredMerchants.UpsertAsync(merchant, cancellationToken).ConfigureAwait(false);
        return new PlayerShopSettlementResult(PlayerShopServiceStatus.Success, merchant, Mesos: mesos);
    }

    public async Task<PlayerShopSettlementResult> CloseForClaimAsync(
        int storeId,
        Player owner,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        using var merchantLock = await EnterMerchantLockAsync(storeId, cancellationToken).ConfigureAwait(false);

        var merchant = await _hiredMerchants.FindByStoreIdAsync(storeId, cancellationToken).ConfigureAwait(false);
        if (merchant is null)
        {
            return new PlayerShopSettlementResult(PlayerShopServiceStatus.MerchantNotFound);
        }

        if (!merchant.IsOwner(owner.Character.Id, owner.Character.Name))
        {
            return new PlayerShopSettlementResult(PlayerShopServiceStatus.OwnerMismatch, merchant);
        }

        merchant.CloseForClaim(now);
        var settlement = merchant.CreateSettlement();
        await _hiredMerchants.UpsertAsync(merchant, cancellationToken).ConfigureAwait(false);
        return new PlayerShopSettlementResult(PlayerShopServiceStatus.Success, merchant, settlement.Items, settlement.Mesos);
    }

    public async Task<PlayerShopSettlementResult> ClaimAsync(
        Player owner,
        CancellationToken cancellationToken = default)
    {
        using var ownerLock = await EnterOwnerLockAsync(owner.Character.AccountId, owner.Character.Id, cancellationToken)
            .ConfigureAwait(false);

        var merchant = await _hiredMerchants
            .FindClaimableByOwnerAsync(owner.Character.AccountId, owner.Character.Id, cancellationToken)
            .ConfigureAwait(false);
        if (merchant is null)
        {
            return new PlayerShopSettlementResult(PlayerShopServiceStatus.MerchantNotFound);
        }

        var settlement = merchant.CreateSettlement();
        if ((long)owner.Character.Meso + settlement.Mesos > int.MaxValue)
        {
            return new PlayerShopSettlementResult(PlayerShopServiceStatus.MesoOverflow, merchant, settlement.Items, settlement.Mesos);
        }

        if (!CanHoldAll(owner, settlement.Items))
        {
            return new PlayerShopSettlementResult(PlayerShopServiceStatus.InventoryFull, merchant, settlement.Items, settlement.Mesos);
        }

        foreach (var (type, item) in settlement.Items)
        {
            owner.Inventory.By(type).Gain(item);
        }

        owner.GainMeso(settlement.Mesos);
        owner.FlushInventory();
        merchant.MarkClosed();
        await _hiredMerchants.DeleteAsync(merchant.StoreId, cancellationToken).ConfigureAwait(false);
        return new PlayerShopSettlementResult(PlayerShopServiceStatus.Success, merchant, settlement.Items, settlement.Mesos);
    }

    public async Task<int> ExpireOpenMerchantsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var expired = await _hiredMerchants.FindExpiredAsync(now, cancellationToken).ConfigureAwait(false);
        foreach (var merchant in expired)
        {
            using var merchantLock = await EnterMerchantLockAsync(merchant.StoreId, cancellationToken).ConfigureAwait(false);
            merchant.CloseForClaim(now);
            await _hiredMerchants.UpsertAsync(merchant, cancellationToken).ConfigureAwait(false);
        }

        return expired.Count;
    }

    private async Task<LockReleaser> EnterMerchantLockAsync(int storeId, CancellationToken cancellationToken)
    {
        var gate = _merchantLocks.GetOrAdd(storeId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new LockReleaser(gate);
    }

    private async Task<LockReleaser> EnterOwnerLockAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken)
    {
        var key = ownerAccountId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" +
            ownerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var gate = _ownerLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new LockReleaser(gate);
    }

    private readonly struct LockReleaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;

        public LockReleaser(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose() => _gate.Release();
    }

    private static bool CanHoldAll(Player player, IReadOnlyList<(InventoryType Type, Item Item)> items)
    {
        foreach (var group in items.GroupBy(static item => item.Type))
        {
            var bag = player.Inventory.By(group.Key);
            var required = group.Count();
            var free = bag.SlotLimit - bag.Items.Count;
            if (free < required)
            {
                return false;
            }
        }

        return true;
    }

    private static PlayerShopServiceStatus MapAddListingStatus(PlayerShopAddListingStatus status)
        => status switch
        {
            PlayerShopAddListingStatus.Success => PlayerShopServiceStatus.Success,
            PlayerShopAddListingStatus.Closed => PlayerShopServiceStatus.InvalidState,
            PlayerShopAddListingStatus.Full => PlayerShopServiceStatus.InvalidState,
            PlayerShopAddListingStatus.InvalidQuantity => PlayerShopServiceStatus.InvalidQuantity,
            PlayerShopAddListingStatus.InvalidPrice => PlayerShopServiceStatus.InvalidPrice,
            PlayerShopAddListingStatus.RestrictedItem => PlayerShopServiceStatus.RestrictedItem,
            _ => PlayerShopServiceStatus.InvalidState,
        };

    private static PlayerShopServiceStatus MapPurchaseStatus(PlayerShopPurchaseStatus status)
        => status switch
        {
            PlayerShopPurchaseStatus.Success => PlayerShopServiceStatus.Success,
            PlayerShopPurchaseStatus.Closed => PlayerShopServiceStatus.InvalidState,
            PlayerShopPurchaseStatus.Expired => PlayerShopServiceStatus.Expired,
            PlayerShopPurchaseStatus.Blacklisted => PlayerShopServiceStatus.Blacklisted,
            PlayerShopPurchaseStatus.InvalidListing => PlayerShopServiceStatus.InvalidListing,
            PlayerShopPurchaseStatus.InvalidQuantity => PlayerShopServiceStatus.InvalidQuantity,
            PlayerShopPurchaseStatus.SoldOut => PlayerShopServiceStatus.InvalidQuantity,
            PlayerShopPurchaseStatus.NotEnoughMeso => PlayerShopServiceStatus.NotEnoughMeso,
            PlayerShopPurchaseStatus.BuyerInventoryFull => PlayerShopServiceStatus.InventoryFull,
            PlayerShopPurchaseStatus.TotalPriceOverflow => PlayerShopServiceStatus.InvalidQuantity,
            PlayerShopPurchaseStatus.StoreMesoOverflow => PlayerShopServiceStatus.StoreMesoOverflow,
            _ => PlayerShopServiceStatus.InvalidState,
        };

    private static PlayerShopServiceStatus MapTakeItemStatus(PlayerShopTakeItemStatus status)
        => status switch
        {
            PlayerShopTakeItemStatus.Success => PlayerShopServiceStatus.Success,
            PlayerShopTakeItemStatus.Closed => PlayerShopServiceStatus.InvalidState,
            PlayerShopTakeItemStatus.InvalidListing => PlayerShopServiceStatus.InvalidListing,
            PlayerShopTakeItemStatus.InvalidQuantity => PlayerShopServiceStatus.InvalidQuantity,
            PlayerShopTakeItemStatus.OwnerInventoryFull => PlayerShopServiceStatus.InventoryFull,
            _ => PlayerShopServiceStatus.InvalidState,
        };
}
