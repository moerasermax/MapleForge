using Maple.Content.Shops;

namespace Maple.Content.Tests.Shops;

public sealed class JsonShopCatalogTests
{
    [Fact]
    public void BuiltInCatalog_LoadsTemporaryShop35()
    {
        var catalog = new JsonShopCatalog(FindCatalogPath());

        var shop = catalog.GetShop(35);

        Assert.NotNull(shop);
        Assert.Equal(1033002, shop.NpcId);
        Assert.Collection(
            shop.Items,
            item =>
            {
                Assert.Equal(2000000, item.ItemId);
                Assert.Equal(50, item.Price);
                Assert.Equal(25, item.SellPrice);
            },
            item =>
            {
                Assert.Equal(2000001, item.ItemId);
                Assert.Equal(160, item.Price);
                Assert.Equal(80, item.SellPrice);
            });
    }

    private static string FindCatalogPath()
    {
        var fromOutput = Path.Combine(AppContext.BaseDirectory, "Shops", "npc-shops.v113.json");
        if (File.Exists(fromOutput))
        {
            return fromOutput;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Maple.Content", "Shops", "npc-shops.v113.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Unable to locate npc-shops.v113.json");
    }
}
