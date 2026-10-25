using System.Globalization;
using Maple.Core.Data;
using Maple.Core.Items;
using Maple.Core.World;

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
            MpRate: GetInt(spec, "mpR") ?? 0,
            Cures: GetCures(spec));
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
            Jump: Inc("Jump"),
            RequiredItems: GetRequiredItems(GetItemNode(scrollId)?["req"]));
    }

    /// <summary>P154：卷軸節點的 req/{i}（int 裝備 ID）。</summary>
    private static IReadOnlyList<int>? GetRequiredItems(IDataNode? req)
    {
        if (req is null)
        {
            return null;
        }

        var ids = req.Children.Values.Select(static c => c.Value is int i ? i : 0).Where(static i => i > 0).ToArray();
        return ids.Length > 0 ? ids : null;
    }

    /// <summary>P127：Java <c>loadFromData</c> 的 cure 清單（poison、seal、darkness、weakness、curse 依序）。</summary>
    private static IReadOnlyList<PlayerDisease>? GetCures(IDataNode? spec)
    {
        var cures = new List<PlayerDisease>(5);
        if (GetInt(spec, "poison") > 0) cures.Add(PlayerDisease.Poison);
        if (GetInt(spec, "seal") > 0) cures.Add(PlayerDisease.Seal);
        if (GetInt(spec, "darkness") > 0) cures.Add(PlayerDisease.Darkness);
        if (GetInt(spec, "weakness") > 0) cures.Add(PlayerDisease.Weaken);
        if (GetInt(spec, "curse") > 0) cures.Add(PlayerDisease.Curse);
        return cures.Count > 0 ? cures : null;
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
