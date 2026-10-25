using Maple.Core.Items;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Combat;

/// <summary>
/// P138：攻擊射程檢查（對照 Java <c>GameConstants.getAttackRange</c>、<c>getAttackRangeBySkill</c>、
/// <c>getAttackRangeByWeapon</c>、<c>MapConstants.isBossMap</c>）。單位為距離平方（Java <c>Point.distanceSq</c>）。
/// </summary>
public static class AttackRange
{
    /// <summary>
    /// 有技能效果：<c>(450 + defRange)² + getMaxDistanceSq + range²</c>，技能表有指定值則改用表值；
    /// 普攻（無效果）：依武器（Java「槍」Spear 200000、「矛」Polearm 180000、拳套 250000、火槍/弩/弓 220000、其他 100000）。
    /// </summary>
    public static double MaxDistanceSq(Player player, MapleStatEffect? effect, int skillId, int defRange)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (effect is null)
        {
            var weapon = player.Character.Equips.FirstOrDefault(static e => e.Position == PlayerLocalStatsCalculator.WeaponSlot) is { } item
                ? WeaponTypes.FromItemId(item.ItemId)
                : WeaponType.None;
            return weapon switch
            {
                WeaponType.Spear => 200_000,
                WeaponType.Claw => 250_000,
                WeaponType.Gun or WeaponType.Crossbow or WeaponType.Bow => 220_000,
                WeaponType.Polearm => 180_000,
                _ => 100_000,
            };
        }

        var bySkill = BySkill(skillId);
        if (bySkill != 0)
        {
            return bySkill;
        }

        var reach = 450.0 + defRange;
        return reach * reach + effect.MaxDistanceSq + (double)effect.Range * effect.Range;
    }

    /// <summary>Java <c>getAttackRangeBySkill</c>。</summary>
    public static double BySkill(int skillId) => skillId switch
    {
        21120006 => 800_000,
        2121007 or 2221007 or 2321008 => 750_000,
        2221006 or 3101005 or 21101003 => 600_000,
        15111006 => 500_000,
        12111006 or 2111003 => 400_000,
        5221004 or 4001344 or 2101004 or 1121008 => 350_000,
        2211002 => 300_000,
        5110001 or 2311004 or 2211003 or 2001005 => 250_000,
        2321007 => 200_000,
        20001000 or 1000 => 180_000,
        _ => 0,
    };

    /// <summary>Java <c>MapConstants.isBossMap</c>：武陵道場（92502xxxx）與指定王圖不做射程檢查。</summary>
    public static bool IsBossMap(int mapId)
        => mapId / 10000 == 92502 || mapId is 105100300 or 220080001 or 230040420 or 240060000 or 240060100
            or 240060200 or 270050100 or 280030000 or 551030200 or 740000000 or 741020101 or 741020102
            or 749050301 or 802000211 or 802000611 or 922010900 or 925020200 or 930000600;

    public static double DistanceSq(Position a, Position b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
