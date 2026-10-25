using System.Globalization;
using Maple.Core.Data;
using Maple.Core.Items;

namespace Maple.Application.Items;

/// <summary>
/// Item-use metadata backed by Item.wz.
/// </summary>
public sealed class WzItemUseCatalog : IItemUseCatalog, IItemEffectCatalog, IScrollCatalog
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

    /// <summary>
    /// P120：消耗品回復數值（對照 Java <c>MapleItemInformationProvider.getItemEffect</c> →
    /// <c>MapleStatEffect.loadItemEffectFromData(item/spec)</c> 的 hp/mp/hpR/mpR）。查無道具回 null；有道具但無 spec 回全 0。
    /// </summary>
    public ItemEffect? GetEffect(int itemId)
    {
        var node = GetItemNode(itemId);
        if (node is null)
        {
            return null;
        }

        var spec = node["spec"];
        return new ItemEffect(
            itemId,
            Hp: GetInt(spec, "hp") ?? 0,
            Mp: GetInt(spec, "mp") ?? 0,
            HpRate: GetInt(spec, "hpR") ?? 0,
            MpRate: GetInt(spec, "mpR") ?? 0);
    }

    /// <summary>
    /// P121：升級卷軸（204xxxx）數值（對照 Java <c>getEquipStats(scrollId)</c>：info 的 success、cursed、inc*）。
    /// 特殊卷軸（混沌、恢復、裝備強化、潛能、釘鞋/防寒）的專屬效果尚未移植，目前只取通用數值。
    /// </summary>
    public ScrollEffect? GetScroll(int scrollId)
    {
        if (scrollId / 10000 != 204)
        {
            return null;
        }

        var info = GetItemNode(scrollId)?["info"];
        if (info is null)
        {
            return null;
        }

        short Inc(string key) => (short)Math.Clamp(GetInt(info, "inc" + key) ?? 0, short.MinValue, short.MaxValue);

        return new ScrollEffect(
            scrollId,
            SuccessRate: GetInt(info, "success") ?? 0,
            CurseRate: GetInt(info, "cursed") ?? 0,
            Str: Inc("STR"),
            Dex: Inc("DEX"),
            Int: Inc("INT"),
            Luk: Inc("LUK"),
            Hp: Inc("MHP"),
            Mp: Inc("MMP"),
            Watk: Inc("PAD"),
            Matk: Inc("MAD"),
            Wdef: Inc("PDD"),
            Mdef: Inc("MDD"),
            Acc: Inc("ACC"),
            Avoid: Inc("EVA"),
            Speed: Inc("Speed"),
            Jump: Inc("Jump"));
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
