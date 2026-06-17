namespace Maple.Core.Items;

/// <summary>Version-neutral item-use metadata needed by item-use domain flows.</summary>
public interface IItemUseCatalog
{
    int? GetReturnScrollDestinationMapId(int itemId);

    IReadOnlyList<SummonBagMobEntry>? GetSummonBagMobs(int itemId);
}

public sealed record SummonBagMobEntry(int MobId, int Probability);
