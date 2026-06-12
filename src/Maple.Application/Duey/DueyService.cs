using Maple.Core.Characters;
using Maple.Core.Duey;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Duey;

public enum DueyResultStatus
{
    Success,
    NotEnoughMeso,
    RecipientNotFound,
    SameAccount,
    UnableToReceive,
    InventoryFull,
    PackageNotFound,
    InvalidRequest,
}

public sealed record DueySendRequest(
    InventoryType? ItemType,
    short ItemSlot,
    short Quantity,
    int Meso,
    string RecipientName,
    bool QuickDelivery,
    string Message);

public sealed record DueySendResult(
    DueyResultStatus Status,
    int Meso,
    IReadOnlyList<DueyInventoryMutation> InventoryMutations,
    DueyPackage? Package = null)
{
    public static DueySendResult Fail(DueyResultStatus status, int meso)
        => new(status, meso, Array.Empty<DueyInventoryMutation>());
}

public sealed record DueyReceiveResult(
    DueyResultStatus Status,
    int PackageId,
    int Meso,
    InventoryType? GainedItemType = null,
    Item? GainedItem = null)
{
    public static DueyReceiveResult Fail(DueyResultStatus status, int packageId, int meso)
        => new(status, packageId, meso);
}

public sealed record DueyReturnResult(
    DueyResultStatus Status,
    int PackageId,
    DueyPackage? ReturnedPackage = null)
{
    public static DueyReturnResult Fail(DueyResultStatus status, int packageId)
        => new(status, packageId);
}

/// <summary>Duey 宅配用例。協定 operation byte 與封包格式由 adapter 負責。</summary>
public sealed class DueyService
{
    public const int RegularDeliveryFee = 5_000;
    public const int QuickDeliveryTicketItemId = 5_330_000;
    public const int MaxMesoPerPackage = 100_000_000;
    public const int PackageLifetimeDays = 20;

    private readonly IDueyPackageRepository _packages;
    private readonly ICharacterRepository _characters;
    private readonly TimeProvider _timeProvider;

    public DueyService(
        IDueyPackageRepository packages,
        ICharacterRepository characters,
        TimeProvider? timeProvider = null)
    {
        _packages = packages;
        _characters = characters;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<DueyPackage>> GetInboxAsync(Player player, CancellationToken ct = default)
    {
        var now = NowUnixMillis();
        await _packages.DeleteExpiredAsync(player.Character.Id, now, ct).ConfigureAwait(false);
        return await _packages.GetInboxAsync(player.Character.Id, now, ct).ConfigureAwait(false);
    }

    public async Task<DueySendResult> SendAsync(
        Player sender,
        DueySendRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RecipientName) ||
            request.Meso < 0 ||
            request.Meso > MaxMesoPerPackage)
        {
            return DueySendResult.Fail(DueyResultStatus.InvalidRequest, sender.Character.Meso);
        }

        var recipient = await _characters.FindByNameAsync(request.RecipientName, ct).ConfigureAwait(false);
        if (recipient is null)
        {
            return DueySendResult.Fail(DueyResultStatus.RecipientNotFound, sender.Character.Meso);
        }

        if (recipient.Id == sender.Character.Id || recipient.AccountId == sender.Character.AccountId)
        {
            return DueySendResult.Fail(DueyResultStatus.SameAccount, sender.Character.Meso);
        }

        var finalCost = CalculateFinalCost(request.Meso, request.QuickDelivery);
        if (sender.Character.Meso < finalCost)
        {
            return DueySendResult.Fail(DueyResultStatus.NotEnoughMeso, sender.Character.Meso);
        }

        ItemRecord? packageItem = null;
        if (request.ItemType is { } type)
        {
            if (!TrySnapshotSendItem(sender, type, request.ItemSlot, request.Quantity, out packageItem))
            {
                return DueySendResult.Fail(DueyResultStatus.UnableToReceive, sender.Character.Meso);
            }
        }

        if (request.QuickDelivery && !sender.HasItem(InventoryType.Cash, QuickDeliveryTicketItemId))
        {
            return DueySendResult.Fail(DueyResultStatus.UnableToReceive, sender.Character.Meso);
        }

        var now = NowUnixMillis();
        var package = new DueyPackage
        {
            SenderName = sender.Character.Name,
            RecipientCharacterId = recipient.Id,
            Meso = request.Meso,
            Item = packageItem,
            Message = request.Message,
            CreatedAtUnixMillis = now,
            ExpiresAtUnixMillis = now + (long)TimeSpan.FromDays(PackageLifetimeDays).TotalMilliseconds,
            Checked = true,
        };

        var mutations = new List<DueyInventoryMutation>(request.QuickDelivery ? 2 : 1);
        if (request.ItemType is { } sendType &&
            !TakeItemForPackage(sender, sendType, request.ItemSlot, request.Quantity, mutations))
        {
            return DueySendResult.Fail(DueyResultStatus.UnableToReceive, sender.Character.Meso);
        }

        DueyInventoryMutation? ticketMutation = null;
        if (request.QuickDelivery &&
            !sender.TryTakeDueyItemById(InventoryType.Cash, QuickDeliveryTicketItemId, 1, out ticketMutation))
        {
            return DueySendResult.Fail(DueyResultStatus.UnableToReceive, sender.Character.Meso);
        }

        if (ticketMutation is not null)
        {
            mutations.Add(ticketMutation);
        }

        await _packages.AddAsync(package, ct).ConfigureAwait(false);
        sender.GainMeso(-finalCost);
        sender.FlushInventory();
        await _characters.UpdateAsync(sender.Character, ct).ConfigureAwait(false);

