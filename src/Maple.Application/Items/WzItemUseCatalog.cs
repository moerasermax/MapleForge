using System.Globalization;
using Maple.Core.Data;
using Maple.Core.Items;

namespace Maple.Application.Items;

/// <summary>
/// Item-use metadata backed by Item.wz.
/// </summary>
public sealed class WzItemUseCatalog : IItemUseCatalog
{
    private readonly IDataProvider _data;

    public WzItemUseCatalog(IDataProvider data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
    }

    public int? GetReturnScrollDestinationMapId(int itemId)
    {
        var moveTo = GetInt(GetItemNode(itemId)?["spec"], "moveTo");
        return moveTo is null or -1 ? null : moveTo;
    }

    public IReadOnlyList<SummonBagMobEntry>? GetSummonBagMobs(int itemId)
    {
        var mobNode = GetItemNode(itemId)?["mob"];
        if (mobNode is null)
        {
            return null;
        }

        var entries = new List<SummonBagMobEntry>();
        foreach (var (_, entry) in mobNode.Children.OrderBy(static pair => SortKey(pair.Key)))
        {
            var mobId = GetInt(entry, "id");
            var probability = GetInt(entry, "prob");
            if (mobId is > 0 && probability is not null)
            {
                entries.Add(new SummonBagMobEntry(mobId.Value, probability.Value));
            }
        }

        return entries.Count == 0 ? null : entries;
    }

    private IDataNode? GetItemNode(int itemId)
    {
        if (itemId <= 0)
        {
            return null;
        }

        var itemRoot = GetItemRoot();
        if (itemRoot is null)
        {
            return null;
        }

        var itemIdText = itemId.ToString(CultureInfo.InvariantCulture);
        var wzItemIdText = "0" + itemIdText;
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

        return null;
    }

    private IDataNode? GetItemRoot()
    {
        try
        {
            return _data.GetRoot("Item");
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
    }

    private static int? GetInt(IDataNode? node, string key)
    {
        var value = node?[key]?.Value;
        return value switch
        {
            int v => v,
            short v => v,
            long v when v <= int.MaxValue && v >= int.MinValue => (int)v,
            byte v => v,
            sbyte v => v,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static int SortKey(string key) =>
        int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue;
}
