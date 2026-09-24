using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Core.Tests.Skills;

/// <summary>P092：<see cref="Player.TryRecover"/> 對照 Java <c>prepareRecovery</c>/<c>canRecover</c>/<c>doRecovery</c>。</summary>
public sealed class PlayerPeriodicBuffTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryRecover_HealsEveryFiveSecondsStrictly()
    {
        var player = NewPlayer(hp: 50);
        ApplyRecovery(player, x: 10);

        Assert.Null(player.TryRecover(Start.AddSeconds(5)));           // 剛好 5 秒：Java 用 <，不觸發
        Assert.Equal(10, player.TryRecover(Start.AddSeconds(6))!.HpDelta);
        Assert.Equal(60, player.Hp);
        Assert.Null(player.TryRecover(Start.AddSeconds(10)));          // 從 6 秒重新計時
        Assert.Equal(10, player.TryRecover(Start.AddSeconds(12))!.HpDelta);
        Assert.Equal(70, player.Hp);
    }

    [Fact]
    public void TryRecover_FullHp_CancelsRecoveryBuff()
    {
        var player = NewPlayer(hp: 100);
        ApplyRecovery(player, x: 10);

        var tick = player.TryRecover(Start.AddSeconds(6));

        Assert.NotNull(tick);
        Assert.Equal(0, tick!.HpDelta);
        Assert.Equal(1001, Assert.Single(tick.Cancellations).SourceId);
        Assert.Empty(player.ActiveBuffs);
    }

    [Fact]
    public void TryRecover_NoRecoveryBuff_ReturnsNull()
    {
        Assert.Null(NewPlayer(hp: 50).TryRecover(Start.AddHours(1)));
    }

    [Fact]
    public void TryDragonBlood_DrainsEveryFourSecondsStrictly()
    {
        var player = NewPlayer(hp: 100);
        ApplyBuff(player, 1311008, MapleBuffStat.DRAGONBLOOD, x: 20);

        Assert.Null(player.TryDragonBlood(Start.AddSeconds(4)));
        var tick = player.TryDragonBlood(Start.AddSeconds(5));
        Assert.Equal((-20, 1311008), (tick!.HpDelta, tick.SourceId));
        Assert.Equal(80, player.Hp);
    }

    [Fact]
    public void TryDragonBlood_HpWouldDropToOneOrBelow_CancelsBuffWithoutDrain()
    {
        // Java：stats.getHp() - x <= 1 → cancelEffectFromBuffStat(DRAGONBLOOD)。
        var player = NewPlayer(hp: 21);
        ApplyBuff(player, 1311008, MapleBuffStat.DRAGONBLOOD, x: 20);

        var tick = player.TryDragonBlood(Start.AddSeconds(5));

        Assert.Equal(0, tick!.HpDelta);
        Assert.Single(tick.Cancellations);
        Assert.Equal(21, player.Hp);
        Assert.Empty(player.ActiveBuffs);
    }

    private static void ApplyBuff(Player player, int sourceId, MapleBuffStat stat, int x)
        => player.ApplySkillEffect(new MapleStatEffect
        {
            SourceId = sourceId,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = new[] { new BuffStatValue(stat, x) },
        }, Start);

    private static void ApplyRecovery(Player player, int x)
        => player.ApplySkillEffect(new MapleStatEffect
        {
            SourceId = 1001,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 30_000,
            Statups = new[] { new BuffStatValue(MapleBuffStat.RECOVERY, x) },
        }, Start);

    private static Player NewPlayer(short hp)
        => new(new Character { Id = 1, Name = "R", Stats = new CharacterStats { Hp = hp, MaxHp = 100, Mp = 10, MaxMp = 10 } }, new Position(0, 0, 0, 0));
}
