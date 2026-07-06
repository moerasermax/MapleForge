using System.Globalization;
using Maple.Core.Data;
using Maple.Core.Inventory;
using Maple.Core.Items;

namespace Maple.Content.Items;

public sealed class WzItemMakeCatalog : IItemMakeCatalog
{
    private readonly IDataProvider _data;
    private readonly object _recipeGate = new();
    private readonly object _itemGate = new();
    private ItemMakeRecipes? _recipes;
    private readonly Dictionary<int, IDataNode?> _itemNodeCache = new();
    private readonly Dictionary<int, Equip?> _equipCache = new();
    private readonly Dictionary<int, ItemMakeEnhanceStats?> _enhanceCache = new();

    public WzItemMakeCatalog(IDataProvider data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
    }

    public ItemMakeGemRecipe? GetGemRecipe(int itemId)
        => EnsureRecipes().GemRecipes.GetValueOrDefault(itemId);

    public ItemMakeCreateRecipe? GetCreateRecipe(int itemId)
        => EnsureRecipes().CreateRecipes.GetValueOrDefault(itemId);

    public int GetItemMakeLevel(int itemId)
        => GetInt(GetInfoNode(itemId), "itemMakeLevel", 0);

    public int GetRequiredLevel(int itemId)
        => GetInt(GetInfoNode(itemId), "reqLevel", 0);

    public bool IsDropRestricted(int itemId)
    {
        var flag = GetFlag(itemId);
        return (flag & 0x200) != 0 ||
               (flag & 0x400) != 0 ||
               itemId is 3012000 or 4030004 or 1052098 or 1052202;
    }

    public bool IsAccountShared(int itemId)
        => (GetFlag(itemId) & 0x100) != 0;

    public Equip? CreateEquip(int itemId)
    {
        lock (_itemGate)
        {
            if (!_equipCache.TryGetValue(itemId, out var cached))
            {
                cached = LoadEquip(itemId);
                _equipCache[itemId] = cached;
            }

            return cached?.Copy() as Equip;
        }
    }

    public ItemMakeEnhanceStats? GetEnhanceStats(int itemId)
    {
        lock (_itemGate)
        {
            if (!_enhanceCache.TryGetValue(itemId, out var cached))
            {
                cached = LoadEnhanceStats(itemId);
                _enhanceCache[itemId] = cached;
            }

            return cached;
        }
    }

    public int GemRecipeCount => EnsureRecipes().GemRecipes.Count;

    public int CreateRecipeCount => EnsureRecipes().CreateRecipes.Count;

    private ItemMakeRecipes EnsureRecipes()
    {
        if (_recipes is not null)
        {
            return _recipes;
        }

        lock (_recipeGate)
        {
            _recipes ??= LoadRecipes();
            return _recipes;
        }
    }

    private ItemMakeRecipes LoadRecipes()
    {
        var root = SafeGetAt("Etc", "ItemMake.img");
        if (root is null)
        {
            return new ItemMakeRecipes(
                new Dictionary<int, ItemMakeGemRecipe>(),
                new Dictionary<int, ItemMakeCreateRecipe>());
        }

        var gemRecipes = new Dictionary<int, ItemMakeGemRecipe>();
        var createRecipes = new Dictionary<int, ItemMakeCreateRecipe>();

        foreach (var dataType in OrderedChildren(root))
        {
            if (!TryParseInt(dataType.Name, out var type))
            {
                continue;
            }

            if (type == 0)
            {
                foreach (var itemFolder in OrderedChildren(dataType))
                {
                    if (TryLoadGemRecipe(itemFolder, out var recipe))
                    {
                        gemRecipes[recipe.ItemId] = recipe;
                    }
                }

                continue;
            }

            if (type is 1 or 2 or 4 or 8 or 16)
            {
                foreach (var itemFolder in OrderedChildren(dataType))
                {
                    if (TryLoadCreateRecipe(itemFolder, out var recipe))
                    {
                        createRecipes[recipe.ItemId] = recipe;
                    }
                }
            }
        }

        return new ItemMakeRecipes(gemRecipes, createRecipes);
    }

