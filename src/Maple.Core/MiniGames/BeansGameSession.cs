namespace Maple.Core.MiniGames;

public enum BeansGameActionStatus
{
    Success,
    NotActive,
    InsufficientBeans,
    Ignored,
}

public sealed record BeansGameActionResult(
    BeansGameActionStatus Status,
    int BeansAfter,
    int BeansDelta = 0,
    bool ExitRequested = false);

public sealed class BeansGameSession
{
    public const int StartCost = 1;

    public int PlayerId { get; }

    public bool IsActive { get; private set; }

    public int LightLevel { get; private set; }

    public bool CanGainReward { get; private set; }

    public BeansGameSession(int playerId)
    {
        PlayerId = playerId;
    }

    public BeansGameActionResult Start(int currentBeans)
    {
        if (currentBeans < StartCost)
        {
            Reset();
            return new BeansGameActionResult(BeansGameActionStatus.InsufficientBeans, currentBeans, ExitRequested: true);
        }

        IsActive = true;
        CanGainReward = false;
        return new BeansGameActionResult(BeansGameActionStatus.Success, currentBeans - StartCost, -StartCost);
    }

    public BeansGameActionResult Pause(int currentBeans)
    {
        if (!IsActive)
        {
            return new BeansGameActionResult(BeansGameActionStatus.NotActive, currentBeans);
        }

        return new BeansGameActionResult(BeansGameActionStatus.Success, currentBeans);
    }

    public BeansGameActionResult Shoot(int currentBeans, int count)
    {
        if (!IsActive)
        {
            return new BeansGameActionResult(BeansGameActionStatus.NotActive, currentBeans);
        }

        if (currentBeans <= 0)
        {
            Reset();
            return new BeansGameActionResult(BeansGameActionStatus.InsufficientBeans, currentBeans, ExitRequested: true);
        }

        var spend = Math.Max(1, count);
        if (currentBeans < spend)
        {
            Reset();
            return new BeansGameActionResult(BeansGameActionStatus.InsufficientBeans, currentBeans, ExitRequested: true);
        }

        CanGainReward = true;
        return new BeansGameActionResult(BeansGameActionStatus.Success, currentBeans - spend, -spend);
    }

    public int AddLight()
    {
        if (LightLevel < 7)
        {
            LightLevel++;
        }

        return LightLevel;
    }

    public void Reset()
    {
        IsActive = false;
        LightLevel = 0;
        CanGainReward = false;
    }
}
