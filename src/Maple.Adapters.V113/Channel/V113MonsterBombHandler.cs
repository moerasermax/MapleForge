using Maple.Application.Combat;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113MonsterBombRequest(int MobObjectId);

internal sealed record V113MonsterBombResult(
    V113MonsterBombRequest Request,
    bool Killed,
    IReadOnlyList<byte[]> SelfPackets,
    IReadOnlyList<byte[]> MapPackets)
{
    /// <summary>P186：自爆的怪物 ID（事件實例 unregisterMonster 用）。</summary>
    public int MonsterId { get; init; }
}

internal static class V113MonsterBombHandler
{
    public static V113MonsterBombRequest Parse(PacketReader reader)
        => new(reader.ReadInt());

    public static V113MonsterBombResult Handle(
        PacketReader reader,
        Player player,
        FieldInstance field,
        CombatService combat)
    {
        V113MonsterBombRequest request;
        try
        {
            request = Parse(reader);
        }
        catch (InvalidDataException)
        {
            return EnableActionsOnly(new V113MonsterBombRequest(0));
        }

        if (!player.IsAlive || player.Character.Job is not (421 or 422))
        {
            return EnableActionsOnly(request);
        }

        Mob? mob;
        lock (field)
        {
            mob = field.GetMob(request.MobObjectId);
            if (mob is null || mob.Stats.SelfDestructAnimation < 0)
            {
                return EnableActionsOnly(request);
            }

            var killed = combat.KillMobWithoutRewards(field, request.MobObjectId, (byte)mob.Stats.SelfDestructAnimation, player.Character.Id);
            if (!killed.Killed)
            {
                return EnableActionsOnly(request);
            }

            // P148：Java killMonster → killBy → spawnRevives（全圖 SPAWN_MONSTER link=死亡 oid；控制者收 CONTROL）。
            var revived = killed.Revived ?? [];
            return new V113MonsterBombResult(
                request,
                Killed: true,
                SelfPackets: [.. revived.Select(static r => V113CombatPackets.SpawnMonsterControl(r, newSpawn: true, aggro: false))],
                MapPackets:
                [
                    V113CombatPackets.KillMonster(killed.ObjectId, killed.Animation),
                    .. revived.Select(static r => V113CombatPackets.SpawnMonster(r, spawnType: -2, link: r.LinkObjectId)),
                ])
            {
                MonsterId = killed.MonsterId,
            };
        }
    }

    private static V113MonsterBombResult EnableActionsOnly(V113MonsterBombRequest request)
        => new(request, Killed: false, SelfPackets: [V113StatsPackets.EnableActions()], MapPackets: Array.Empty<byte[]>());
}
