using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P107：對照 Java <c>MapleMonster</c> attackers 的最後攻擊時間（HPDisplayType 3 只給 4 秒內攻擊者）。</summary>
public sealed class MobRecentAttackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetRecentAttackers_IncludesAttackersWithinFourSeconds()
    {
        var mob = MakeMob();
        mob.RecordAttacker(1, T0);
        mob.RecordAttacker(2, T0.AddSeconds(3));

        Assert.Equal(new[] { 1, 2 }, mob.GetRecentAttackers(T0.AddSeconds(4)).Order());
    }

    [Fact]
    public void GetRecentAttackers_ExcludesStaleAttackers()
    {
        var mob = MakeMob();
        mob.RecordAttacker(1, T0);
        mob.RecordAttacker(2, T0.AddSeconds(3));

        Assert.Equal(new[] { 2 }, mob.GetRecentAttackers(T0.AddSeconds(4.001)));
    }

    [Fact]
    public void RecordAttacker_RepeatedAttack_RefreshesTime()
    {
        var mob = MakeMob();
        mob.RecordAttacker(1, T0);
        mob.RecordAttacker(1, T0.AddSeconds(10));

        Assert.Equal(new[] { 1 }, mob.GetRecentAttackers(T0.AddSeconds(12)));
    }

    private static Mob MakeMob()
        => new(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1),
            objectId: 100001);
}
