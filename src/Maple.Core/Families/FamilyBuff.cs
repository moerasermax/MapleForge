namespace Maple.Core.Families;

public sealed record FamilyBuffEntry(int Type, int RepCost, string BuffType, int Duration);

public static class FamilyBuff
{
    private static readonly FamilyBuffEntry[] Catalog =
    [
        new(0, 0, "teleport", 0),
        new(1, 0, "summon", 0),
        new(2, 300, "drop_50", 15),
        new(3, 500, "exp_50", 15),
        new(4, 700, "pedigree_drop_exp_100", 30),
        new(5, 800, "drop_100", 15),
        new(6, 1000, "exp_100", 15),
        new(7, 1200, "drop_100", 30),
        new(8, 1500, "exp_100", 30),
        new(9, 2000, "party_drop_100", 30),
        new(10, 2500, "party_exp_100", 30),
    ];

    public static IReadOnlyList<FamilyBuffEntry> All => Catalog;

    public static FamilyBuffEntry? Find(int type) =>
        Catalog.FirstOrDefault(entry => entry.Type == type);
}
