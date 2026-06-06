using System.Text.Json;
using Maple.Core.Shops;

namespace Maple.Content.Shops;

public sealed class JsonShopCatalog : IShopCatalog
{
    private readonly IReadOnlyDictionary<int, ShopDefinition> _shopsById;
    private readonly IReadOnlyDictionary<int, ShopDefinition> _shopsByNpcId;
    private readonly IReadOnlyDictionary<int, int> _sellPricesByItemId;

    public JsonShopCatalog(string path)
        : this(File.OpenRead(path))
    {
    }

    public JsonShopCatalog(Stream stream)
    {
        using (stream)
        {
            var document = JsonSerializer.Deserialize<ShopCatalogDocument>(stream, CreateJsonOptions())
                ?? new ShopCatalogDocument();

            var shops = document.Shops
                .Select(s => new ShopDefinition(
                    s.ShopId,
                    s.NpcId,
                    s.Items.Select(i => new ShopItem(
                        i.ItemId,
                        i.Price,
                        i.Buyable,
                        i.RequiredItemId,
                        i.RequiredItemQuantity,
                        i.SellPrice)).ToArray()))
                .ToArray();

            _shopsById = shops.ToDictionary(s => s.ShopId);
            _shopsByNpcId = shops.ToDictionary(s => s.NpcId);
            _sellPricesByItemId = shops
                .SelectMany(s => s.Items)
                .GroupBy(i => i.ItemId)
                .ToDictionary(g => g.Key, g => g.First().SellPrice);
        }
    }

    public ShopDefinition? GetShop(int shopId) => _shopsById.GetValueOrDefault(shopId);

    public ShopDefinition? GetShopForNpc(int npcId) => _shopsByNpcId.GetValueOrDefault(npcId);

    public int? GetSellPrice(int itemId)
        => _sellPricesByItemId.TryGetValue(itemId, out var price) ? price : null;

    private static JsonSerializerOptions CreateJsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

    private sealed class ShopCatalogDocument
    {
        public List<ShopDto> Shops { get; init; } = new();
    }

    private sealed class ShopDto
    {
        public int ShopId { get; init; }
        public int NpcId { get; init; }
        public List<ShopItemDto> Items { get; init; } = new();
    }

    private sealed class ShopItemDto
    {
        public int ItemId { get; init; }
        public int Price { get; init; }
        public short Buyable { get; init; } = 1000;
        public int RequiredItemId { get; init; }
        public int RequiredItemQuantity { get; init; }
        public int SellPrice { get; init; }
    }
}
