using Maple.Core.Inventory;
using Maple.Core.Skills;

namespace Maple.Core.World;

/// <summary>死亡懲罰結果種類（對照 Java <c>MapleCharacter.playerDead</c> 經驗值區塊的 if/else 分支）。</summary>
public enum DeathPenaltyKind
{
    /// <summary>初心者系職業（0/1000/2000/2001/3000）不受懲罰。</summary>
    ExemptJob,
    /// <summary>消耗一個護身符（5130000）或強效護身符（5130002）抵銷經驗值損失。</summary>
    CharmConsumed,
    /// <summary>扣除經驗值。</summary>
    ExpLost,
}

public sealed record DeathPenaltyResult(
    DeathPenaltyKind Kind,
    int ExpLost,
    int CharmsLeft,
    IReadOnlyList<InventoryQuantityMutation> CharmMutations);

public sealed partial class Player
{
    /// <summary>護身符（Java <c>5130000</c>）。</summary>
    public const int SafetyCharmItemId = 5130000;

    /// <summary>強效護身符（Java <c>5130002</c>）。</summary>
    public const int SuperSafetyCharmItemId = 5130002;

    /// <summary>Java <c>dispelSkill(0)</c> 會取消的技能來源（召喚/傀儡/分身類）。</summary>
    private static readonly HashSet<int> DeathDispelSkillIds =
    [
        4331003, 4331002, 4341002, 22131001, 1321007, 2121005, 2221005,
        2311006, 2321003, 3111002, 3111005, 3211002, 3211005, 4111002,
    ];

    private static readonly MapleBuffStat[] DeathCancelledStats =
    [
        MapleBuffStat.MORPH, MapleBuffStat.MONSTER_RIDING, MapleBuffStat.SUMMON, MapleBuffStat.PUPPET,
    ];

    /// <summary>
    /// P090：對照 Java <c>MapleCharacter.playerDead</c> 的 buff 區塊：<c>dispelSkill(0)</c>（依 buff stat 列舉順序找第一個
    /// 來源在清單內的 buff，取消整個 effect 後停止）→ <c>cancelEffectFromBuffStat</c> MORPH / MONSTER_RIDING / SUMMON / PUPPET
    /// （各自取消持有該 stat 的整個 effect）。回傳所有取消結果，呼叫端送 cancelBuff 與召喚獸副作用。
    /// </summary>
    public IReadOnlyList<PlayerBuffCancellation> CancelBuffsOnDeath()
    {
        var result = new List<PlayerBuffCancellation>();
        lock (_skillsGate)
        {
            var dispel = _activeBuffs.Values
                .OrderBy(static b => (int)b.Stat)
                .FirstOrDefault(static b => DeathDispelSkillIds.Contains(b.SourceId));
            if (dispel is not null)
            {
                result.AddRange(CancelBuffBySource(dispel.SourceId));
            }

            foreach (var stat in DeathCancelledStats)
            {
                if (_activeBuffs.TryGetValue(stat, out var holder))
                {
                    result.AddRange(CancelBuffBySource(holder.SourceId));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// P076：對照 Java <c>MapleCharacter.playerDead</c> 的經驗值區塊。初心者系職業免懲罰；否則先消耗護身符、
    /// 再消耗強效護身符；都沒有才扣經驗：村莊或 <c>FieldLimitType.RegularExpLoss</c> 地圖扣 1%，其他地圖扣
    /// <c>(職業系 3 為 0.08、其他 0.2) / LUK + 0.05</c>（以 float 計算，比照 Java），乘上本級升級所需經驗，下限 0。
    /// </summary>
    public DeathPenaltyResult ApplyDeathPenalty(bool reducedExpLoss)
    {
        var job = Character.Job;
        if (job is 0 or 1000 or 2000 or 2001 or 3000)
        {
            return new DeathPenaltyResult(DeathPenaltyKind.ExemptJob, 0, 0, Array.Empty<InventoryQuantityMutation>());
        }

        foreach (var charmId in new[] { SafetyCharmItemId, SuperSafetyCharmItemId })
        {
            if (TryConsumeItemById(InventoryType.Cash, charmId, 1, out var mutations))
            {
                var left = Math.Min(Inventory.By(InventoryType.Cash).CountById(charmId), 0xFF);
                return new DeathPenaltyResult(DeathPenaltyKind.CharmConsumed, 0, left, mutations);
            }
        }

        float diePercentage;
        if (reducedExpLoss)
        {
            diePercentage = 0.01f;
        }
        else
        {
            var luk = Character.Stats.Luk;
            float v8 = job / 100 == 3 ? 0.08f : 0.2f;
            diePercentage = luk <= 0 ? float.PositiveInfinity : (float)(v8 / luk + 0.05);
        }

        var expForLevel = GetExpNeededForLevel(Character.Level);
        var loss = float.IsPositiveInfinity(diePercentage)
            ? long.MaxValue
            : (long)((double)expForLevel * diePercentage);
        var newExp = Math.Max(0L, Character.Exp - loss);
        var lost = (int)(Character.Exp - newExp);
        Character.Exp = (int)newExp;
        return new DeathPenaltyResult(DeathPenaltyKind.ExpLost, lost, 0, Array.Empty<InventoryQuantityMutation>());
    }
}
