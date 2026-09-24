using Maple.Core.Skills;

namespace Maple.Core.World;

/// <summary>P092：週期性 buff 效果的一次 tick 結果。</summary>
public sealed record PeriodicBuffTick(int HpDelta, IReadOnlyList<PlayerBuffCancellation> Cancellations, int SourceId = 0);

public sealed partial class Player
{
    /// <summary>Java <c>canRecover</c>：<c>lastRecoveryTime + 5000 &lt; now</c>。</summary>
    public static readonly TimeSpan RecoveryInterval = TimeSpan.FromMilliseconds(5_000);

    /// <summary>Java <c>canBlood</c>：<c>lastDragonBloodTime + 4000 &lt; now</c>。</summary>
    public static readonly TimeSpan DragonBloodInterval = TimeSpan.FromMilliseconds(4_000);

    /// <summary>Java <c>canBerserk</c>：<c>lastBerserkTime + 10000 &lt; now</c>。</summary>
    public static readonly TimeSpan BerserkInterval = TimeSpan.FromMilliseconds(10_000);

    private DateTimeOffset? _lastRecoveryAt;
    private DateTimeOffset? _lastDragonBloodAt;
    private DateTimeOffset? _lastBerserkAt;

    /// <summary>P094：距離上次狂戰士檢查是否已超過 10 秒（Java <c>lastBerserkTime</c> 初值 0 → 第一次一定通過）。</summary>
    public bool CanCheckBerserk(DateTimeOffset now)
    {
        lock (_skillsGate)
        {
            return _lastBerserkAt is not { } last || last + BerserkInterval < now;
        }
    }

    /// <summary>P094：記錄這次狂戰士檢查時間（Java <c>lastBerserkTime = now</c>）。</summary>
    public void MarkBerserkChecked(DateTimeOffset now)
    {
        lock (_skillsGate)
        {
            _lastBerserkAt = now;
        }
    }

    /// <summary>
    /// P093：龍之魂（DRAGONBLOOD buff）週期扣血。對照 Java <c>registerEffect</c>（<c>prepareDragonBlood</c>）+
    /// <c>canBlood(now)</c> + <c>doDragonBlood</c>：到期重設計時；<c>hp - x &lt;= 1</c> → 取消 DRAGONBLOOD；否則 <c>addHP(-x)</c>。
    /// 職業 131/132 與「活著」的判斷由呼叫端負責（Java 在 <c>handleCooldowns</c> 內檢查）。
    /// </summary>
    public PeriodicBuffTick? TryDragonBlood(DateTimeOffset now)
    {
        ActiveBuffStat? blood;
        lock (_skillsGate)
        {
            blood = _activeBuffs.GetValueOrDefault(MapleBuffStat.DRAGONBLOOD);
            if (blood is null)
            {
                _lastDragonBloodAt = null;
                return null;
            }

            var last = _lastDragonBloodAt is { } l && l >= blood.StartedAt ? l : blood.StartedAt;
            if (last + DragonBloodInterval >= now)
            {
                _lastDragonBloodAt = last;
                return null;
            }

            _lastDragonBloodAt = now;
        }

        if (Character.Stats.Hp - blood.Value <= 1)
        {
            return new PeriodicBuffTick(0, CancelBuffBySource(blood.SourceId), blood.SourceId);
        }

        Character.Stats.Hp = (short)(Character.Stats.Hp - blood.Value);
        return new PeriodicBuffTick(-blood.Value, Array.Empty<PlayerBuffCancellation>(), blood.SourceId);
    }

    /// <summary>
    /// P092：回復術（RECOVERY buff）週期回血。對照 Java <c>MapleCharacter.registerEffect</c>（套用時 <c>prepareRecovery</c>
    /// 啟動計時）+ <c>canRecover(now)</c> + <c>doRecovery</c>：到期後重設計時；HP 已滿 → 取消 RECOVERY buff；否則
    /// <c>healHP(x, true)</c>。沒有 RECOVERY buff 或未到週期回 null。呼叫端負責「玩家活著」的判斷（Java 在
    /// <c>handleCooldowns</c> 的 <c>isAlive()</c> 區塊內呼叫）。
    /// </summary>
    public PeriodicBuffTick? TryRecover(DateTimeOffset now)
    {
        ActiveBuffStat? recovery;
        lock (_skillsGate)
        {
            recovery = _activeBuffs.GetValueOrDefault(MapleBuffStat.RECOVERY);
            if (recovery is null)
            {
                _lastRecoveryAt = null;
                return null;
            }

            var last = _lastRecoveryAt is { } l && l >= recovery.StartedAt ? l : recovery.StartedAt;
            if (last + RecoveryInterval >= now)
            {
                _lastRecoveryAt = last;
                return null;
            }

            _lastRecoveryAt = now;
        }

        if (Character.Stats.Hp >= Character.Stats.MaxHp)
        {
            return new PeriodicBuffTick(0, CancelBuffBySource(recovery.SourceId));
        }

        HealHp(recovery.Value);
        return new PeriodicBuffTick(recovery.Value, Array.Empty<PlayerBuffCancellation>());
    }
}
