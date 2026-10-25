namespace Maple.Core.Items;

/// <summary>Version-neutral item-use metadata needed by item-use domain flows.</summary>
public interface IItemUseCatalog
{
    int? GetReturnScrollDestinationMapId(int itemId);

    IReadOnlyList<SummonBagMobEntry>? GetSummonBagMobs(int itemId);

    /// <summary>P180：Java <c>MapleItemInformationProvider.getRewardItem</c>（道具 reward 節點＋totalprob）；無則 null。</summary>
    RewardItemTable? GetRewardItems(int itemId) => null;
}

public sealed record SummonBagMobEntry(int MobId, int Probability);

/// <summary>P180：Java <c>StructRewardItem</c>（item／prob／count／period(小時)／Effect／worldMsg）。</summary>
public sealed record RewardItemEntry(int ItemId, int Prob, short Quantity, int Period, string Effect, string? WorldMessage);

/// <summary>P180：道具的獎勵表（Java <c>Pair&lt;totalprob, List&lt;StructRewardItem&gt;&gt;</c>）。</summary>
public sealed record RewardItemTable(int TotalProb, IReadOnlyList<RewardItemEntry> Entries)
{
    /// <summary>
    /// Java UseRewardItem 抽獎：反覆依序掃描，每項 <c>prob &gt; 0 &amp;&amp; nextInt(totalprob) &lt; prob</c> 即中。
    /// 需 <see cref="TotalProb"/> &gt; 0 且至少一項 prob &gt; 0（否則回 null，Java 會無限迴圈）。
    /// </summary>
    public RewardItemEntry? Roll(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (TotalProb <= 0 || !Entries.Any(static e => e.Prob > 0))
        {
            return null;
        }

        while (true)
        {
            foreach (var entry in Entries)
            {
                if (entry.Prob > 0 && random.Next(TotalProb) < entry.Prob)
                {
                    return entry;
                }
            }
        }
    }
}
