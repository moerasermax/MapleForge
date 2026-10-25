namespace Maple.Core.Items;

/// <summary>P136：對照 Java <c>MapleWeaponType</c>（武器係數、初始熟練度）。</summary>
public enum WeaponType
{
    None,
    OneHandedSword,
    OneHandedAxe,
    OneHandedBlunt,
    Dagger,
    Wand,
    Staff,
    TwoHandedSword,
    TwoHandedAxe,
    TwoHandedBlunt,
    Spear,
    Polearm,
    Bow,
    Crossbow,
    Claw,
    Knuckle,
    Gun,
}

public static class WeaponTypes
{
    /// <summary>
    /// Java <c>GameConstants.getWeaponType</c>：<c>(itemId / 10000) % 100</c>。注意 Java 把 43 對到「槍」(Spear)、
    /// 44 對到「矛」(Polearm)——<c>calculateMaxBaseDamage</c> 的「矛」分支用 STR/DEX ×1.25，照抄。
    /// </summary>
    public static WeaponType FromItemId(int itemId) => (itemId / 10000 % 100) switch
    {
        30 => WeaponType.OneHandedSword,
        31 => WeaponType.OneHandedAxe,
        32 => WeaponType.OneHandedBlunt,
        33 => WeaponType.Dagger,
        37 => WeaponType.Wand,
        38 => WeaponType.Staff,
        40 => WeaponType.TwoHandedSword,
        41 => WeaponType.TwoHandedAxe,
        42 => WeaponType.TwoHandedBlunt,
        43 => WeaponType.Spear,
        44 => WeaponType.Polearm,
        45 => WeaponType.Bow,
        46 => WeaponType.Crossbow,
        47 => WeaponType.Claw,
        48 => WeaponType.Knuckle,
        49 => WeaponType.Gun,
        _ => WeaponType.None,
    };

    /// <summary>Java <c>getMaxDamageMultiplier</c>（float）。</summary>
    public static float MaxDamageMultiplier(this WeaponType type) => type switch
    {
        WeaponType.OneHandedSword or WeaponType.OneHandedAxe or WeaponType.OneHandedBlunt => 1.2f,
        WeaponType.Dagger => 1.3f,
        WeaponType.Wand or WeaponType.Staff => 1.0f,
        WeaponType.TwoHandedSword or WeaponType.TwoHandedAxe or WeaponType.TwoHandedBlunt => 1.34f,
        WeaponType.Spear or WeaponType.Polearm => 1.49f,
        WeaponType.Bow => 1.3f,
        WeaponType.Crossbow => 1.35f,
        WeaponType.Claw => 1.75f,
        WeaponType.Knuckle => 1.7f,
        WeaponType.Gun => 1.5f,
        _ => 0.0f,
    };

    /// <summary>Java <c>getBaseMastery</c>。</summary>
    public static int BaseMastery(this WeaponType type) => type switch
    {
        WeaponType.None => 0,
        WeaponType.Wand or WeaponType.Staff => 25,
        WeaponType.Bow or WeaponType.Crossbow or WeaponType.Claw or WeaponType.Gun => 15,
        _ => 20,
    };
}
