using Maple.Application.Combat;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113MonsterBombRequest(int MobObjectId);

internal sealed record V113MonsterBombResult(
    V113MonsterBombRequest Request,
    bool Killed,
    IReadOnlyList<byte[]> SelfPackets,
    IReadOnlyList<byte[]> MapPackets);

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

            var killed = combat.KillMobWithoutRewards(field, request.MobObjectId, (byte)mob.Stats.SelfDestructAnimation);
            if (!killed.Killed)
            {
                return EnableActionsOnly(request);
            }

            return new V113MonsterBombResult(
                request,
                Killed: true,
                SelfPackets: Array.Empty<byte[]>(),
                MapPackets: [V113CombatPackets.KillMonster(killed.ObjectId, killed.Animation)]);
        }
    }

    private static V113MonsterBombResult EnableActionsOnly(V113MonsterBombRequest request)
        => new(request, Killed: false, SelfPackets: [V113StatsPackets.EnableActions()], MapPackets: Array.Empty<byte[]>());
}
