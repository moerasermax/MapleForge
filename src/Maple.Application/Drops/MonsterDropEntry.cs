namespace Maple.Application.Drops;

/// <summary>怪物掉落表列，對照 Java drop_data / MonsterDropEntry。</summary>
public sealed record MonsterDropEntry(
    int ItemId,
    int Chance,
    int MinimumQuantity,
    int MaximumQuantity,
    short QuestId = 0);

public interface IMonsterDropCatalog
{
    IReadOnlyList<MonsterDropEntry> RetrieveDrop(int monsterId);
}

public sealed class InMemoryMonsterDropCatalog : IMonsterDropCatalog
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<MonsterDropEntry>> _drops;

    public InMemoryMonsterDropCatalog(IReadOnlyDictionary<int, IReadOnlyList<MonsterDropEntry>> drops)
    {
        _drops = drops;
    }

    public IReadOnlyList<MonsterDropEntry> RetrieveDrop(int monsterId)
        => _drops.TryGetValue(monsterId, out var entries) ? entries : Array.Empty<MonsterDropEntry>();
}
