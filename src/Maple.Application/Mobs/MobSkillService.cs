using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Mobs;

/// <summary>怪物這次移動實際施放的技能（null＝沒有施放）與已套用的效果。</summary>
public sealed record MobSkillUse(
    MobSkillDefinition Skill,
    IReadOnlyList<MobBuffApplication>? Buffs = null,
    IReadOnlyList<MobHeal>? Heals = null,
    ActiveDisease? Disease = null,
    // P128：召喚出的怪物（已加入 field；呼叫端以技能 SpawnEffect 廣播 SPAWN_MONSTER）。
    IReadOnlyList<Mob>? Summoned = null,
    // P129：被驅散的控制者 buff（呼叫端送 CANCEL_BUFF 與 buff 連帶效果）。
    IReadOnlyList<PlayerBuffCancellation>? DispelledBuffs = null);

/// <summary>P125：怪物補血（Java healMonster 廣播的 oid 與補血量）。</summary>
public sealed record MobHeal(int ObjectId, int Amount);

/// <summary>P123：單隻怪物身上的自身 buff 變化（先解除的狀態、新套用的狀態）。</summary>
public sealed record MobBuffApplication(int ObjectId, IReadOnlyList<MonsterStatus> Cancelled, IReadOnlyList<MonsterStatusEffect> Applied);

/// <summary>
/// P122：怪物技能挑選（對照 Java <c>MobHandler.MoveMonster</c> 的 useSkill 區塊）：從怪物技能清單隨機抽一個 →
/// <c>checkCurrentBuff</c> 未生效 → 冷卻（<c>ls == 0 || (now - ls &gt; cool &amp;&amp; !summonOnce)</c>，先登記冷卻）→
/// HP 百分比 ≤ 技能 hp 門檻才施放 → <c>applyEffect</c>（P123：自身 buff／免疫／反射；其餘類別待後續 P）→ 扣 MP。
/// </summary>
public sealed class MobSkillService
{
    private readonly IMobSkillCatalog _catalog;
    private readonly Random _random;
    private readonly MapService? _maps;

    /// <param name="maps">P128：召喚（200）需要怪物模板與地圖踏腳石；未注入時不召喚。</param>
    public MobSkillService(IMobSkillCatalog catalog, Random? random = null, MapService? maps = null)
    {
        _catalog = catalog;
        _random = random ?? Random.Shared;
        _maps = maps;
    }

    /// <param name="controller">送出 MOVE_LIFE 的控制者（Java applyEffect 的 player）；異常狀態只會施加在他身上。</param>
    public MobSkillUse? TryUseSkill(FieldInstance field, Mob mob, DateTimeOffset now, Player? controller = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mob);

        var skills = mob.Stats.Skills;
        if (skills is null || skills.Count == 0)
        {
            return null;
        }

        var chosen = skills[_random.Next(skills.Count)];
        var skill = _catalog.Get(chosen.SkillId, chosen.Level);
        if (skill is null || IsBuffAlreadyActive(skill, mob, field))
        {
            return null;
        }

        var lastUsed = mob.GetLastSkillUsed(skill.SkillId);
        if (lastUsed is { } ls && !((now - ls).TotalMilliseconds > skill.CoolTimeMilliseconds && !skill.SummonOnce))
        {
            return null;
        }

        mob.SetLastSkillUsed(skill.SkillId, now, skill.CoolTimeMilliseconds);

        var hpPercent = (int)((float)mob.Hp / Math.Max(1, mob.Stats.MaxHp) * 100);
        if (hpPercent > skill.Hp)
        {
            return null;
        }

