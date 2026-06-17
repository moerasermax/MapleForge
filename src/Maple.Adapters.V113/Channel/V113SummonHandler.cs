using Maple.Application.Maps;
using Maple.Core.IO;
using Maple.Core.World;
using Maple.Net;

namespace Maple.Adapters.V113.Channel;

/// <summary>v113 召喚獸 handler MVP。中央 dispatch 之後可直接呼叫這些 static methods。</summary>
internal static class V113SummonHandler
{
    public static async Task HandleMoveSummonAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        IMapSessionRegistry mapRegistry,
        CancellationToken ct)
    {
        V113MoveSummonRequest request;
        try
        {
            request = V113SummonPackets.ParseMoveSummon(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        byte[]? packet;
        lock (field)
        {
            if (field.Get(request.ObjectId) is not Summon summon ||
                summon.OwnerId != player.Character.Id ||
                summon.MovementType == SummonMovementType.Stationary)
            {
                return;
            }

            UpdateSummonPosition(summon, request);
            packet = V113SummonPackets.MoveSummon(player.Character.Id, request);
        }

        await BroadcastToOthersAsync(player, mapRegistry, packet, ct);
    }

    public static async Task HandleSummonAttackAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        IMapSessionRegistry mapRegistry,
        CancellationToken ct)
    {
        V113SummonAttackRequest request;
        try
        {
            request = V113SummonPackets.ParseSummonAttack(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        byte[]? packet;
        lock (field)
        {
            if (field.Get(request.SummonObjectId) is not Summon summon ||
                summon.OwnerId != player.Character.Id ||
                summon.SkillLevel == 0)
            {
                return;
            }

            packet = V113SummonPackets.SummonAttack(
                player.Character.Id,
                summon.ObjectId,
                request.Animation,
                request.Targets,
                player.Character.Level);
        }

        await BroadcastToOthersAsync(player, mapRegistry, packet, ct);
    }

    public static async Task HandleDamageSummonAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        IMapSessionRegistry mapRegistry,
        CancellationToken ct)
    {
        V113DamageSummonRequest request;
        try
        {
            request = V113SummonPackets.ParseDamageSummon(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        Summon? summon;
        byte[]? damagePacket;
        byte[]? removePacket = null;
        byte[]? cancelBuffPacket = null;
        lock (field)
        {
            summon = field.Objects
                .OfType<Summon>()
                .FirstOrDefault(s => s.OwnerId == player.Character.Id && s.IsPuppet);
            if (summon is null)
            {
                return;
            }

            summon.TakeDamage(request.Damage);
            damagePacket = V113SummonPackets.DamageSummon(
                player.Character.Id,
                summon.SkillId,
                request.Damage,
                request.Unknown,
                request.MonsterIdFrom);

            if (summon.Hp <= 0)
            {
                field.Remove(summon.ObjectId);
                removePacket = V113SummonPackets.RemoveSummon(summon, animated: true);
                var cancellations = player.CancelBuffBySource(summon.SkillId);
                var stats = cancellations.SelectMany(static c => c.Stats).Distinct().ToArray();
                if (stats.Length > 0)
                {
                    cancelBuffPacket = V113SkillPackets.CancelBuff(stats);
                }
            }
        }

        await BroadcastToOthersAsync(player, mapRegistry, damagePacket, ct);
        if (removePacket is not null)
        {
            await BroadcastToMapAsync(player, session, mapRegistry, removePacket, ct);
        }

        if (cancelBuffPacket is not null)
        {
            await session.SendAsync(cancelBuffPacket, ct);
        }
    }

    public static async Task HandleSubSummonAsync(PacketReader reader, MapleSession session, CancellationToken ct)
    {
        try
        {
            _ = V113SummonPackets.ParseSubSummon(reader);
        }
        catch (InvalidDataException)
        {
            // MVP still releases the client action lock.
        }

        await session.SendAsync(V113StatsPackets.EnableActions(), ct);
    }

    private static void UpdateSummonPosition(Summon summon, V113MoveSummonRequest request)
    {
        var next = new Position(
            request.StartX,
            request.StartY,
            summon.Position.Stance,
            summon.Position.Foothold);

        try
        {
            if (request.RawMovement.Length > 0)
            {
                var result = V113MovementParser.Parse(new PacketReader(request.RawMovement));
                if (result.Commands > 0)
                {
                    next = new Position(result.X, result.Y, result.Stance, result.Foothold);
                }
            }
        }
        catch (InvalidDataException)
        {
            // Keep start position fallback; raw movement is still relayed verbatim.
        }

        summon.MoveTo(next);
    }

    private static async Task BroadcastToMapAsync(
        Player player,
        MapleSession session,
        IMapSessionRegistry mapRegistry,
        byte[] packet,
        CancellationToken ct)
    {
        await session.SendAsync(packet, ct);
        await BroadcastToOthersAsync(player, mapRegistry, packet, ct);
    }

    private static async Task BroadcastToOthersAsync(
        Player player,
        IMapSessionRegistry mapRegistry,
        byte[] packet,
        CancellationToken ct)
    {
        var others = mapRegistry.GetOthers(player.Character.MapId, player.Character.Id);
        foreach (var other in others)
        {
            try
            {
                await other.SendPacket(packet, ct);
            }
            catch
            {
                // Match existing channel broadcast helpers: dead sessions are ignored.
            }
        }
    }
}
