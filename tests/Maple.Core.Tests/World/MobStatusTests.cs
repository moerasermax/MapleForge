using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

/// <summary>P110：對照 Java <c>MapleMonster.applyStatus</c>／<c>cancelStatus</c> 的狀態規則。</summary>
public sealed class MobStatusTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryApplyStatus_NormalMob_Applies()
    {
        var mob = MakeMob();

        Assert.Equal(MonsterStatusApplyResult.Applied, mob.TryApplyStatus(Effect(MonsterStatus.Stun)));
        Assert.True(mob.Statuses.ContainsKey(MonsterStatus.Stun));
    }

    [Fact]
    public void TryApplyStatus_SameStatusTwice_AlreadyActive()
    {
        var mob = MakeMob();
        mob.TryApplyStatus(Effect(MonsterStatus.Stun));

        Assert.Equal(MonsterStatusApplyResult.AlreadyActive, mob.TryApplyStatus(Effect(MonsterStatus.Stun)));
    }

    [Theory]
    [InlineData(MonsterStatus.Stun, MonsterStatusApplyResult.Immune)]
    [InlineData(MonsterStatus.Freeze, MonsterStatusApplyResult.Immune)]
    [InlineData(MonsterStatus.Poison, MonsterStatusApplyResult.Immune)]
    [InlineData(MonsterStatus.Speed, MonsterStatusApplyResult.Applied)]
    [InlineData(MonsterStatus.NinjaAmbush, MonsterStatusApplyResult.Applied)]
    [InlineData(MonsterStatus.Watk, MonsterStatusApplyResult.Applied)]
    public void TryApplyStatus_Boss_OnlyAcceptsSpeedAmbushWatk(MonsterStatus status, MonsterStatusApplyResult expected)
    {
        var mob = MakeMob(new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1, Boss: true));

        Assert.Equal(expected, mob.TryApplyStatus(Effect(status)));
    }

    [Theory]
    [InlineData(MonsterStatus.Stun, MonsterStatusApplyResult.Immune)]
    [InlineData(MonsterStatus.Speed, MonsterStatusApplyResult.Immune)]
    [InlineData(MonsterStatus.VenomousWeapon, MonsterStatusApplyResult.Immune)]
    [InlineData(MonsterStatus.Freeze, MonsterStatusApplyResult.Applied)]
    public void TryApplyStatus_Friendly_BlocksStunSpeedPoison(MonsterStatus status, MonsterStatusApplyResult expected)
    {
        var mob = MakeMob(new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1, Friendly: true));

        Assert.Equal(expected, mob.TryApplyStatus(Effect(status)));
    }

    [Fact]
    public void TryApplyStatus_NoDoom_BlocksDoom()
    {
        var mob = MakeMob(new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1, NoDoom: true));

        Assert.Equal(MonsterStatusApplyResult.Immune, mob.TryApplyStatus(Effect(MonsterStatus.Doom)));
    }

    [Fact]
    public void TryApplyStatus_FreezeImmuneMonsterIds()
    {
        var mob = MakeMob(new MobStats(9400253, 100, MaxMp: 0, Level: 1, Exp: 1), monsterId: 9400253);

        Assert.Equal(MonsterStatusApplyResult.Immune, mob.TryApplyStatus(Effect(MonsterStatus.Freeze)));
    }

    [Fact]
    public void TryApplyStatus_PoisonWithoutSourceEffect_Immune()
    {
        Assert.Equal(
            MonsterStatusApplyResult.Immune,
            MakeMob().TryApplyStatus(Effect(MonsterStatus.Poison), hasSourceEffect: false));
    }

    [Fact]
    public void TryApplyStatus_DeadMob_MobDead()
    {
        var mob = MakeMob();
        mob.TakeDamage(1000);

        Assert.Equal(MonsterStatusApplyResult.MobDead, mob.TryApplyStatus(Effect(MonsterStatus.Stun)));
    }

    [Fact]
    public void CancelStatus_SummonCannotBeCancelled()
    {
        var mob = MakeMob();
        mob.TryApplyStatus(Effect(MonsterStatus.Summon));

        Assert.False(mob.CancelStatus(MonsterStatus.Summon));
        Assert.True(mob.Statuses.ContainsKey(MonsterStatus.Summon));
    }

    [Fact]
    public void ExpireStatuses_RemovesOnlyExpired()
    {
        var mob = MakeMob();
        mob.TryApplyStatus(Effect(MonsterStatus.Stun, T0.AddSeconds(2)));
        mob.TryApplyStatus(Effect(MonsterStatus.Speed, T0.AddSeconds(10)));
        mob.TryApplyStatus(Effect(MonsterStatus.Darkness, expiresAt: null));

        var expired = mob.ExpireStatuses(T0.AddSeconds(2));

        Assert.Equal(new[] { MonsterStatus.Stun }, expired.Select(static e => e.Status));
        Assert.Equal(2, mob.Statuses.Count);
    }

    private static MonsterStatusEffect Effect(MonsterStatus status, DateTimeOffset? expiresAt = null)
        => new(status, 1, 2111003, null, expiresAt);

    private static Mob MakeMob(MobStats? stats = null, int monsterId = 100100)
        => new(
            new MapMonster { MonsterId = monsterId, X = 0, Y = 0, Fh = 1 },
            stats ?? new MobStats(monsterId, 100, MaxMp: 0, Level: 1, Exp: 1),
            objectId: 100001);
}
