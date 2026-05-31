using Maple.Core.Data;
using System.Collections.ObjectModel;

namespace Maple.Content.Wz;

/// <summary>
/// 將 WzObject（Directory/Image/Property）包裝成 IDataNode。
/// 作為 Core 與 WZ 具體格式之間的 adapter。
/// </summary>
internal sealed class WzDataNode : IDataNode
{
    private readonly WzObject _wz;
    private IReadOnlyDictionary<string, IDataNode>? _children;

    internal WzDataNode(WzObject wz)
    {
        _wz = wz;
    }

    public string Name => _wz.Name;

    public object? Value => _wz switch
    {
        WzProperty p when p.Value is not Dictionary<string, WzProperty> => p.Value,
        _ => null
    };

    public IReadOnlyDictionary<string, IDataNode> Children
    {
        get
        {
            if (_children is not null)
            {
                return _children;
            }

            _children = _wz switch
            {
                WzDirectory dir => AdaptChildren(dir.Children),
                WzImage img => AdaptProperties(img.Properties),
                WzProperty { Value: Dictionary<string, WzProperty> sub } => AdaptProperties(sub),
                _ => ReadOnlyDictionary<string, IDataNode>.Empty
            };

            return _children;
        }
    }

    public IDataNode? this[string name] => Children.TryGetValue(name, out var node) ? node : null;

    private static IReadOnlyDictionary<string, IDataNode> AdaptChildren(
        IReadOnlyDictionary<string, WzObject> source)
    {
        var dict = new Dictionary<string, IDataNode>(source.Count, StringComparer.Ordinal);
        foreach (var (key, val) in source)
        {
            dict[key] = new WzDataNode(val);
        }

        return new ReadOnlyDictionary<string, IDataNode>(dict);
    }

    private static IReadOnlyDictionary<string, IDataNode> AdaptProperties(
        IReadOnlyDictionary<string, WzProperty> source)
    {
        var dict = new Dictionary<string, IDataNode>(source.Count, StringComparer.Ordinal);
        foreach (var (key, val) in source)
        {
            dict[key] = new WzDataNode(val);
        }

        return new ReadOnlyDictionary<string, IDataNode>(dict);
    }
}
