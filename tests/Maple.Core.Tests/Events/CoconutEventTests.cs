using Maple.Core.Events;

namespace Maple.Core.Tests.Events;

public sealed class CoconutEventTests
{
    [Fact]
    public void Hit_FallingCoconut_AddsScoreForTeam()
    {
        var evt = CoconutEvent.CreateRunning(coconutCount: 3);

        var result = evt.Hit(1, CoconutTeam.Story, roll: 0.75);

        Assert.Equal(CoconutHitStatus.Applied, result.Status);
        Assert.Equal(CoconutHitOutcome.Fell, result.Outcome);
        Assert.Equal(0, evt.MapleScore);
        Assert.Equal(1, evt.StoryScore);
        Assert.True(result.ScoreChanged);
    }

    [Fact]
    public void Hit_StopOutcome_MarksCoconutStoppedWithoutScore()
    {
        var evt = CoconutEvent.CreateRunning(coconutCount: 3);

        var result = evt.Hit(1, CoconutTeam.Maple, roll: 0.10);

        Assert.Equal(CoconutHitOutcome.Stopped, result.Outcome);
        Assert.True(evt.Coconuts[1].IsStopped);
        Assert.Equal(0, evt.MapleScore);
        Assert.Equal(0, evt.StoryScore);
    }

    [Fact]
    public void Hit_AlreadyStoppedCoconut_ReturnsAlreadyStopped()
    {
        var evt = CoconutEvent.CreateRunning(coconutCount: 3);
        evt.Hit(1, CoconutTeam.Maple, roll: 0.10);

        var result = evt.Hit(1, CoconutTeam.Maple, roll: 0.75);

        Assert.Equal(CoconutHitStatus.CoconutAlreadyStopped, result.Status);
        Assert.Equal(0, evt.MapleScore);
    }

    [Fact]
    public void Hit_WhenEventNotRunning_ReturnsNotRunning()
    {
        var evt = new CoconutEvent(coconutCount: 3, running: false);

        var result = evt.Hit(1, CoconutTeam.Maple, roll: 0.75);

        Assert.Equal(CoconutHitStatus.EventNotRunning, result.Status);
    }

    [Fact]
    public void Hit_BombOutcome_DoesNotScore()
    {
        var evt = CoconutEvent.CreateRunning(coconutCount: 3);

        var result = evt.Hit(1, CoconutTeam.Maple, roll: 0.50);

        Assert.Equal(CoconutHitOutcome.Bombed, result.Outcome);
        Assert.Equal(0, evt.MapleScore);
        Assert.Equal(0, evt.StoryScore);
    }
}
