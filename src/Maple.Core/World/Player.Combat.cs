namespace Maple.Core.World;

public sealed partial class Player
{
    public short Hp => Character.Stats.Hp;

    public short MaxHp => Character.Stats.MaxHp;

    public short Mp => Character.Stats.Mp;

    public short MaxMp => Character.Stats.MaxMp;

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

        Character.Stats.Hp = (short)Math.Min(Character.Stats.MaxHp, Character.Stats.Hp + amount);
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
