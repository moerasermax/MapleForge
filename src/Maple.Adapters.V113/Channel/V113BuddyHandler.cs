using Maple.Application.Buddies;
using Maple.Core.IO;
using Maple.Core.World;
using Maple.Net;

namespace Maple.Adapters.V113.Channel;

public sealed class V113BuddyHandler
{
    private readonly BuddyService _buddies;

    public V113BuddyHandler(BuddyService buddies)
    {
        _buddies = buddies;
    }

    public async Task HandleModifyAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        int channel,
        CancellationToken ct)
    {
        BuddyModifyRequest request;
        try
        {
            request = V113BuddyPackets.ParseModify(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = await _buddies.ModifyAsync(player.Character, request, channel, ct);
        await SendResultAsync(result, session, ct);
    }

    public async Task OnPlayerLoggedInAsync(
        Player player,
        MapleSession session,
        int channel,
        CancellationToken ct)
    {
        var result = _buddies.LogOn(
            player.Character,
            channel,
            (packet, token) => session.SendAsync(packet, token));

        await SendResultAsync(result, session, ct);
    }

    public async Task OnPlayerLoggedOutAsync(Player player, CancellationToken ct = default)
    {
        var result = _buddies.LogOff(player.Character);
        await SendRemoteAsync(result, ct);
    }

    private static async Task SendResultAsync(BuddyServiceResult result, MapleSession session, CancellationToken ct)
    {
        if (result.Self.Message is { } message)
        {
            await session.SendAsync(V113BuddyPackets.Message(message), ct);
        }

        if (result.Self.BuddyList is not null)
        {
            await session.SendAsync(V113BuddyPackets.UpdateBuddyList(result.Self.BuddyList), ct);
        }

        if (result.Self.PendingRequest is { } pending)
        {
            await session.SendAsync(V113BuddyPackets.RequestBuddyListAdd(pending.CharacterId, pending.Name), ct);
        }

        await SendRemoteAsync(result, ct);
    }

    private static async Task SendRemoteAsync(BuddyServiceResult result, CancellationToken ct)
    {
        foreach (var request in result.RemoteRequests)
        {
            try
            {
                await request.Target.SendPacket(
                    V113BuddyPackets.RequestBuddyListAdd(request.CharacterIdFrom, request.NameFrom),
                    ct);
            }
            catch
            {
                // Session may have closed while another player was modifying buddy state.
            }
        }

        foreach (var update in result.RemoteChannelUpdates)
        {
            try
            {
                await update.Target.SendPacket(
                    V113BuddyPackets.UpdateBuddyChannel(update.CharacterId, update.ChannelForClient),
                    ct);
            }
            catch
            {
                // Session may have closed while another player was modifying buddy state.
            }
        }
    }
}
