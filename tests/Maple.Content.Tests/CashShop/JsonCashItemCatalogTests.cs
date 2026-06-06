using System.Text;
using Maple.Content.CashShop;

namespace Maple.Content.Tests.CashShop;

public sealed class JsonCashItemCatalogTests
{
    [Fact]
    public void GetBySerialNumber_LoadsMinimalJsonFields()
    {
        const string json = """
        {
          "items": [
            {
              "serialNumber": 10000001,
              "itemId": 5350000,
              "count": 10,
              "price": 45,
              "periodDays": 0,
              "gender": 2,
              "class": -1,
              "onSale": true
            }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var catalog = new JsonCashItemCatalog(stream);

        var item = catalog.GetBySerialNumber(10000001);

        Assert.NotNull(item);
        Assert.Equal(5350000, item!.ItemId);
        Assert.Equal((short)10, item.Count);
        Assert.Equal(45, item.Price);
        Assert.True(item.OnSale);
    }
}
