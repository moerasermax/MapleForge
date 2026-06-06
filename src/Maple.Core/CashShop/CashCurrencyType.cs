namespace Maple.Core.CashShop;

/// <summary>Cash Shop 扣款幣別。數值對照 Java useNX：1=NX/GASH, 2=MaplePoint。</summary>
public enum CashCurrencyType : byte
{
    Cash = 1,
    MaplePoint = 2,
}