        return new DueySendResult(DueyResultStatus.Success, sender.Character.Meso, mutations, package);
    }

    public async Task<DueyReceiveResult> ReceiveAsync(
        Player recipient,
        int packageId,
        CancellationToken ct = default)
    {
        var now = NowUnixMillis();
        var package = await _packages
            .FindForRecipientAsync(packageId, recipient.Character.Id, now, ct)
            .ConfigureAwait(false);

        if (package is null)
        {
            return DueyReceiveResult.Fail(DueyResultStatus.PackageNotFound, packageId, recipient.Character.Meso);
        }

        InventoryType? itemType = null;
        if (package.Item is not null)
        {
            if (!InventoryTypes.IsValid(package.Item.Type))
            {
                return DueyReceiveResult.Fail(DueyResultStatus.UnableToReceive, packageId, recipient.Character.Meso);
            }

            itemType = (InventoryType)package.Item.Type;
            if (!recipient.CanReceiveDueyItem(itemType.Value))
            {
                return DueyReceiveResult.Fail(DueyResultStatus.InventoryFull, packageId, recipient.Character.Meso);
            }
        }

        var nextMeso = (long)recipient.Character.Meso + package.Meso;
        if (package.Meso < 0 || nextMeso is < 0 or > int.MaxValue)
        {
            return DueyReceiveResult.Fail(DueyResultStatus.UnableToReceive, packageId, recipient.Character.Meso);
        }

        if (!await _packages.RemoveAsync(packageId, recipient.Character.Id, ct).ConfigureAwait(false))
        {
            return DueyReceiveResult.Fail(DueyResultStatus.PackageNotFound, packageId, recipient.Character.Meso);
        }

        Item? gained = null;
        if (package.Item is not null && itemType is not null)
        {
            var item = package.Item.ToItem();
            item.Slot = 0;
            gained = recipient.TryReceiveDueyItem(itemType.Value, item);
            if (gained is null)
            {
                return DueyReceiveResult.Fail(DueyResultStatus.InventoryFull, packageId, recipient.Character.Meso);
            }
        }

        if (package.Meso != 0)
        {
            recipient.GainMeso(package.Meso);
        }

        recipient.FlushInventory();
        await _characters.UpdateAsync(recipient.Character, ct).ConfigureAwait(false);

        return new DueyReceiveResult(
            DueyResultStatus.Success,
            packageId,
            recipient.Character.Meso,
            itemType,
            gained);
    }

    public async Task<DueyReturnResult> ReturnAsync(
        Player recipient,
        int packageId,
        CancellationToken ct = default)
    {
        var now = NowUnixMillis();
        var package = await _packages
            .FindForRecipientAsync(packageId, recipient.Character.Id, now, ct)
            .ConfigureAwait(false);

        if (package is null)
        {
            return DueyReturnResult.Fail(DueyResultStatus.PackageNotFound, packageId);
        }

        if (!await _packages.RemoveAsync(packageId, recipient.Character.Id, ct).ConfigureAwait(false))
        {
            return DueyReturnResult.Fail(DueyResultStatus.PackageNotFound, packageId);
        }

        var sender = await _characters.FindByNameAsync(package.SenderName, ct).ConfigureAwait(false);
        if (sender is null || sender.Id == recipient.Character.Id)
        {
            return new DueyReturnResult(DueyResultStatus.Success, packageId);
        }

        var returned = new DueyPackage
        {
            SenderName = recipient.Character.Name,
            RecipientCharacterId = sender.Id,
            Meso = package.Meso,
            Item = package.Item,
            Message = package.Message,
            CreatedAtUnixMillis = now,
            ExpiresAtUnixMillis = now + (long)TimeSpan.FromDays(PackageLifetimeDays).TotalMilliseconds,
            Checked = true,
        };

        await _packages.AddAsync(returned, ct).ConfigureAwait(false);
        return new DueyReturnResult(DueyResultStatus.Success, packageId, returned);
    }

    public static int CalculateFinalCost(int meso, bool quickDelivery)
        => meso + GetTaxAmount(meso) + (quickDelivery ? 0 : RegularDeliveryFee);

    public static int GetTaxAmount(int meso)
    {
        if (meso >= 100_000_000) return JavaRound(0.06 * meso);
        if (meso >= 25_000_000) return JavaRound(0.05 * meso);
        if (meso >= 10_000_000) return JavaRound(0.04 * meso);
        if (meso >= 5_000_000) return JavaRound(0.03 * meso);
        if (meso >= 1_000_000) return JavaRound(0.018 * meso);
        if (meso >= 100_000) return JavaRound(0.008 * meso);
        return 0;
    }

    private long NowUnixMillis() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private static int JavaRound(double value) => (int)Math.Floor(value + 0.5d);

    private static bool TrySnapshotSendItem(
        Player sender,
        InventoryType type,
        short slot,
        short quantity,
        out ItemRecord? record)
    {
        record = null;
        if (slot <= 0 || quantity <= 0)
        {
            return false;
        }

        var item = sender.Inventory.By(type).Get(slot);
        if (item is null)
        {
            return false;
        }

        var available = item.IsEquip ? (short)1 : item.Quantity;
        if (quantity > available)
        {
            return false;
        }

        var copy = item.Copy();
        copy.Quantity = item.IsEquip ? (short)1 : quantity;
        copy.Slot = 0;
        record = ItemRecord.From(type, copy);
        return true;
    }

    private static bool TakeItemForPackage(
        Player sender,
        InventoryType type,
        short slot,
        short quantity,
        ICollection<DueyInventoryMutation> mutations)
    {
        if (!sender.TryTakeDueyItem(type, slot, quantity, out _, out var mutation))
        {
            return false;
        }

        if (mutation is not null)
        {
            mutations.Add(mutation);
        }

        return true;
    }
}
