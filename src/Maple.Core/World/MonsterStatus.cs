namespace Maple.Core.World;

/// <summary>
/// 怪物異常狀態（P110，對照 Java <c>client.status.MonsterStatus</c>；數值＝Java 建構子的索引 i，
/// 封包遮罩位置／排序屬協定細節，由 Adapters 決定）。
/// </summary>
public enum MonsterStatus
{
    Watk = 0,
    Wdef = 1,
    Matk = 2,
    Mdef = 3,
    Acc = 4,
    Avoid = 5,
    Speed = 6,
    Stun = 7,
    Freeze = 8,
    Poison = 9,
    Seal = 10,
    Darkness = 11,
    WeaponAttackUp = 12,
    WeaponDefenseUp = 13,
    MagicAttackUp = 14,
    MagicDefenseUp = 15,
    Doom = 16,
    ShadowWeb = 17,
    WeaponImmunity = 18,
    MagicImmunity = 19,
    Showdown = 20,
    DamageImmunity = 21,
    NinjaAmbush = 22,
    DamagedElemAttr = 23,
    VenomousWeapon = 24,
    Blind = 25,
    SealSkill = 26,
    Empty = 27,
    Hypnotize = 28,
    WeaponDamageReflect = 29,
    MagicDamageReflect = 30,
    Summon = 31,
    Mbs32 = 32,
    Neutralise = 33,
    Imprint = 34,
    MonsterBomb = 35,
    MagicCrash = 36,
}

/// <summary>怪物技能來源（Java <c>MobSkill</c> 的 id/level），玩家技能來源時為 null。</summary>
public sealed record MobSkillSource(int SkillId, int Level);

/// <summary>
/// 一個生效中的怪物異常狀態（對照 Java <c>MonsterStatusEffect</c>）。
/// <paramref name="Value"/> 為 Java <c>x</c>（封包寫出的數值）；<paramref name="ExpiresAt"/> 為 null 表示不會自然到期。
/// </summary>
public sealed record MonsterStatusEffect(
    MonsterStatus Status,
    int Value,
    int SkillId,
    MobSkillSource? MobSkill,
    DateTimeOffset? ExpiresAt,
    int PoisonDamage = 0,
    int FromCharacterId = 0)
{
    public bool ShouldCancel(DateTimeOffset now) => ExpiresAt is { } at && at <= now;
}

public enum MonsterStatusApplyResult
{
    Applied,
    MobDead,
    Immune,
    AlreadyActive,
}
