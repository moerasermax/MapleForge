using Maple.Core.Skills;

namespace Maple.Core.World;

/// <summary>P092：週期性 buff 效果的一次 tick 結果。</summary>
public sealed record PeriodicBuffTick(int HpDelta, IReadOnlyList<PlayerBuffCancellation> Cancellations);

public sealed partial class Player
{
    /// <summary>Java <c>canRecover</c>：<c>lastRecoveryTime + 5000 &lt; now</c>。</summary>
    public static readonly TimeSpan RecoveryInterval = TimeSpan.FromMilliseconds(5_000);

    private DateTimeOffset? _lastRecoveryAt;

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
