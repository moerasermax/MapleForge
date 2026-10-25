using Maple.Core.Characters;
using Maple.Core.Skills;

namespace Maple.Core.World;

public sealed partial class Player
{
    public short Hp => Character.Stats.Hp;

    /// <summary>
    /// P115：目前最大 HP（對照 Java <c>PlayerStats.getCurrentMaxHp</c>＝<c>localmaxhp</c>）：基礎值＋裝備 HP，
    /// 再乘 MAXHP buff 百分比，最後夾在 ±30000 取絕對值。基礎值（AP/升級、角色資訊封包）仍是 <c>Character.Stats.MaxHp</c>。
    /// 尚未納入：裝備 HpR、套裝、潛能（C# 尚無資料欄位）。
    /// </summary>
    public short MaxHp => ComputeCurrentMax(Character.Stats.MaxHp, static e => e.Hp, MapleBuffStat.MAXHP);

    public short Mp => Character.Stats.Mp;

    /// <summary>P115：目前最大 MP（Java <c>getCurrentMaxMp</c>），規則同 <see cref="MaxHp"/>。</summary>
    public short MaxMp => ComputeCurrentMax(Character.Stats.MaxMp, static e => e.Mp, MapleBuffStat.MAXMP);

    /// <summary>
    /// P116：對照 Java <c>MapleCharacter.enforceMaxHpMp</c>（<c>recalcLocalStats</c> 尾段）：HP/MP 超過目前上限時壓回，
    /// 回傳要送出的屬性更新（MP 先、HP 後，同 Java）；沒有變動回空清單。
    /// </summary>
    public IReadOnlyList<PlayerStatUpdate> EnforceMaxHpMp()
    {
        var updates = new List<PlayerStatUpdate>(2);
        var maxMp = MaxMp;
        if (Character.Stats.Mp > maxMp)
        {
            Character.Stats.Mp = maxMp;
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Mp, maxMp));
        }

        var maxHp = MaxHp;
        if (Character.Stats.Hp > maxHp)
        {
            Character.Stats.Hp = maxHp;
            updates.Add(new PlayerStatUpdate(PlayerStatKind.Hp, maxHp));
        }

        return updates;
    }

    private short ComputeCurrentMax(short baseValue, Func<EquipEntry, short> equipValue, MapleBuffStat buffStat)
    {
        // Java recalcLocalStats：int localmaxhp_ = getMaxHp() + Σ equip.getHp()；
        // localmaxhp_ += (percent_hp * localmaxhp_) / 100f（裝備 HpR，C# 尚無）；
        // localmaxhp_ += (buff / 100.0) * localmaxhp_（複合指定 → 截斷成 int）。
        var total = (int)baseValue;
        foreach (var equip in Character.Equips)
        {
            total += equipValue(equip);
        }

        if (GetBuffValue(buffStat) is { } buff)
        {
            total = (int)(total + buff / 100.0 * total);
        }

        return (short)Math.Min(30000, Math.Abs(Math.Max(-30000, total)));
    }

    public bool IsAlive => Hp > 0;

    /// <summary>玩家受傷；扣血量夾在 [0, Hp]，回傳實際扣血。</summary>
    public short TakeDamage(int damage)
    {
        if (damage <= 0 || !IsAlive)
        {
            return 0;
        }

        var applied = (short)Math.Min(damage, Character.Stats.Hp);
        Character.Stats.Hp = (short)(Character.Stats.Hp - applied);
        return applied;
    }

    public void HealHp(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Character.Stats.Hp = (short)Math.Min(MaxHp, Character.Stats.Hp + amount);
    }

    public void UseMp(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Character.Stats.Mp = (short)Math.Max(0, Character.Stats.Mp - amount);
    }
}
