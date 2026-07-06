namespace Maple.Core.CashShop;

public enum CashCouponRewardType
{
    CashPoints = 1,
    MaplePoints = 2,
    Item = 3,
    Meso = 4,
}

/// <summary>Cash Shop coupon code reward definition. Mirrors Java nxcode reward columns.</summary>
public sealed class CashCoupon
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public bool Valid { get; set; } = true;

    public CashCouponRewardType Type { get; set; }

    public int Item { get; set; }

    public short Size { get; set; } = 1;

    /// <summary>Reward item period in days; -1 means permanent, matching Java nxcode.time.</summary>
    public int Time { get; set; } = -1;

    public string UsedBy { get; set; } = string.Empty;

    public DateTimeOffset? UsedAt { get; set; }
}
