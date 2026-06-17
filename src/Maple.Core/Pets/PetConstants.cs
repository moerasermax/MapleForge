namespace Maple.Core.Pets;

public static class PetConstants
{
    public const int MaxCloseness = 30000;
    public const int MaxFullness = 100;
    public const int FeedFullnessGain = 30;
    public const int MaxLevel = 30;

    public const int ItemPickupFlag = 0x01;
    public const int UnpickableFlag = 0x08;

    public static int GetClosenessNeededForLevel(int level)
    {
        ReadOnlySpan<int> table =
        [
            0, 1, 3, 6, 14, 31, 60, 108, 181, 287,
            434, 632, 891, 1224, 1642, 2161, 2793, 3557, 4467, 5542,
            6801, 8263, 9950, 11882, 14084, 16578, 19391, 22547, 26074, 30000,
        ];

        if (level <= 1)
        {
            return table[0];
        }

        if (level >= MaxLevel)
        {
            return table[^1];
        }

        return table[level - 1];
    }
}
