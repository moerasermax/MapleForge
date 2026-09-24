using Maple.Core.Maps;

namespace Maple.Core.World;

/// <summary>
/// P074：地圖持續扣血（寒冷/高溫/水中地圖）的執行期狀態。對照 Java <c>MapleMap</c> 的
/// <c>decHP</c>/<c>decHPInterval</c>/<c>protectItem</c>/<c>lastHurtTime</c> 與 <c>canHurt()</c>：
/// <c>setHPDec</c> 在 <c>decHP &gt; 0</c>（或迷你地城 749040100）時啟動計時；<c>canHurt</c> 在
/// <c>lastHurtTime + decHPInterval &lt; now</c> 時回 true 並把 <c>lastHurtTime</c> 推到 now。
/// </summary>
public sealed class FieldHpDecay
{
    /// <summary>Java 特例：迷你地城 749040100 不論 <c>decHP</c> 都啟動計時。</summary>
    public const int MiniDungeonMapId = 749040100;

    private FieldHpDecay(int decHp, int intervalMilliseconds, int protectItemId, DateTimeOffset startedAt)
    {
        DecHp = decHp;
        IntervalMilliseconds = intervalMilliseconds;
        ProtectItemId = protectItemId;
        LastHurtAt = startedAt;
    }

    public int DecHp { get; }

    public int IntervalMilliseconds { get; }

    public int ProtectItemId { get; }

    public DateTimeOffset LastHurtAt { get; private set; }

    /// <summary>依地圖靜態資料建立；不需要扣血的地圖回 null（對照 Java <c>lastHurtTime</c> 維持 0、<c>canHurt</c> 永遠 false）。</summary>
    public static FieldHpDecay? Create(MapData map, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.DecHp <= 0 && map.MapId != MiniDungeonMapId)
        {
            return null;
        }

        return new FieldHpDecay(map.DecHp, map.DecHpInterval, map.ProtectItem, now);
    }

    /// <summary>對照 Java <c>canHurt()</c>：到了扣血週期回 true 並重設計時起點。</summary>
    public bool TryHurt(DateTimeOffset now)
    {
        if (LastHurtAt.AddMilliseconds(IntervalMilliseconds) < now)
        {
            LastHurtAt = now;
            return true;
        }

        return false;
    }
}
