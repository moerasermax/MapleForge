using System.Text.Json;
using Maple.Core.CashShop;

namespace Maple.Content.CashShop;

public sealed class JsonCashItemCatalog : ICashItemCatalog
{
    private readonly IReadOnlyDictionary<int, CashItemDefinition> _itemsBySerialNumber;

    public JsonCashItemCatalog(string path)
        : this(File.OpenRead(path))
    {
    }

    public JsonCashItemCatalog(Stream stream)
    {
        using (stream)
        {
            var document = JsonSerializer.Deserialize<CashItemCatalogDocument>(stream, CreateJsonOptions())
                ?? new CashItemCatalogDocument();

            _itemsBySerialNumber = document.Items
                .Select(static i => new CashItemDefinition(
                    i.SerialNumber,
                    i.ItemId,
                    i.Count,
                    i.Price,
                    i.PeriodDays,
                    i.Gender,
                    i.Class,
                    i.OnSale))
                .ToDictionary(static i => i.SerialNumber);
        }
    }

    public CashItemDefinition? GetBySerialNumber(int serialNumber)
        => _itemsBySerialNumber.GetValueOrDefault(serialNumber);

    private static JsonSerializerOptions CreateJsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

    private sealed class CashItemCatalogDocument
    {
        public List<CashItemDto> Items { get; init; } = new();
    }

    private sealed class CashItemDto
    {
        public int SerialNumber { get; init; }
        public int ItemId { get; init; }
        public short Count { get; init; } = 1;
        public int Price { get; init; }
        public int PeriodDays { get; init; }
        public byte Gender { get; init; } = 2;
        public int Class { get; init; } = -1;
        public bool OnSale { get; init; }
    }
}
