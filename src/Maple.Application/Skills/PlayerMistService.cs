using Maple.Application.Combat;
using Maple.Application.Mobs;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Skills;

/// <summary>P132：一次霧週期效果的結果——被上毒的怪物與被回 MP 的玩家（呼叫端廣播封包）。</summary>
public sealed record MistTickResult(
    IReadOnlyList<(int MobObjectId, MonsterStatusEffect Status)> Poisoned,
    IReadOnlyList<Player> MpRestored)
{
    public static MistTickResult Empty { get; } = new([], []);

    public bool IsEmpty => Poisoned.Count == 0 && MpRestored.Count == 0;
}

/// <summary>
/// P132：玩家技能霧（對照 Java <c>MapleStatEffect.applyTo</c> 的 <c>isMist()</c> 分支、<c>MapleMist</c> 玩家建構子、
/// <c>MapleMap.spawnMist</c> 的週期排程）：劇毒霧 2111003／毒炸彈 14111006／火焰齒輪 12111005 對範圍內怪物上毒，
/// 恢復光環 22161003 對範圍內玩家回 MP，煙霧彈 4221006 無伺服器端效果。呼叫端持有 field 鎖。
/// </summary>
public sealed class PlayerMistService
{
    public const int FirstEffectDelayMilliseconds = 2000;
    public const int EffectPeriodMilliseconds = 2500;

    /// <summary>Java <c>MapleMist</c> 玩家建構子 <c>skillDelay = 8</c>。</summary>
    public const short PlayerSkillDelay = 8;

    private readonly Random _random;

    public PlayerMistService(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    /// <summary>Java <c>isMist()</c>：<c>skill &amp;&amp; sourceid ∈ {2111003, 4221006, 12111005, 14111006, 22161003}</c>。</summary>
    public static bool IsMistSkill(int skillId) => skillId is 2111003 or 4221006 or 12111005 or 14111006 or 22161003;

    /// <summary>Java <c>MapleMist</c> 玩家建構子的 <c>isPoisonMist</c>：0 煙霧彈、1 上毒、2 恢復光環。</summary>
    public static int PoisonModeOf(int skillId) => skillId switch
    {
        2111003 or 12111005 or 14111006 => 1,
        22161003 => 2,
        _ => 0,
    };

    /// <summary>
    /// Java <c>calculateBoundingBox(pos, applyfrom.isFacingLeft())</c> → <c>new MapleMist(bounds, applyfrom, this)</c> →
    /// <c>spawnMist(mist, getDuration(), false)</c>。無 lt/rb 時照 Java 給 (x, y, 面向左 1 : -1, 1) 的退化矩形。
    /// 技能等級取擁有者目前等級（Java <c>owner.getSkillLevel</c>）。非霧技能回 null。
    /// </summary>
    public Mist? TrySpawn(FieldInstance field, Player owner, MapleStatEffect effect, Position position, bool facingLeft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(effect);

        if (!effect.IsSkill || !IsMistSkill(effect.SourceId))
        {
            return null;
        }

        var box = effect.Area is { } area
            ? MobSkillService.BoundingBox(area, position, facingLeft)
            : (position.X, position.Y, facingLeft ? 1 : -1, 1);
        var poisonMode = PoisonModeOf(effect.SourceId);
        var objectId = Math.Max(
            CombatService.DefaultMobObjectIdBase,
            field.Objects.Select(static o => o.ObjectId).DefaultIfEmpty(0).Max()) + 1;
        var mist = new Mist(
            objectId,
            owner.Character.Id,
            effect.SourceId,
            owner.GetSkillLevel(effect.SourceId),
            isMobMist: false,
            poisonMode,
            PlayerSkillDelay,
            box,
            now.AddMilliseconds(Math.Max(0, effect.DurationMilliseconds)),
            effect)
        {
            NextEffectAt = poisonMode == 0 ? null : now.AddMilliseconds(FirstEffectDelayMilliseconds),
        };
        field.Add(mist);
        return mist;
    }

    /// <summary>
    /// 推進所有到點且尚未到期的玩家霧（世界 tick 每秒呼叫；2.5 秒週期在 1 秒粒度下最多晚 1 秒）。
    /// 上毒：Java <c>mist.makeChanceResult() &amp;&amp; !isBuffed(POISON)</c> → <c>applyStatus(owner, POISON, poison=true,
    /// duration=霧的總時間)</c>；毒傷同攻擊路徑（P113）。Java 在生成時抓住擁有者物件，擁有者離圖後仍以其計算；
    /// MapleForge 只在擁有者仍在本圖時上毒（不跨 field 持有玩家參照）。
    /// 回 MP：範圍內每位玩家各擲一次機率，<c>addMP((int)(x * (maxMp / 100.0)))</c>。
    /// </summary>
    public MistTickResult Tick(FieldInstance field, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);

        List<(int, MonsterStatusEffect)>? poisoned = null;
        List<Player>? restored = null;
        foreach (var mist in field.Objects.OfType<Mist>().ToArray())
        {
            if (mist.IsMobMist || mist.SourceEffect is not { } effect || mist.NextEffectAt is not { } due
                || due > now || mist.ExpiresAt <= now)
            {
                continue;
            }

            mist.NextEffectAt = due.AddMilliseconds(EffectPeriodMilliseconds);
            if (mist.PoisonMode == 1)
            {
                PoisonMobs(field, mist, effect, now, poisoned ??= []);
            }
            else if (mist.PoisonMode == 2)
            {
                RestoreMp(field, mist, effect, restored ??= []);
            }
        }

        return poisoned is null && restored is null
            ? MistTickResult.Empty
            : new MistTickResult(poisoned ?? [], restored ?? []);
    }

