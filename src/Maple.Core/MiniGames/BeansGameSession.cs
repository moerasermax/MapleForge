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

/// <summary>對照 Java BeanGame type=7 的三段計時分級（rewardBalls(amount, stage)）。</summary>
public readonly record struct BeansTimingReward(int Amount, int Stage, int BeansAfter);

public sealed class BeansGameSession
{
    public const int StartCost = 1;

    public int PlayerId { get; }

    public bool IsActive { get; private set; }

    public int LightLevel { get; private set; }

    public bool CanGainReward { get; private set; }

    /// <summary>對照 Java beans_stage（type=7 計時分級進度，0=未開始）。</summary>
    public int Stage { get; private set; }

    /// <summary>對照 Java beans_time（type=7 分級起算時間，客戶端傳來的時間戳）。</summary>
    public int StageStartTime { get; private set; }

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

    /// <summary>
    /// 上跑馬燈+固定 2000 顆獎勵（對照 Java BeanGame type=5）。CanUseBeans/canGainBeansReward
    /// 兩者皆真才發，發放後 CanGainReward 歸 false（Java：發完當次不能再發，要重新 Shoot 才行）。
    /// </summary>
    public BeansGameActionResult TryGainMarqueeReward(int currentBeans)
    {
        if (!IsActive || !CanGainReward)
        {
            return new BeansGameActionResult(BeansGameActionStatus.Ignored, currentBeans);
        }

        CanGainReward = false;
        const int reward = 2000;
        return new BeansGameActionResult(BeansGameActionStatus.Success, currentBeans + reward, reward);
    }

    /// <summary>
    /// 計時分級獎勵（對照 Java BeanGame type=7）：以 <paramref name="clientTime"/>（客戶端時間戳）
    /// 對照 <see cref="StageStartTime"/> 判斷經過秒數分三級：&gt;10s 給 0 顆並重置 stage=5（Java 用
    /// stage 當封包參數，不是真的「進度」語意，照抄）；5~10s 給 100 顆 stage=4；&lt;5s 給 100 顆
    /// stage=1。**不像 Marquee 會清 CanGainReward**——Java 這裡沒有清，允許重複觸發（照抄 Java 行為，
    /// 不修正這個看似可重複拿獎的設計）。
    /// </summary>
    public BeansTimingReward? EvaluateTiming(int clientTime, int currentBeans)
    {
        if (!IsActive || !CanGainReward)
        {
            return null;
        }

        if (Stage == 0)
        {
            StageStartTime = clientTime;
        }

        var elapsed = clientTime - StageStartTime;
        if (elapsed > 10000)
        {
            Stage = 0;
            return new BeansTimingReward(0, 5, currentBeans);
        }

        if (elapsed > 5000)
        {
            Stage = 4;
            return new BeansTimingReward(100, 4, currentBeans + 100);
        }

        Stage = 1;
        return new BeansTimingReward(100, 1, currentBeans + 100);
    }

    public void Reset()
    {
        IsActive = false;
        LightLevel = 0;
        CanGainReward = false;
        Stage = 0;
        StageStartTime = 0;
    }
}
