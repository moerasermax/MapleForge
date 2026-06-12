using Maple.Application.Duey;
using Maple.Core.IO;
using Maple.Core.World;
using Maple.Net;

namespace Maple.Adapters.V113.Channel;

public sealed class V113DueyHandler
{
    private readonly DueyService _duey;

    public V113DueyHandler(DueyService duey)
    {
        _duey = duey;
    }

    public async Task OpenAsync(Player player, MapleSession session, CancellationToken ct)
    {
        _ = player;
        await session.SendAsync(V113DueyPackets.OpenSecondPassword(), ct);
    }

    public async Task SendInboxAsync(Player player, MapleSession session, CancellationToken ct)
    {
        var inbox = await _duey.GetInboxAsync(player, ct).ConfigureAwait(false);
        await session.SendAsync(V113DueyPackets.Inbox(inbox), ct);
    }

    public async Task HandleActionAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        V113DueyAction action;
        try
        {
            action = V113DueyPackets.ParseAction(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113DueyPackets.Status(V113DueyPackets.StatusUnsuccessful), ct);
            return;
        }

        switch (action.Operation)
        {
            case V113DueyClientOperation.SecondPassword:
                // MVP: second password is accepted here; account-level validation can be added by the central session context.
                await SendInboxAsync(player, session, ct);
                break;

            case V113DueyClientOperation.SendPackage:
                await HandleSendPackageAsync(action, player, session, ct);
                break;

            case V113DueyClientOperation.ReceivePackage:
                await HandleReceivePackageAsync(action.PackageId, player, session, ct);
                break;

            case V113DueyClientOperation.ReturnPackage:
                await HandleReturnPackageAsync(action.PackageId, player, session, ct);
                break;

            case V113DueyClientOperation.Close:
                break;
        }
    }

    private async Task HandleSendPackageAsync(
        V113DueyAction action,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        if (action.InvalidInventoryType || action.SendRequest is null)
        {
            await session.SendAsync(V113DueyPackets.Status(V113DueyPackets.StatusUnsuccessful), ct);
            return;
        }

        var result = await _duey.SendAsync(player, action.SendRequest, ct).ConfigureAwait(false);
        if (result.Status == DueyResultStatus.Success)
        {
            foreach (var mutation in result.InventoryMutations)
            {
                await session.SendAsync(V113DueyPackets.ModifyInventoryQuantity(mutation), ct);
            }

            await session.SendAsync(V113DueyPackets.UpdateMeso(result.Meso), ct);
        }

        await session.SendAsync(V113DueyPackets.Status(V113DueyPackets.StatusFor(result.Status)), ct);
    }

    private async Task HandleReceivePackageAsync(
        int packageId,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        var result = await _duey.ReceiveAsync(player, packageId, ct).ConfigureAwait(false);
        if (result.Status != DueyResultStatus.Success)
        {
            await session.SendAsync(V113DueyPackets.Status(V113DueyPackets.StatusFor(result.Status)), ct);
            return;
        }

        if (result.GainedItemType is { } type && result.GainedItem is { } item)
        {
            await session.SendAsync(V113DueyPackets.ModifyInventoryAdd(type, item), ct);
        }

        await session.SendAsync(V113DueyPackets.UpdateMeso(result.Meso), ct);
        await session.SendAsync(V113DueyPackets.RemovePackage(returnedOrDeleted: false, packageId), ct);
    }

    private async Task HandleReturnPackageAsync(
        int packageId,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        var result = await _duey.ReturnAsync(player, packageId, ct).ConfigureAwait(false);
        if (result.Status != DueyResultStatus.Success)
        {
            await session.SendAsync(V113DueyPackets.Status(V113DueyPackets.StatusFor(result.Status)), ct);
            return;
        }

        await session.SendAsync(V113DueyPackets.RemovePackage(returnedOrDeleted: true, packageId), ct);
    }
}
