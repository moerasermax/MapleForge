using System.Text.Json;
using Maple.Core.Drops;

namespace Maple.Content.Drops;

/// <summary>
/// P182：反應堆掉落表（對照 Java <c>ReactorScriptManager.getDrops</c> 的 <c>reactordrops</c> 表）。
/// 資料檔不進版控：由 <c>tools/drop-data/convert_reactor_drops.py</c> 從 Java SQL dump 轉出，
/// 格式 <c>{"drops": {"&lt;reactorId&gt;": [[itemId, chance, questId], ...]}}</c>（列順序＝表的 id 順序）。
/// </summary>
public sealed class JsonReactorDropCatalog : IReactorDropCatalog
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<ReactorDropEntry>> _drops;

    public JsonReactorDropCatalog(string path)
        : this(File.OpenRead(path))
    {
    }

    public JsonReactorDropCatalog(Stream stream)
    {
        using (stream)
        {
            var document = JsonSerializer.Deserialize<DropDocument>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new DropDocument();

            var drops = new Dictionary<int, IReadOnlyList<ReactorDropEntry>>();
            foreach (var (key, rows) in document.Drops)
            {
                if (int.TryParse(key, out var reactorId))
                {
                    drops[reactorId] = rows
                        .Where(static r => r.Length >= 2)
                        .Select(static r => new ReactorDropEntry(r[0], r[1], r.Length > 2 ? r[2] : -1))
                        .ToArray();
                }
            }

            _drops = drops;
        }
    }

    public int ReactorCount => _drops.Count;

    public IReadOnlyList<ReactorDropEntry> GetDrops(int reactorId)
        => _drops.TryGetValue(reactorId, out var entries) ? entries : Array.Empty<ReactorDropEntry>();

    private sealed class DropDocument
    {
        public Dictionary<string, int[][]> Drops { get; set; } = new();
    }
}
