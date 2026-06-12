using Maple.Application.Reactors;
using Maple.Core.IO;
using Maple.Core.World;
using Maple.Net;

namespace Maple.Adapters.V113.Channel;

internal static class V113ReactorHandler
{
    public static async Task SendFieldReactorsAsync(FieldInstance field, MapleSession session, CancellationToken ct)
    {
        List<Reactor> reactors;
        lock (field)
        {
            reactors = field.Objects.OfType<Reactor>().Where(static r => r.IsAlive).ToList();
        }

        foreach (var reactor in reactors)
        {
            await session.SendAsync(V113ReactorPackets.SpawnReactor(reactor), ct);
        }
    }

    public static async Task<ReactorInteractionResult?> HandleDamageReactorAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        ReactorService reactors,
        Func<byte[], CancellationToken, Task> broadcastToMap,
        CancellationToken ct)
    {
        V113DamageReactorRequest request;
        try
        {
            request = V113ReactorPackets.ParseDamageReactor(reader);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        ReactorInteractionResult result;
        lock (field)
        {
            result = reactors.HitReactor(field, player, request.ObjectId, request.CharacterPosition, request.Stance);
        }

        if (result.Hit is not null && V113ReactorPackets.EncodeHitResult(result.Hit) is { } packet)
        {
            await broadcastToMap(packet, ct);
        }

        return result;
    }

    public static ReactorInteractionResult? HandleTouchReactor(
        PacketReader reader,
        Player player,
        FieldInstance field,
        ReactorService reactors)
    {
        V113TouchReactorRequest request;
        try
        {
            request = V113ReactorPackets.ParseTouchReactor(reader);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        lock (field)
        {
            return reactors.TouchReactor(field, player, request.ObjectId, request.Touched);
        }
    }
}
