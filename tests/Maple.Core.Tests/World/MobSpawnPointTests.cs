using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>
/// P064（M4-2 世界 tick）：<see cref="MobSpawnPoint"/> 對照 Java <c>server.life.SpawnPoint</c>
/// 的 <c>shouldSpawn</c>/死亡監聽重排邏輯。刻意不接任何排程器或真正生怪行為，只驗證這個
/// 純狀態機本身對不對。
/// </summary>
public sealed class MobSpawnPointTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldSpawn_NegativeMobTime_NeverSpawns()
    {
        var point = NewPoint(mobTime: -1, mobile: true);

        Assert.False(point.ShouldSpawn(Now));
        Assert.False(point.ShouldSpawn(Now + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void ShouldSpawn_FreshPoint_CanSpawnImmediately()
    {
        var point = NewPoint(mobTime: 10, mobile: true);

        Assert.True(point.ShouldSpawn(Now));
    }

    [Fact]
    public void ShouldSpawn_PositiveMobTime_CapsAtOneConcurrentInstance()
    {
        var point = NewPoint(mobTime: 10, mobile: true);
        point.OnSpawned();

        Assert.False(point.ShouldSpawn(Now));
    }

    [Fact]
    public void ShouldSpawn_ImmobileZeroMobTime_CapsAtOneConcurrentInstance()
    {
        // 對照 Java：mobTime==0 但怪物不會走動時，跟 mobTime>0 一樣單點只能同時 1 隻。
        var point = NewPoint(mobTime: 0, mobile: false);
        point.OnSpawned();

        Assert.False(point.ShouldSpawn(Now));
    }

    [Fact]
    public void ShouldSpawn_MobileZeroMobTime_AllowsUpToTwoConcurrentInstances()
    {
        var point = NewPoint(mobTime: 0, mobile: true);
        point.OnSpawned();

        Assert.True(point.ShouldSpawn(Now));

        point.OnSpawned();

        Assert.False(point.ShouldSpawn(Now));
    }

    [Fact]
    public void OnMonsterKilled_PositiveMobTime_DelaysNextSpawnByMobTimeSeconds()
    {
        var point = NewPoint(mobTime: 10, mobile: true);
        point.OnSpawned();

        point.OnMonsterKilled(Now);

        Assert.Equal(Now + TimeSpan.FromSeconds(10), point.NextPossibleSpawn);
        Assert.False(point.ShouldSpawn(Now + TimeSpan.FromSeconds(9)));
        Assert.True(point.ShouldSpawn(Now + TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void OnMonsterKilled_ZeroMobTime_AllowsImmediateRespawn()
    {
        var point = NewPoint(mobTime: 0, mobile: false);
        point.OnSpawned();

        point.OnMonsterKilled(Now);

        Assert.Equal(Now, point.NextPossibleSpawn);
        Assert.True(point.ShouldSpawn(Now));
    }

    [Fact]
    public void OnMonsterKilled_DecrementsSpawnedCountButNotBelowZero()
    {
        var point = NewPoint(mobTime: 10, mobile: true);

        point.OnMonsterKilled(Now);

        Assert.Equal(0, point.SpawnedCount);
    }

    private static MobSpawnPoint NewPoint(int mobTime, bool mobile) => new(
        new MapMonster { MonsterId = 100100, MobTime = mobTime },
        mobile,
        Now);
}
