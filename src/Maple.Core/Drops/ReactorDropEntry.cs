namespace Maple.Core.Drops;

/// <summary>P182：Java <c>ReactorDropEntry</c>（reactordrops 表：itemid、chance＝1/chance 機率、questid）。</summary>
public sealed record ReactorDropEntry(int ItemId, int Chance, int QuestId);

/// <summary>P182：Java <c>ReactorScriptManager.getDrops(reactorId)</c>。</summary>
public interface IReactorDropCatalog
{
    IReadOnlyList<ReactorDropEntry> GetDrops(int reactorId);
}

public sealed class InMemoryReactorDropCatalog : IReactorDropCatalog
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<ReactorDropEntry>> _drops;

    public InMemoryReactorDropCatalog(IReadOnlyDictionary<int, IReadOnlyList<ReactorDropEntry>> drops) => _drops = drops;

    public IReadOnlyList<ReactorDropEntry> GetDrops(int reactorId)
        => _drops.TryGetValue(reactorId, out var entries) ? entries : Array.Empty<ReactorDropEntry>();
}
