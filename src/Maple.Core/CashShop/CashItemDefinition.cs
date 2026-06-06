namespace Maple.Core.CashShop;

/// <summary>
/// 版本無關 Cash Shop 商品定義。欄位對照 Java CashItem/CashModItem：
/// SN / ItemId / Count / Price / Period / Gender / Class / OnSale。
/// </summary>
public sealed record CashItemDefinition(
    int SerialNumber,
    int ItemId,
    short Count,
    int Price,
    int PeriodDays,
    byte Gender,
    int Class,
    bool OnSale)
{
    public short Quantity => Count <= 0 ? (short)1 : Count;

    public bool GenderMatches(byte characterGender) => Gender == 2 || Gender == characterGender;
}
