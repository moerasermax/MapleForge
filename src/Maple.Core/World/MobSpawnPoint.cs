using Maple.Core.Maps;

namespace Maple.Core.World;

/// <summary>
/// 地圖上一個怪物重生點的執行期狀態。對照 Java <c>server.life.SpawnPoint</c>——WZ 靜態資料
/// （<see cref="MapMonster"/>，含 <c>MobTime</c>）本身不變，這裡追蹤「目前這個點還能不能生」。
/// P064（M4-2 世界 tick）：只建立資料模型跟純函式，刻意不接任何排程器/怪物真正生怪的行為，
/// 沿用 P061 的「先立可測試的模型，最後才接副作用」分階段慣例。
/// </summary>
public sealed class MobSpawnPoint
{
    private int _spawnedCount;

    public MobSpawnPoint(MapMonster definition, bool mobile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        Mobile = mobile;
        NextPossibleSpawn = now;
    }

    /// <summary>這個重生點的 WZ 靜態資料（位置/怪物 id/MobTime 等）。</summary>
    public MapMonster Definition { get; }

    /// <summary>對照 Java <c>immobile = !monster.getStats().getMobile()</c> 的反相：怪物模板本身是否會走動。</summary>
    public bool Mobile { get; }

    public DateTimeOffset NextPossibleSpawn { get; private set; }

    public int SpawnedCount => _spawnedCount;

    /// <summary>對照 Java <c>SpawnPoint.shouldSpawn</c>：
    /// <c>MobTime &lt; 0</c> 永不重生；<c>MobTime != 0</c> 或怪物不會走動時最多同時 1 隻，
    /// 否則（<c>MobTime == 0</c> 且會走動）最多同時 2 隻；還沒到 <see cref="NextPossibleSpawn"/>
    /// 也不能生。</summary>
    public bool ShouldSpawn(DateTimeOffset now)
    {
        if (Definition.MobTime < 0)
        {
            return false;
        }

        var singleInstanceCap = Definition.MobTime != 0 || !Mobile;
        if ((singleInstanceCap && _spawnedCount > 0) || _spawnedCount > 1)
        {
            return false;
        }

        return NextPossibleSpawn <= now;
    }

    /// <summary>對照 Java <c>spawnMonster</c> 裡的 <c>spawnedMonsters.incrementAndGet()</c>：
    /// 呼叫端確認 <see cref="ShouldSpawn"/> 為真並實際生出怪物後呼叫。</summary>
    public void OnSpawned() => _spawnedCount++;

    /// <summary>對照 Java <c>MonsterListener.monsterKilled</c>：怪物死亡時重算下次可重生時間
    /// （<c>MobTime &gt; 0</c> 才延後，否則立刻可生）並歸還這個重生點的計數額度。</summary>
    public void OnMonsterKilled(DateTimeOffset now)
    {
        NextPossibleSpawn = Definition.MobTime > 0 ? now + TimeSpan.FromSeconds(Definition.MobTime) : now;
        _spawnedCount = Math.Max(0, _spawnedCount - 1);
    }
}
