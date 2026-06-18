using System.Text.Json;
using Maple.Core.Skills;

namespace Maple.Content.Skills;

public sealed class JsonSkillBookCatalog : ISkillBookCatalog
{
    private readonly IReadOnlyDictionary<int, SkillBookDefinition> _booksByItemId;

    public JsonSkillBookCatalog(string path)
        : this(File.OpenRead(path))
    {
    }

    public JsonSkillBookCatalog(Stream stream)
    {
        using (stream)
        {
            var document = JsonSerializer.Deserialize<SkillBookCatalogDocument>(stream, CreateJsonOptions())
                ?? new SkillBookCatalogDocument();

            _booksByItemId = document.Items
                .Select(static i => new SkillBookDefinition(
                    i.ItemId,
                    i.SkillIds,
                    i.SuccessRate,
                    i.ReqSkillLevel,
                    i.MasterLevel))
                .ToDictionary(static i => i.ItemId);
        }
    }

    public SkillBookDefinition? GetByItemId(int itemId)
        => _booksByItemId.GetValueOrDefault(itemId);

    private static JsonSerializerOptions CreateJsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

    private sealed class SkillBookCatalogDocument
    {
        public List<SkillBookDto> Items { get; init; } = new();
    }

    private sealed class SkillBookDto
    {
        public int ItemId { get; init; }
        public int[] SkillIds { get; init; } = [];
        public int SuccessRate { get; init; }
        public int ReqSkillLevel { get; init; }
        public int MasterLevel { get; init; }
    }
}
