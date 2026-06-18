using Maple.Core.MiniGames;

namespace Maple.Core.Tests.MiniGames;

public sealed class RpsSessionTests
{
    [Fact]
    public void Start_ActivatesSessionWithZeroWins()
    {
        var session = new RpsSession(1, () => RpsChoice.Scissors);

        session.Start();

        Assert.True(session.IsActive);
        Assert.Equal(0, session.Wins);
        Assert.False(session.AwaitingContinue);
    }

    [Fact]
    public void Play_Win_IncrementsWinsAndWaitsForContinue()
    {
        var session = Started(() => RpsChoice.Scissors);

        var result = session.Play(RpsChoice.Rock);

        Assert.Equal(RpsResult.Win, result);
        Assert.Equal(1, session.Wins);
        Assert.True(session.IsActive);
        Assert.True(session.AwaitingContinue);
    }

    [Fact]
    public void Play_Lose_EndsSession()
    {
        var session = Started(() => RpsChoice.Paper);

        var result = session.Play(RpsChoice.Rock);

        Assert.Equal(RpsResult.Lose, result);
        Assert.False(session.IsActive);
        Assert.False(session.AwaitingContinue);
    }

    [Fact]
    public void Play_Tie_KeepsSessionActiveWithoutIncrementingWins()
    {
        var session = Started(() => RpsChoice.Rock);

        var result = session.Play(RpsChoice.Rock);

        Assert.Equal(RpsResult.Tie, result);
        Assert.True(session.IsActive);
        Assert.Equal(0, session.Wins);
    }

    [Fact]
    public void Continue_AfterWin_AllowsAnotherRound()
    {
        var session = Started(() => RpsChoice.Scissors);
        session.Play(RpsChoice.Rock);

        var continued = session.Continue();

        Assert.True(continued);
        Assert.True(session.IsActive);
        Assert.False(session.AwaitingContinue);
    }

    [Fact]
    public void CashOut_ReturnsRewardAndEndsSession()
    {
        var session = Started(() => RpsChoice.Scissors);
        session.Play(RpsChoice.Rock);

        var reward = session.CashOut();

        Assert.Equal(2000, reward);
        Assert.False(session.IsActive);
    }

    private static RpsSession Started(Func<RpsChoice> opponent)
    {
        var session = new RpsSession(1, opponent);
        session.Start();
        return session;
    }
}
