using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Combat;

public sealed record CombatRangedAttack(
    CombatAttack Attack,
    int SkillId,
    short ProjectileSlot,
    short CashProjectileSlot,
    byte AreaOfEffect);

public sealed record RangedAttackConsumableOptions(
    int BulletCount = 1,
    int BulletConsumeOverride = 0,
    bool HasShadowPartner = false,
    bool HasSoulArrow = false,
    bool HasSpiritClaw = false)
{
    public int ProjectileConsumeCount
    {
        get
        {
            var baseCount = BulletConsumeOverride > 0 ? BulletConsumeOverride : BulletCount;
            baseCount = Math.Max(1, baseCount);
            return HasShadowPartner ? baseCount * 2 : baseCount;
        }
    }
}

public enum RangedAttackApplyStatus
{
    Applied,
    AttackerDead,
    ProjectileMissing,
    ProjectileQuantityMissing,
    CapsuleMissing,
}

public sealed record RangedAttackApplyResult(
    RangedAttackApplyStatus Status,
    CombatAttackResult Combat,
    int ProjectileItemId,
    int VisualProjectileItemId,
    IReadOnlyList<InventoryQuantityMutation> InventoryMutations)
{
    public bool Applied => Status == RangedAttackApplyStatus.Applied;
}

/// <summary>Ranged/magic attack use case; ranged consumes projectiles before reusing CombatService damage.</summary>
public sealed class RangedMagicCombatService
{
    private const int ShadowMeso = 4111004;
    private const int Flamethrower = 5211004;
    private const int IceSplitter = 5211005;
    private const int BlazeCapsule = 2331000;
    private const int GlazeCapsule = 2332000;

    private static readonly CombatAttackResult EmptyCombat = new(Array.Empty<CombatMobHit>());
    private static readonly IReadOnlyList<InventoryQuantityMutation> NoMutations = Array.Empty<InventoryQuantityMutation>();

    private readonly CombatService _combat;

    public RangedMagicCombatService(CombatService combat)
    {
        _combat = combat;
    }

    public RangedAttackApplyResult ApplyRangedAttack(
        FieldInstance field,
        Player attacker,
        CombatRangedAttack ranged,
        RangedAttackConsumableOptions? consumables = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(ranged);

        if (!attacker.IsAlive)
        {
            return new RangedAttackApplyResult(
                RangedAttackApplyStatus.AttackerDead,
                EmptyCombat,
                0,
                0,
                NoMutations);
        }

        consumables ??= new RangedAttackConsumableOptions();

        var projectileItemId = 0;
        var visualProjectileItemId = 0;
        var mutations = new List<InventoryQuantityMutation>();

        if (RequiresProjectile(ranged, consumables))
        {
            if (!attacker.TryResolveRangedProjectile(ranged.ProjectileSlot, ranged.CashProjectileSlot, out var projectile))
            {
                return new RangedAttackApplyResult(
                    RangedAttackApplyStatus.ProjectileMissing,
                    EmptyCombat,
                    0,
                    0,
                    NoMutations);
            }

            projectileItemId = projectile.ItemId;
            visualProjectileItemId = projectile.VisualItemId;

            if (!consumables.HasSpiritClaw)
            {
                if (!attacker.TryConsumeUseItemById(projectileItemId, consumables.ProjectileConsumeCount, out var projectileMutations))
                {
                    return new RangedAttackApplyResult(
                        RangedAttackApplyStatus.ProjectileQuantityMissing,
                        EmptyCombat,
                        projectileItemId,
                        visualProjectileItemId,
                        NoMutations);
                }

                mutations.AddRange(projectileMutations);
            }
        }

        if (RequiredCapsuleItemId(ranged.SkillId) is { } capsuleItemId)
        {
            if (!attacker.TryConsumeUseItemById(capsuleItemId, 1, out var capsuleMutations))
            {
                return new RangedAttackApplyResult(
                    RangedAttackApplyStatus.CapsuleMissing,
                    EmptyCombat,
                    projectileItemId,
                    visualProjectileItemId,
                    mutations);
            }

            mutations.AddRange(capsuleMutations);
        }

        var combat = _combat.ApplyAttack(field, attacker, ranged.Attack);
        return new RangedAttackApplyResult(
            RangedAttackApplyStatus.Applied,
            combat,
            projectileItemId,
            visualProjectileItemId,
            mutations);
    }

    public CombatAttackResult ApplyMagicAttack(FieldInstance field, Player attacker, CombatAttack attack)
        => _combat.ApplyAttack(field, attacker, attack);

    private static bool RequiresProjectile(CombatRangedAttack attack, RangedAttackConsumableOptions consumables)
        => attack.AreaOfEffect != 0 && !consumables.HasSoulArrow && attack.SkillId != ShadowMeso;

    private static int? RequiredCapsuleItemId(int skillId)
        => skillId switch
        {
            Flamethrower => BlazeCapsule,
            IceSplitter => GlazeCapsule,
            _ => null,
        };
}
