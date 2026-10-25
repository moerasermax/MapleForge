using Maple.Core.Inventory;

namespace Maple.Core.Items;

/// <summary>
/// 裝備數值隨機浮動（對照 Java <c>MapleItemInformationProvider.randomizeStats</c>／<c>getRandStat</c>）：
/// 每項非 0 數值在 ±ceil(10%) 內均勻浮動，主屬性／攻擊／命中迴避等單項上限 5，防禦與 HP/MP 上限 10。
/// 原地修改並回傳同一件裝備；值為 0 的項目維持 0。
/// </summary>
public static class EquipStatRandomizer
{
    private const int MinorMaxRange = 5;
    private const int MajorMaxRange = 10;

    public static Equip Randomize(Equip equip, Random random)
    {
        ArgumentNullException.ThrowIfNull(equip);
        ArgumentNullException.ThrowIfNull(random);

        equip.Str = RandStat(equip.Str, MinorMaxRange, random);
        equip.Dex = RandStat(equip.Dex, MinorMaxRange, random);
        equip.Int = RandStat(equip.Int, MinorMaxRange, random);
        equip.Luk = RandStat(equip.Luk, MinorMaxRange, random);
        equip.Matk = RandStat(equip.Matk, MinorMaxRange, random);
        equip.Watk = RandStat(equip.Watk, MinorMaxRange, random);
        equip.Acc = RandStat(equip.Acc, MinorMaxRange, random);
        equip.Avoid = RandStat(equip.Avoid, MinorMaxRange, random);
        equip.Jump = RandStat(equip.Jump, MinorMaxRange, random);
        equip.Hands = RandStat(equip.Hands, MinorMaxRange, random);
        equip.Speed = RandStat(equip.Speed, MinorMaxRange, random);
        equip.Wdef = RandStat(equip.Wdef, MajorMaxRange, random);
        equip.Mdef = RandStat(equip.Mdef, MajorMaxRange, random);
        equip.Hp = RandStat(equip.Hp, MajorMaxRange, random);
        equip.Mp = RandStat(equip.Mp, MajorMaxRange, random);
        return equip;
    }

    /// <summary>逐字對照 Java <c>getRandStat</c>（含負值時的 double 運算順序）。</summary>
    internal static short RandStat(short defaultValue, int maxRange, Random random)
    {
        if (defaultValue == 0)
        {
            return 0;
        }

        var range = (int)Math.Min(Math.Ceiling(defaultValue * 0.1), maxRange);
        return (short)((defaultValue - range) + Math.Floor(random.NextDouble() * (range * 2 + 1)));
    }
}
