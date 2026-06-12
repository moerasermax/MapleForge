using Maple.Application.Maps;
using Maple.Application.Social;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

public sealed class V113FollowHandler
{
    private readonly FollowService _follows;

    public V113FollowHandler(FollowService follows)
    {
        _follows = follows;
    }

    public async Task HandleFollowRequestAsync(
        PacketReader reader,
        Player player,
        IMapSessionRegistry mapRegistry,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        V113FollowRequest request;
        try
        {
            request = V113FollowPackets.ParseFollowRequest(reader);
        }
        catch (InvalidDataException)
        {
            await sendSelf(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var result = _follows.RequestFollow(
            player,
            request.TargetCharacterId,
            request.IsMapChangeResume,
            request.IsCancel);

        if (result.Status == FollowActionStatus.Success && result.Target is not null && !request.IsMapChangeResume)
        {
            await result.Target.SendPacket(V113FollowPackets.FollowRequest(player.Character.Id), ct);
            return;
        }

        if (result.Status == FollowActionStatus.Canceled)
        {
            await BroadcastToMapAsync(
                player,
                mapRegistry,
                sendSelf,
                V113FollowPackets.FollowEffect(player.Character.Id, replierCharacterId: 0),
                ct);
            return;
        }

        if (result.Status != FollowActionStatus.Success)
        {
            await sendSelf(V113FollowPackets.FollowMessage("Follow request failed."), ct);
            await sendSelf(V113StatsPackets.EnableActions(), ct);
        }
    }

    public async Task HandleFollowReplyAsync(
        PacketReader reader,
        Player player,
        IMapSessionRegistry mapRegistry,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        V113FollowReply reply;
        try
        {
            reply = V113FollowPackets.ParseFollowReply(reader);
        }
        catch (InvalidDataException)
        {
            await sendSelf(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var result = _follows.ReplyToFollow(player, reply.RequesterCharacterId, reply.Accepted);
        if (result.Status == FollowActionStatus.Success && result.Requester is not null)
        {
            await BroadcastToMapAsync(
                player,
                mapRegistry,
                sendSelf,
                V113FollowPackets.FollowEffect(result.Requester.CharacterId, player.Character.Id),
                ct);
            return;
        }

        if (result.Status == FollowActionStatus.Declined && result.Requester is not null)
        {
            await result.Requester.SendPacket(V113FollowPackets.FollowMsg(5), ct);
            return;
        }

        await sendSelf(V113FollowPackets.FollowMessage("Follow request failed."), ct);
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
