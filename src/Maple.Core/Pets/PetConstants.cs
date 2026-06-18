namespace Maple.Core.Pets;

public static class PetConstants
{
    public const int MaxCloseness = 30000;
    public const int MaxFullness = 100;
    public const int FeedFullnessGain = 30;
    public const int MaxLevel = 30;

    public const int ItemPickupFlag = 0x01;
    public const int ExpandPickupFlag = 0x02;
    public const int AutoPickupFlag = 0x04;
    public const int UnpickableFlag = 0x08;
    public const int LeftoverPickupFlag = 0x10;
    public const int HpChargeFlag = 0x20;
    public const int MpChargeFlag = 0x40;
    public const int PetBuffFlag = 0x80;
    public const int PetDrawFlag = 0x100;
    public const int PetDialogueFlag = 0x200;

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