    private static bool TryLoadGemRecipe(IDataNode itemFolder, out ItemMakeGemRecipe recipe)
    {
        recipe = default!;
        if (!TryParseInt(itemFolder.Name, out var itemId))
        {
            return false;
        }

        recipe = new ItemMakeGemRecipe(
            itemId,
            GetInt(itemFolder, "meso", 0),
            GetInt(itemFolder, "reqLevel", 0),
            GetInt(itemFolder, "reqSkillLevel", 0),
            GetInt(itemFolder, "itemNum", 0),
            ReadIngredients(itemFolder["recipe"]),
            ReadRandomRewards(itemFolder["randomReward"]));
        return true;
    }

    private static bool TryLoadCreateRecipe(IDataNode itemFolder, out ItemMakeCreateRecipe recipe)
    {
        recipe = default!;
        if (!TryParseInt(itemFolder.Name, out var itemId))
        {
            return false;
        }

        recipe = new ItemMakeCreateRecipe(
            itemId,
            GetInt(itemFolder, "meso", 0),
            GetInt(itemFolder, "reqLevel", 0),
            GetInt(itemFolder, "reqSkillLevel", 0),
            GetInt(itemFolder, "itemNum", 0),
            GetInt(itemFolder, "tuc", 0),
            GetInt(itemFolder, "catalyst", 0),
            ReadIngredients(itemFolder["recipe"]));
        return true;
    }

    private static IReadOnlyList<ItemMakeIngredient> ReadIngredients(IDataNode? recipeNode)
    {
        if (recipeNode is null)
        {
            return Array.Empty<ItemMakeIngredient>();
        }

        var ingredients = new List<ItemMakeIngredient>();
        foreach (var child in OrderedChildren(recipeNode))
        {
            var itemId = GetInt(child, "item", 0);
            var count = GetInt(child, "count", 0);
            if (itemId > 0 && count > 0)
            {
                ingredients.Add(new ItemMakeIngredient(itemId, count));
            }
        }

        return ingredients;
    }

    private static IReadOnlyList<ItemMakeRandomReward> ReadRandomRewards(IDataNode? rewardNode)
    {
        if (rewardNode is null)
        {
            return Array.Empty<ItemMakeRandomReward>();
        }

        var rewards = new List<ItemMakeRandomReward>();
        foreach (var child in OrderedChildren(rewardNode))
        {
            var itemId = GetInt(child, "item", 0);
            var weight = GetInt(child, "prob", 0);
            if (itemId > 0 && weight > 0)
            {
                rewards.Add(new ItemMakeRandomReward(itemId, weight));
            }
        }

        return rewards;
    }

    private Equip? LoadEquip(int itemId)
    {
        var info = GetInfoNode(itemId);
        if (info is null)
        {
            return null;
        }

        return new Equip
        {
            ItemId = itemId,
            Quantity = 1,
            UpgradeSlots = ReadByte(info, "tuc"),
            Str = ReadShort(info, "STR", "incSTR"),
            Dex = ReadShort(info, "DEX", "incDEX"),
            Int = ReadShort(info, "INT", "incINT"),
            Luk = ReadShort(info, "LUK", "incLUK"),
            Hp = ReadShort(info, "MHP", "incMHP", "incMaxHP"),
            Mp = ReadShort(info, "MMP", "incMMP", "incMaxMP"),
            Watk = ReadShort(info, "PAD", "incPAD"),
            Matk = ReadShort(info, "MAD", "incMAD"),
            Wdef = ReadShort(info, "PDD", "incPDD"),
            Mdef = ReadShort(info, "MDD", "incMDD"),
            Acc = ReadShort(info, "ACC", "incACC"),
            Avoid = ReadShort(info, "EVA", "incEVA"),
            Hands = ReadShort(info, "Craft", "incCraft"),
            Speed = ReadShort(info, "Speed", "incSpeed"),
            Jump = ReadShort(info, "Jump", "incJump"),
        };
    }

