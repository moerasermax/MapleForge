using System.Text.Json;
using Maple.Core.Drops;

namespace Maple.Content.Drops;

/// <summary>
/// P119：怪物掉落表（對照 Java <c>MapleMonsterInformationProvider.retrieveDrop</c> 的 <c>drop_data</c> 表）。
/// 資料檔不進版控（遊戲資料，同 WZ）：由 <c>tools/drop-data/convert_drop_data.py</c> 從 Java SQL dump 轉出，
/// 格式 <c>{"drops": {"&lt;dropperId&gt;": [[itemId, chance, min, max, questId], ...]}}</c>。
/// </summary>
public sealed class JsonMonsterDropCatalog : IMonsterDropCatalog
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<MonsterDropEntry>> _drops;

    public JsonMonsterDropCatalog(string path)
        : this(File.OpenRead(path))
    {
    }

    public JsonMonsterDropCatalog(Stream stream)
    {
        using (stream)
        {
            var document = JsonSerializer.Deserialize<DropDocument>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new DropDocument();

            var drops = new Dictionary<int, IReadOnlyList<MonsterDropEntry>>();
            foreach (var (key, rows) in document.Drops)
            {
                if (!int.TryParse(key, out var dropperId))
                {
                    continue;
                }

                drops[dropperId] = rows
                    .Where(static r => r.Length >= 4)
                    .Select(static r => new MonsterDropEntry(
                        ItemId: r[0],
                        Chance: r[1],
                        MinimumQuantity: r[2],
                        MaximumQuantity: r[3],
                        QuestId: r.Length > 4 ? (short)r[4] : (short)0))
                    .ToArray();
            }

            _drops = drops;
        }
    }

    public int MonsterCount => _drops.Count;

    public IReadOnlyList<MonsterDropEntry> RetrieveDrop(int monsterId)
        => _drops.TryGetValue(monsterId, out var entries) ? entries : Array.Empty<MonsterDropEntry>();

    private sealed class DropDocument
    {
        public Dictionary<string, int[][]> Drops { get; set; } = new();
    }
}