    private void PoisonMobs(FieldInstance field, Mist mist, MapleStatEffect effect, DateTimeOffset now, List<(int, MonsterStatusEffect)> poisoned)
    {
        var owner = field.Players.FirstOrDefault(p => p.Character.Id == mist.OwnerId);
        if (owner is null)
        {
            return;
        }

        var duration = effect.DurationMilliseconds >= 2_000_000_000 ? 5000 : Math.Max(0, effect.DurationMilliseconds);
        foreach (var mob in field.Objects.OfType<Mob>().ToArray())
        {
            if (!mob.IsAlive || !MobSkillService.Contains(mist.Box, mob.Position))
            {
                continue;
            }

            if (!MakeChanceResult(effect.Prop) || mob.Statuses.ContainsKey(MonsterStatus.Poison))
            {
                continue;
            }

            // Java applyStatus：poison && getHp() > 1 才計算毒傷；hp <= 1 整個狀態不套用（同 CombatService）。
            if (mob.Hp <= 1)
            {
                continue;
            }

            var damage = MonsterPoisonCalculator.PoisonDamage(mob.Stats.MaxHp, owner, owner.GetSkillLevel(mist.SkillId), _random);
            var status = new MonsterStatusEffect(
                MonsterStatus.Poison,
                damage,
                mist.SkillId,
                null,
                now.AddMilliseconds(duration),
                damage,
                owner.Character.Id);
            if (mob.TryApplyStatus(status, sourceElement: effect.SourceElement) == MonsterStatusApplyResult.Applied)
            {
                poisoned.Add((mob.ObjectId, status));
            }
        }
    }

    private void RestoreMp(FieldInstance field, Mist mist, MapleStatEffect effect, List<Player> restored)
    {
        foreach (var player in field.Players.ToArray())
        {
            if (!MobSkillService.Contains(mist.Box, player.Position) || !MakeChanceResult(effect.Prop))
            {
                continue;
            }

            var amount = (int)(effect.X * (player.MaxMp / 100.0));
            if (amount <= 0)
            {
                continue;
            }

            player.HealMp(amount);
            restored.Add(player);
        }
    }

    /// <summary>Java <c>MapleStatEffect.makeChanceResult</c>：<c>prop == 100 || nextInt(99) &lt; prop</c>。</summary>
    private bool MakeChanceResult(int prop) => prop == 100 || _random.Next(99) < prop;
}
