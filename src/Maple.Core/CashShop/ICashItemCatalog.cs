namespace Maple.Core.CashShop;

/// <summary>Cash Shop 商品目錄抽象；實作可來自 WZ、DB、JSON 或測試假資料。</summary>
public interface ICashItemCatalog
{
    CashItemDefinition? GetBySerialNumber(int serialNumber);
}
