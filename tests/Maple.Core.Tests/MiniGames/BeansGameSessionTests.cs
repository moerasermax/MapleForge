using Maple.Core.MiniGames;

namespace Maple.Core.Tests.MiniGames;

public sealed class BeansGameSessionTests
{
    [Fact]
    public void Start_DeductsOneBeanAndActivatesSession()
    {
        var session = new BeansGameSession(1);

        var result = session.Start(currentBeans: 5);

        Assert.Equal(BeansGameActionStatus.Success, result.Status);
        Assert.Equal(4, result.BeansAfter);
        Assert.Equal(-1, result.BeansDelta);
        Assert.True(session.IsActive);
    }

    [Fact]
    public void Start_WithoutBeans_RequestsExit()
    {
        var session = new BeansGameSession(1);

        var result = session.Start(currentBeans: 0);

        Assert.Equal(BeansGameActionStatus.InsufficientBeans, result.Status);
        Assert.True(result.ExitRequested);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void Shoot_DeductsRequestedCountAndAllowsReward()
    {
        var session = new BeansGameSession(1);
        session.Start(currentBeans: 10);

        var result = session.Shoot(currentBeans: 9, count: 3);

        Assert.Equal(BeansGameActionStatus.Success, result.Status);
        Assert.Equal(6, result.BeansAfter);
        Assert.Equal(-3, result.BeansDelta);
        Assert.True(session.CanGainReward);
    }

    [Fact]
    public void Shoot_WhenNotActive_ReturnsNotActive()
    {
        var session = new BeansGameSession(1);

        var result = session.Shoot(currentBeans: 5, count: 1);

        Assert.Equal(BeansGameActionStatus.NotActive, result.Status);
        Assert.Equal(5, result.BeansAfter);
    }

    [Fact]
    public void TryGainMarqueeReward_AfterShoot_Grants2000AndClearsCanGainReward()
    {
        // 對照 Java BeanGame type=5：canGainBeansReward 為真才發，發完歸 false。
        var session = new BeansGameSession(1);
        session.Start(currentBeans: 10);
        session.Shoot(currentBeans: 9, count: 1);

        var result = session.TryGainMarqueeReward(currentBeans: 8);

        Assert.Equal(BeansGameActionStatus.Success, result.Status);
        Assert.Equal(2008, result.BeansAfter);
        Assert.Equal(2000, result.BeansDelta);
        Assert.False(session.CanGainReward);
    }

    [Fact]
    public void TryGainMarqueeReward_WithoutPriorShoot_Ignored()
    {
        var session = new BeansGameSession(1);
        session.Start(currentBeans: 10);

        var result = session.TryGainMarqueeReward(currentBeans: 9);

        Assert.Equal(BeansGameActionStatus.Ignored, result.Status);
        Assert.Equal(9, result.BeansAfter);
    }

    [Fact]
    public void TryGainMarqueeReward_CalledTwice_OnlyGrantsOnce()
    {
        var session = new BeansGameSession(1);
        session.Start(currentBeans: 10);
        session.Shoot(currentBeans: 9, count: 1);

        var first = session.TryGainMarqueeReward(currentBeans: 8);
        var second = session.TryGainMarqueeReward(currentBeans: first.BeansAfter);

        Assert.Equal(BeansGameActionStatus.Success, first.Status);
        Assert.Equal(BeansGameActionStatus.Ignored, second.Status);
        Assert.Equal(first.BeansAfter, second.BeansAfter);
    }

    [Theory]
    [InlineData(4999, 100, 1)]   // <5s：100 顆 stage=1
    [InlineData(5001, 100, 4)]   // 5~10s：100 顆 stage=4
    [InlineData(10001, 0, 5)]    // >10s：0 顆 stage=5（重置）
    public void EvaluateTiming_TieredByElapsedClientTime(int elapsedMs, int expectedAmount, int expectedStage)
    {
        // 對照 Java BeanGame type=7 的三段計時分級。
        var session = new BeansGameSession(1);
        session.Start(currentBeans: 10);
        session.Shoot(currentBeans: 9, count: 1);

        var first = session.EvaluateTiming(clientTime: 100_000, currentBeans: 8);
        Assert.NotNull(first);

        var reward = session.EvaluateTiming(clientTime: 100_000 + elapsedMs, currentBeans: 8);

        Assert.NotNull(reward);
        Assert.Equal(expectedAmount, reward!.Value.Amount);
        Assert.Equal(expectedStage, reward.Value.Stage);
        Assert.Equal(8 + expectedAmount, reward.Value.BeansAfter);
    }

    [Fact]
    public void EvaluateTiming_WithoutPriorShoot_ReturnsNull()
    {
        var session = new BeansGameSession(1);
        session.Start(currentBeans: 10);

        var reward = session.EvaluateTiming(clientTime: 1000, currentBeans: 9);

        Assert.Null(reward);
    }

    [Fact]
    public void Reset_ClearsStageAndStageStartTime()
    {
        var session = new BeansGameSession(1);
        session.Start(currentBeans: 10);
        session.Shoot(currentBeans: 9, count: 1);
        session.EvaluateTiming(clientTime: 5000, currentBeans: 8);
        Assert.NotEqual(0, session.Stage);

        session.Reset();

        Assert.Equal(0, session.Stage);
        Assert.Equal(0, session.StageStartTime);
    }
}