        var buffs = ApplyEffect(field, mob, skill, now);
        var heals = skill.SkillId == HealSkillId ? ApplyHeal(field, mob, skill) : null;
        var disease = ApplyDisease(mob, skill, controller, now);
        var summoned = skill.SkillId == SummonSkillId ? Summon(field, mob, skill, controller) : null;
        var dispelled = skill.SkillId == DispelSkillId && controller is not null && InRange(mob, skill, controller)
            ? controller.DispelSkillBuffs()
            : null;
        mob.ConsumeMp(skill.MpCon);
        return new MobSkillUse(skill, buffs, heals, disease, summoned, dispelled is { Count: > 0 } ? dispelled : null);
    }

    /// <summary>
    /// P123：對照 Java <c>MobSkill.applyEffect</c> 的 stats 分支（攻防命迴速封印提升、物/魔免疫、反射）：有 lt/rb 時
    /// 套用到範圍內所有怪物（含自己若在範圍內），否則只套自己；持續時間＝技能 time。
    /// </summary>
    private static IReadOnlyList<MobBuffApplication>? ApplyEffect(FieldInstance field, Mob mob, MobSkillDefinition skill, DateTimeOffset now)
    {
        var stats = GetBuffStats(skill.SkillId, skill.X);
        if (stats.Count == 0)
        {
            return null;
        }

        var targets = skill.Area is { } area
            ? field.Objects.OfType<Mob>().Where(m => m.IsAlive && Contains(BoundingBox(area, mob), m.Position)).ToArray()
            : [mob];
        var source = new MobSkillSource(skill.SkillId, skill.Level);
        var expiresAt = now.AddMilliseconds(skill.DurationMilliseconds);
        return targets
            .Select(target =>
            {
                var (cancelled, applied) = target.ApplyMonsterBuff(stats, source, expiresAt);
                return new MobBuffApplication(target.ObjectId, cancelled, applied);
            })
            .ToArray();
    }

    public const int HealSkillId = 114;
    public const int SummonSkillId = 200;
    public const int DispelSkillId = 127;

    /// <summary>Java <c>getPlayersInRange</c>（候選只有控制者）：無 lt/rb 時恆在範圍內。</summary>
    private static bool InRange(Mob mob, MobSkillDefinition skill, Player controller)
        => skill.Area is not { } area || Contains(BoundingBox(area, mob), controller.Position);

    /// <summary>
    /// P128：逐字對照 Java <c>MobSkill.applyEffect</c> case 200：依 summons 清單生怪（<c>getCustomSpawnID</c>：
    /// 9400589/9400748 改 9400706），拉圖炸彈 8500003/8500004、海怒斯炸彈 8510100 的座標特例與 220080001/230040420
    /// 地圖邊界夾回，8820007 跳過；落點為 <c>calcPointBelow(x, y - 1)</c>，找不到踏腳石就不生（Java 例外被吞）。
    /// 新怪物的控制者設為施放者的控制者（Java <c>updateMonsterController</c>）。
    /// </summary>
    private List<Mob>? Summon(FieldInstance field, Mob mob, MobSkillDefinition skill, Player? controller)
    {
        if (_maps is null)
        {
            return null;
        }

        var footholds = _maps.LoadMap(field.MapId).Footholds;
        var nextObjectId = Math.Max(
            CombatService.DefaultMobObjectIdBase,
            field.Objects.Select(static o => o.ObjectId).DefaultIfEmpty(0).Max()) + 1;
        var spawned = new List<Mob>();
        foreach (var summonId in skill.Summons)
        {
            if (summonId == 8820007)
            {
                continue;
            }

            var monsterId = mob.Definition.MonsterId is 9400589 or 9400748 ? 9400706 : summonId;
            var stats = _maps.LoadMobStats(monsterId);
            if (stats is null)
            {
                continue;
            }

            int x = mob.Position.X, y = mob.Position.Y;
            switch (summonId)
            {
                case 8500003:
                    y = -590;
                    break;
                case 8500004:
                    x = mob.Position.X + (int)Math.Ceiling(_random.NextDouble() * 1000.0) - 500;
                    break;
                case 8510100:
                    if (Math.Ceiling(_random.NextDouble() * 5) == 1)
                    {
                        y = 78;
                        x = (int)Math.Ceiling(_random.NextDouble() * 5) + (Math.Ceiling(_random.NextDouble() * 2) == 1 ? 180 : 0);
                    }
                    else
                    {
                        x = mob.Position.X + (int)Math.Ceiling(_random.NextDouble() * 1000.0) - 500;
                    }

                    break;
            }

            x = field.MapId switch
            {
                220080001 when x < -890 => (int)(-890 + Math.Ceiling(_random.NextDouble() * 150)),
                220080001 when x > 230 => (int)(230 - Math.Ceiling(_random.NextDouble() * 150)),
                230040420 when x < -239 => (int)(-239 + Math.Ceiling(_random.NextDouble() * 150)),
                230040420 when x > 371 => (int)(371 - Math.Ceiling(_random.NextDouble() * 150)),
                _ => x,
            };

            if (FootholdMath.CalcPointBelow(footholds, x, y - 1) is not { } point)
            {
                continue;
            }

            var summoned = new Mob(
                new MapMonster { MonsterId = monsterId, X = point.X, Y = point.Y, Cy = point.Y, Fh = point.FootholdId, Team = -1 },
                stats,
                nextObjectId++)
            {
                ControllerId = controller?.Character.Id ?? 0,
            };
            field.Add(summoned);
            spawned.Add(summoned);
        }

        return spawned;
    }

    /// <summary>
    /// P126：對照 Java <c>applyEffect</c> 的 disease 分支 → <c>getDiseaseBuff(disease, x, duration, skillId, level)</c>。
    /// Java <c>getPlayersInRange</c> 的候選名單只放控制者，所以有 lt/rb 時也只檢查控制者是否在範圍內。
    /// </summary>
    private static ActiveDisease? ApplyDisease(Mob mob, MobSkillDefinition skill, Player? controller, DateTimeOffset now)
    {
        if (controller is null || Player.DiseaseFromMobSkill(skill.SkillId) is not { } disease)
        {
            return null;
        }

        if (!InRange(mob, skill, controller))
        {
            return null;
        }

        return controller.TryGiveDisease(disease, skill.X, skill.DurationMilliseconds, skill.SkillId, skill.Level, now);
    }

    /// <summary>
    /// P125：對照 Java <c>applyEffect</c> case 114：有 lt/rb 時範圍內所有怪物、否則自己，各 <c>heal(getHP(), 0, true)</c>
    /// （補血量取技能的 hp 欄位——與施放門檻同一個值，照抄）。
    /// </summary>
    private static IReadOnlyList<MobHeal> ApplyHeal(FieldInstance field, Mob mob, MobSkillDefinition skill)
    {
        var targets = skill.Area is { } area
            ? field.Objects.OfType<Mob>().Where(m => m.IsAlive && Contains(BoundingBox(area, mob), m.Position)).ToArray()
            : [mob];
        foreach (var target in targets)
        {
            target.Heal(skill.Hp, 0);
        }

        return targets.Select(t => new MobHeal(t.ObjectId, skill.Hp)).ToArray();
    }

    /// <summary>Java <c>applyEffect</c> switch 的 stats 對應（數值皆為 x）。</summary>
    public static IReadOnlyDictionary<MonsterStatus, int> GetBuffStats(int skillId, int x)
    {
        var stats = new Dictionary<MonsterStatus, int>();
        switch (skillId)
        {
            case 100 or 110 or 150: stats[MonsterStatus.WeaponAttackUp] = x; break;
            case 101 or 111 or 151: stats[MonsterStatus.MagicAttackUp] = x; break;
            case 102 or 112 or 152: stats[MonsterStatus.WeaponDefenseUp] = x; break;
            case 103 or 113 or 153: stats[MonsterStatus.MagicDefenseUp] = x; break;
            case 154: stats[MonsterStatus.Acc] = x; break;
            case 155: stats[MonsterStatus.Avoid] = x; break;
            case 156: stats[MonsterStatus.Speed] = x; break;
            case 157: stats[MonsterStatus.Seal] = x; break;
            case 140: stats[MonsterStatus.WeaponImmunity] = x; break;
            case 141: stats[MonsterStatus.MagicImmunity] = x; break;
            case 142: stats[MonsterStatus.DamageImmunity] = x; break;
            case 143:
                stats[MonsterStatus.WeaponDamageReflect] = x;
                stats[MonsterStatus.WeaponImmunity] = x;
                break;
            case 144:
                stats[MonsterStatus.MagicDamageReflect] = x;
                stats[MonsterStatus.MagicImmunity] = x;
                break;
            case 145:
                stats[MonsterStatus.WeaponDamageReflect] = x;
                stats[MonsterStatus.WeaponImmunity] = x;
                stats[MonsterStatus.MagicDamageReflect] = x;
                stats[MonsterStatus.MagicImmunity] = x;
                break;
        }

        return stats;
    }

    /// <summary>
    /// Java <c>MobSkill.calculateBoundingBox</c>：面向左時 lt/rb 直接加在怪物座標；面向右時左右翻轉。
    /// 回傳 (左, 上, 寬, 高)，對應 <c>java.awt.Rectangle</c>。
    /// </summary>
    public static (int X, int Y, int Width, int Height) BoundingBox(MobSkillArea area, Mob mob)
    {
        int ltX, ltY, rbX, rbY;
        if (mob.IsFacingLeft)
        {
            ltX = area.LtX + mob.Position.X;
            ltY = area.LtY + mob.Position.Y;
            rbX = area.RbX + mob.Position.X;
            rbY = area.RbY + mob.Position.Y;
        }
        else
        {
            rbX = -area.LtX + mob.Position.X;
            rbY = area.RbY + mob.Position.Y;
            ltX = -area.RbX + mob.Position.X;
            ltY = area.LtY + mob.Position.Y;
        }

        return (ltX, ltY, rbX - ltX, rbY - ltY);
    }

    /// <summary>Java <c>Rectangle.contains(Point)</c>：x ∈ [左, 左+寬)、y ∈ [上, 上+高)，寬高需 &gt; 0。</summary>
    public static bool Contains((int X, int Y, int Width, int Height) box, Position position)
        => box.Width > 0 && box.Height > 0
            && position.X >= box.X && position.X < box.X + box.Width
            && position.Y >= box.Y && position.Y < box.Y + box.Height;

    /// <summary>
    /// 逐字對照 Java <c>MobSkill.checkCurrentBuff</c>（含原碼 101/111/151 查 WEAPON_DEFENSE_UP、102/112/152 查
    /// MAGIC_ATTACK_UP，與 applyEffect 的對應交錯——照抄）。
    /// </summary>
    public static bool IsBuffAlreadyActive(MobSkillDefinition skill, Mob mob, FieldInstance field)
        => skill.SkillId switch
        {
            100 or 110 or 150 => mob.Statuses.ContainsKey(MonsterStatus.WeaponAttackUp),
            101 or 111 or 151 => mob.Statuses.ContainsKey(MonsterStatus.WeaponDefenseUp),
            102 or 112 or 152 => mob.Statuses.ContainsKey(MonsterStatus.MagicAttackUp),
            103 or 113 or 153 => mob.Statuses.ContainsKey(MonsterStatus.MagicDefenseUp),
            >= 140 and <= 145 => mob.Statuses.ContainsKey(MonsterStatus.DamageImmunity)
                || mob.Statuses.ContainsKey(MonsterStatus.MagicImmunity)
                || mob.Statuses.ContainsKey(MonsterStatus.WeaponImmunity),
            200 => field.Objects.OfType<Mob>().Count() >= skill.Limit,
            _ => false,
        };
}
