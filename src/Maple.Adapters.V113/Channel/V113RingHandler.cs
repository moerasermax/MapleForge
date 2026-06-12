using Maple.Application.Maps;
using Maple.Application.Social;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed class V113RingHandler
{
    private readonly RingService _rings;

    public V113RingHandler(RingService rings)
    {
        _rings = rings;
    }

    public async Task HandleRingActionAsync(
        PacketReader reader,
        Player player,
        IMapSessionRegistry mapRegistry,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        V113RingRequest request;
        try
        {
            request = V113RingPackets.ParseRingAction(reader);
        }
        catch (InvalidDataException)
        {
            await sendSelf(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (request.IsProposal)
        {
            await HandleProposalAsync(request, player, sendSelf, ct);
            return;
        }

        if (request.IsCancel)
        {
            player.ClearMarriageProposal();
            await sendSelf(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (request.IsReply)
        {
            await HandleReplyAsync(request, player, mapRegistry, sendSelf, ct);
            return;
        }

        // Java mode 3 drops ETC invitation cards. Wedding/NPC flow is outside this port.
        await sendSelf(V113StatsPackets.EnableActions(), ct);
    }

    public async Task BroadcastRingEffectAsync(
        Player player,
        IMapSessionRegistry mapRegistry,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        await BroadcastToMapAsync(
            player,
            mapRegistry,
            sendSelf,
            V113RingPackets.ShowRingEffectCandidate(player.Character.Id),
            ct);
    }

    private async Task HandleProposalAsync(
        V113RingRequest request,
        Player player,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var result = _rings.RequestProposal(player, request.TargetName, request.ItemId);
        if (result.Status == RingActionStatus.Success && result.Target is not null)
        {
            await result.Target.SendPacket(
                V113RingPackets.MarriageRequest(player.Character.Name, player.Character.Id),
                ct);
            return;
        }

        await SendRingFailureAsync(result.Status, sendSelf, ct);
    }

    private async Task HandleReplyAsync(
        V113RingRequest request,
        Player player,
        IMapSessionRegistry mapRegistry,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var result = _rings.ReplyToProposal(player, request.Accepted, request.TargetName, request.CharacterId);
        if (result.Status == RingActionStatus.Success && result.Proposer is not null)
        {
            await result.Proposer.SendPacket(
                V113RingPackets.MarriageResult(
                    V113RingPackets.EngagementSuccess,
                    result.RingItemId,
                    result.Proposer.Name,
                    player.Character.Name,
                    result.Proposer.CharacterId,
                    player.Character.Id),
                ct);

            await sendSelf(V113StatsPackets.EnableActions(), ct);
            await BroadcastRingEffectAsync(player, mapRegistry, sendSelf, ct);
            if (result.ProposerPlayer is not null)
            {
                await BroadcastRingEffectAsync(result.ProposerPlayer, mapRegistry, result.Proposer.SendPacket, ct);
            }
            return;
        }

        if (result.Status == RingActionStatus.Declined && result.Proposer is not null)
        {
            await result.Proposer.SendPacket(V113RingPackets.MarriageResult(V113RingPackets.EngagementDeclined), ct);
            await sendSelf(V113StatsPackets.EnableActions(), ct);
            return;
        }

        await SendRingFailureAsync(result.Status, sendSelf, ct);
    }

    private static async Task SendRingFailureAsync(
        RingActionStatus status,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        var packet = V113RingPackets.MarriageResult(status);
        if (packet.Length > 0)
        {
            await sendSelf(packet, ct);
        }

        await sendSelf(V113StatsPackets.EnableActions(), ct);
    }

    private static async Task BroadcastToMapAsync(
        Player player,
        IMapSessionRegistry mapRegistry,
        Func<byte[], CancellationToken, Task> sendSelf,
        byte[] packet,
        CancellationToken ct)
    {
        await sendSelf(packet, ct);
        foreach (var other in mapRegistry.GetOthers(player.Character.MapId, player.Character.Id))
        {
            try
            {
                await other.SendPacket(packet, ct);
            }
            catch
            {
                // Best-effort map fanout; stale sessions are cleaned by central registries.
            }
        }
    }
}
