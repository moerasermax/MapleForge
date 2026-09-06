using System.Text.Json;
using Maple.Content.Wz;

if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: Maple.Tools.SkillBookExtractor <wz-directory> [output-json] [--inspect <itemId>]");
    return args.Length == 0 ? 1 : 0;
}

var wzDirectory = args[0];
var outputPath = args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal)
    ? args[1]
    : Path.Combine("src", "Maple.Content", "Skills", "minimal-skill-books.v113.json");

int? inspectItemId = null;
for (var i = 1; i < args.Length; i++)
{
    if (args[i] == "--inspect" && i + 1 < args.Length && int.TryParse(args[i + 1], out var itemId))
    {
        inspectItemId = itemId;
        i++;
    }
}

var itemWzPath = Path.Combine(wzDirectory, "Item.wz");
if (!File.Exists(itemWzPath))
{
    Console.Error.WriteLine($"Item.wz not found: {itemWzPath}");
    return 2;
}

using var wz = WzFile.Open(itemWzPath);
var extractor = new SkillBookExtractor(wz.Root);
var items = extractor.Extract().OrderBy(static i => i.ItemId).ToArray();

if (inspectItemId is { } inspect)
{
    var item = items.SingleOrDefault(i => i.ItemId == inspect);
    Console.Error.WriteLine(item is null
        ? $"inspect: {inspect} not found"
        : $"inspect: {JsonSerializer.Serialize(item, JsonOptions())}");
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
var document = new SkillBookCatalogDocument(items);
File.WriteAllText(outputPath, JsonSerializer.Serialize(document, JsonOptions()) + Environment.NewLine);

var mastery = items.Count(static i => i.ItemId / 10000 == 228);
var skill = items.Count(static i => i.ItemId / 10000 == 229);
var cygnus = items.Count(static i => i.ItemId / 10000 == 562);
Console.WriteLine($"Wrote {items.Length} skill-book entries to {outputPath} (228x={mastery}, 229x={skill}, 562x={cygnus}).");
return 0;

static JsonSerializerOptions JsonOptions()
    => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

internal sealed record SkillBookCatalogDocument(IReadOnlyList<SkillBookDto> Items);

internal sealed record SkillBookDto(
    int ItemId,
    int[] SkillIds,
    int SuccessRate,
    int ReqSkillLevel,
    int MasterLevel);

internal sealed class SkillBookExtractor
{
    private readonly WzDirectory _root;

    public SkillBookExtractor(WzDirectory root)
    {
        _root = root;
    }

    public IEnumerable<SkillBookDto> Extract()
    {
        // 對照 Java MapleItemInformationProvider.getSkillStats：228/229/562 共用同一套
        // skill-book schema（skillid 清單 + masterLevel + reqSkillLevel + success）。
        // 562x 是 Cygnus 五轉（聖魂劍士/烈焰巫師/破風使者/暗夜行者/閃雷悍將）技能書。
        foreach (var itemIdPrefix in new[] { "0228", "0229", "0562" })
        {
            foreach (var image in FindImages(itemIdPrefix + ".img"))
            {
                foreach (var item in ExtractImage(image, itemIdPrefix))
                {
                    yield return item;
                }
            }
        }
    }

    private IEnumerable<WzImage> FindImages(string imageName)
    {
        foreach (var child in _root.Children.Values)
        {
            if (child is WzDirectory directory)
            {
                if (directory.Children.TryGetValue(imageName, out var imageObject) && imageObject is WzImage image)
                {
                    yield return image;
                }
            }
            else if (child is WzImage image && string.Equals(image.Name, imageName, StringComparison.Ordinal))
            {
                yield return image;
            }
        }
    }

    private static IEnumerable<SkillBookDto> ExtractImage(WzImage image, string itemIdPrefix)
    {
        var items = image.Properties.TryGetValue(image.Name, out var rootProperty)
                    && rootProperty.Value is Dictionary<string, WzProperty> wrappedItems
            ? wrappedItems
            : image.Properties;

        foreach (var (nodeName, itemProperty) in items.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            if (!nodeName.StartsWith('0') || !int.TryParse(nodeName, out var itemId) || itemId / 10000 is not (228 or 229 or 562))
            {
                continue;
            }

            if (!nodeName.StartsWith(itemIdPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (itemProperty.Value is not Dictionary<string, WzProperty> itemNode
                || !itemNode.TryGetValue("info", out var infoProperty)
                || infoProperty.Value is not Dictionary<string, WzProperty> info)
            {
                continue;
            }

            if (!info.TryGetValue("skill", out var skillProperty)
                || skillProperty.Value is not Dictionary<string, WzProperty> skillNode)
            {
                continue;
            }

            var skillIds = skillNode
                .OrderBy(static p => int.TryParse(p.Key, out var n) ? n : int.MaxValue)
                .Select(static p => ToInt(p.Value.Value))
                .Where(static skillId => skillId > 0)
                .ToArray();

            if (skillIds.Length == 0)
            {
                continue;
            }

            yield return new SkillBookDto(
                itemId,
                skillIds,
                GetInt(info, "success"),
                GetInt(info, "reqSkillLevel"),
                GetInt(info, "masterLevel"));
        }
    }

    private static int GetInt(IReadOnlyDictionary<string, WzProperty> properties, string name)
        => properties.TryGetValue(name, out var property) ? ToInt(property.Value) : 0;

    private static int ToInt(object? value)
        => value switch
        {
            byte b => b,
            sbyte b => b,
            short s => s,
            ushort s => s,
            int i => i,
            uint i => checked((int)i),
            long l => checked((int)l),
            string s when int.TryParse(s, out var i) => i,
            _ => 0,
        };
}