    private ItemMakeEnhanceStats? LoadEnhanceStats(int itemId)
    {
        if (itemId / 10000 != 425)
        {
            return null;
        }

        var info = GetInfoNode(itemId);
        if (info is null)
        {
            return null;
        }

        return new ItemMakeEnhanceStats(
            ReadShort(info, "incPAD"),
            ReadShort(info, "incMAD"),
            ReadShort(info, "incACC"),
            ReadShort(info, "incEVA"),
            ReadShort(info, "incSpeed"),
            ReadShort(info, "incJump"),
            ReadShort(info, "incMaxHP", "incMHP"),
            ReadShort(info, "incMaxMP", "incMMP"),
            ReadShort(info, "incSTR"),
            ReadShort(info, "incDEX"),
            ReadShort(info, "incINT"),
            ReadShort(info, "incLUK"),
            ReadShort(info, "randOption"),
            ReadShort(info, "randStat"));
    }

    private IDataNode? GetInfoNode(int itemId)
        => GetItemNode(itemId)?["info"];

    private IDataNode? GetItemNode(int itemId)
    {
        if (itemId <= 0)
        {
            return null;
        }

        lock (_itemGate)
        {
            if (_itemNodeCache.TryGetValue(itemId, out var cached))
            {
                return cached;
            }

            var node = FindItemNode(itemId);
            _itemNodeCache[itemId] = node;
            return node;
        }
    }

    private IDataNode? FindItemNode(int itemId)
    {
        var itemIdText = itemId.ToString(CultureInfo.InvariantCulture);
        var wzItemIdText = "0" + itemIdText;

        var itemRoot = SafeGetRoot("Item");
        if (itemRoot is not null)
        {
            var itemFileName = wzItemIdText[..4] + ".img";
            foreach (var category in itemRoot.Children.Values)
            {
                var itemFile = category[itemFileName];
                var itemNode = itemFile?[wzItemIdText] ?? itemFile?[itemIdText];
                if (itemNode is not null)
                {
                    return itemNode;
                }
            }
        }

        var characterRoot = SafeGetRoot("Character");
        if (characterRoot is null)
        {
            return null;
        }

        var equipFileName = wzItemIdText + ".img";
        foreach (var category in characterRoot.Children.Values)
        {
            var itemNode = category[equipFileName];
            if (itemNode is not null)
            {
                return itemNode;
            }
        }

        return characterRoot[equipFileName];
    }

    private int GetFlag(int itemId)
    {
        var info = GetInfoNode(itemId);
        return GetInt(info, "flag", GetInt(info, "flags", 0));
    }

    private IDataNode? SafeGetRoot(string fileName)
    {
        try
        {
            return _data.GetRoot(fileName);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private IDataNode? SafeGetAt(string fileName, string path)
    {
        try
        {
            return _data.GetAt(fileName, path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static IEnumerable<IDataNode> OrderedChildren(IDataNode node)
        => node.Children
            .OrderBy(static pair => SortKey(pair.Key))
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Value);

    private static int SortKey(string key)
        => int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : int.MaxValue;

    private static bool TryParseInt(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static byte ReadByte(IDataNode? node, params string[] keys)
        => (byte)Math.Clamp(GetInt(node, keys, 0), byte.MinValue, byte.MaxValue);

    private static short ReadShort(IDataNode? node, params string[] keys)
        => (short)Math.Clamp(GetInt(node, keys, 0), short.MinValue, short.MaxValue);

    private static int GetInt(IDataNode? node, string key, int defaultValue)
        => GetInt(node, new[] { key }, defaultValue);

    private static int GetInt(IDataNode? node, string[] keys, int defaultValue)
    {
        if (node is null)
        {
            return defaultValue;
        }

        foreach (var key in keys)
        {
            var child = node[key];
            var parsed = child?.Value switch
            {
                int v => v,
                short v => v,
                long v when v >= int.MinValue && v <= int.MaxValue => (int)v,
                byte v => v,
                sbyte v => v,
                string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
                _ => (int?)null,
            };

            if (parsed is not null)
            {
                return parsed.Value;
            }
        }

        return defaultValue;
    }

    private sealed record ItemMakeRecipes(
        IReadOnlyDictionary<int, ItemMakeGemRecipe> GemRecipes,
        IReadOnlyDictionary<int, ItemMakeCreateRecipe> CreateRecipes);
}
