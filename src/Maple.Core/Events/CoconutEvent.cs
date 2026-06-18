namespace Maple.Core.Events;

public enum CoconutTeam
{
    Maple = 0,
    Story = 1,
}

public enum CoconutHitOutcome
{
    None,
    Stopped,
    Bombed,
    Fell,
}

public enum CoconutHitStatus
{
    Applied,
    EventNotRunning,
    UnknownCoconut,
    CoconutAlreadyStopped,
}

public sealed record CoconutHitResult(
    CoconutHitStatus Status,
    CoconutHitOutcome Outcome = CoconutHitOutcome.None,
    short CoconutId = 0,
    CoconutTeam? ScoringTeam = null,
    int MapleScore = 0,
    int StoryScore = 0)
{
    public bool Applied => Status == CoconutHitStatus.Applied;
    public bool ScoreChanged => Applied && Outcome == CoconutHitOutcome.Fell && ScoringTeam is not null;
}

public sealed class CoconutState
{
    public short Id { get; }

    public int Hits { get; private set; }

    public bool IsHittable { get; private set; }

    public bool IsStopped { get; private set; }

    internal CoconutState(short id, bool hittable)
    {
        Id = id;
        IsHittable = hittable;
    }

    internal void Hit()
    {
        Hits++;
    }

    internal void Stop()
    {
        IsHittable = false;
        IsStopped = true;
    }

    internal void SetHittable(bool hittable) => IsHittable = hittable;
}

public sealed class CoconutEvent
{
    public const int DefaultCoconutCount = 506;
    public const double StopChance = 0.40;
    public const double BombChance = 0.20;

    private readonly Dictionary<short, CoconutState> _coconuts;

    public bool IsRunning { get; private set; }

    public int MapleScore { get; private set; }

    public int StoryScore { get; private set; }

    public IReadOnlyDictionary<short, CoconutState> Coconuts => _coconuts;

    public CoconutEvent(int coconutCount = DefaultCoconutCount, bool running = false)
    {
        if (coconutCount <= 0 || coconutCount > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(coconutCount), coconutCount, "Coconut count must fit a positive short id range.");
        }

        IsRunning = running;
        _coconuts = Enumerable.Range(0, coconutCount)
            .ToDictionary(i => (short)i, i => new CoconutState((short)i, running));
    }

    public static CoconutEvent CreateRunning(int coconutCount = DefaultCoconutCount) => new(coconutCount, running: true);

    public void Start()
    {
        IsRunning = true;
        foreach (var coconut in _coconuts.Values)
        {
            if (!coconut.IsStopped)
            {
                coconut.SetHittable(true);
            }
        }
    }

    public void Stop()
    {
        IsRunning = false;
        foreach (var coconut in _coconuts.Values)
        {
            coconut.SetHittable(false);
        }
    }

    public CoconutHitResult Hit(short coconutId, CoconutTeam team, double roll)
    {
        if (!Enum.IsDefined(team))
        {
            throw new ArgumentOutOfRangeException(nameof(team), team, "Coconut team must be Maple or Story.");
        }

        if (roll is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roll), roll, "Roll must be in the [0, 1) range.");
        }

        if (!IsRunning)
        {
            return Result(CoconutHitStatus.EventNotRunning, CoconutHitOutcome.None, coconutId);
        }

        if (!_coconuts.TryGetValue(coconutId, out var coconut))
        {
            return Result(CoconutHitStatus.UnknownCoconut, CoconutHitOutcome.None, coconutId);
        }

        if (!coconut.IsHittable || coconut.IsStopped)
        {
            return Result(CoconutHitStatus.CoconutAlreadyStopped, CoconutHitOutcome.None, coconutId);
        }

        coconut.Hit();
        coconut.Stop();

        if (roll < StopChance)
        {
            return Result(CoconutHitStatus.Applied, CoconutHitOutcome.Stopped, coconutId);
        }

        if (roll < StopChance + BombChance)
        {
            return Result(CoconutHitStatus.Applied, CoconutHitOutcome.Bombed, coconutId);
        }

        if (team == CoconutTeam.Maple)
        {
            MapleScore++;
        }
        else
        {
            StoryScore++;
        }

        return Result(CoconutHitStatus.Applied, CoconutHitOutcome.Fell, coconutId, team);
    }

    private CoconutHitResult Result(
        CoconutHitStatus status,
        CoconutHitOutcome outcome,
        short coconutId,
        CoconutTeam? scoringTeam = null)
        => new(status, outcome, coconutId, scoringTeam, MapleScore, StoryScore);

}
