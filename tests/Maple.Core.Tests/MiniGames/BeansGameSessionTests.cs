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
}
