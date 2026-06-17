using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Items;

/// <summary>
/// Applies item-use field mutations that belong outside the v113 packet adapter.
/// </summary>
public sealed class ItemUseService
{
    private readonly MapService _maps;

    public ItemUseService(MapService maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = maps;
    }

    public IReadOnlyList<Mob> SpawnSummonBagMonsters(FieldInstance field, Player player, IEnumerable<int> monsterIds)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(monsterIds);

        var spawned = new List<Mob>();
        var nextObjectId = NextObjectId(field);

        foreach (var monsterId in monsterIds)
        {
            var stats = _maps.LoadMobStats(monsterId);
            if (stats is null)
            {
                continue;
            }

            var definition = new MapMonster
            {
                MonsterId = monsterId,
                X = player.Position.X,
                Y = player.Position.Y,
                Cy = player.Position.Y,
                F = 0,
                Fh = player.Position.Foothold,
                Team = -1,
            };

            var mob = new Mob(definition, stats, nextObjectId++);
            field.Add(mob);
            spawned.Add(mob);
        }

        return spawned;
    }

    public bool RemoveCaughtMob(FieldInstance field, int objectId)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Get(objectId) is Mob && field.Remove(objectId);
    }

    private static int NextObjectId(FieldInstance field)
    {
        var maxObjectId = field.Objects
            .Select(static obj => obj.ObjectId)
            .DefaultIfEmpty(CombatService.DefaultMobObjectIdBase)
            .Max();

        return Math.Max(maxObjectId + 1, CombatService.DefaultMobObjectIdBase + 1);
    }
}
