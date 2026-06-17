using Maple.Core.Inventory;

namespace Maple.Core.World;

public sealed class PlayerMountState
{
    public PlayerMountState(int itemId, int skillId, int level, int exp, int fatigue)
    {
        ItemId = itemId;
        SkillId = skillId;
        Level = Math.Clamp(level, 1, 30);
        Exp = Math.Max(0, exp);
        Fatigue = Math.Clamp(fatigue, 0, 100);
    }

    public int ItemId { get; private set; }

    public int SkillId { get; private set; }

    public int Level { get; private set; }

    public int Exp { get; private set; }

    public int Fatigue { get; private set; }

    public void SetItemId(int itemId) => ItemId = itemId;

    public void SetSkillId(int skillId) => SkillId = skillId;

    public void ReduceFatigue(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Fatigue = Math.Max(0, Fatigue - amount);
    }

    public bool AddFoodExperience(int amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        Exp += amount;
        if (Level < 30 && Exp >= MountExpNeededForLevel(Level + 1))
        {
            Level++;
            return true;
        }

        return false;
    }

    public static int MountExpNeededForLevel(int level) => level switch
    {
        <= 1 => 0,
        2 => 6,
        3 => 25,
        4 => 50,
        5 => 105,
        6 => 134,
        7 => 196,
        8 => 254,
        9 => 263,
        10 => 315,
        11 => 367,
        12 => 430,
        13 => 543,
        14 => 587,
        15 => 679,
        16 => 725,
        17 => 897,
        18 => 1146,
        19 => 1394,
        20 => 1701,
        21 => 2247,
        22 => 2543,
        23 => 2898,
        24 => 3156,
        25 => 3313,
        26 => 3584,
        27 => 3923,
        28 => 4150,
        29 => 4305,
        _ => 4550,
    };
}

public sealed record PlayerMountFoodResult(
    bool Applied,
    bool LevelUp,
    int PreviousFatigue,
    InventoryQuantityMutation? ConsumedItem);

public sealed partial class Player
{
    public PlayerMountState? Mount { get; private set; }

    public void SetMount(PlayerMountState? mount) => Mount = mount;

    public PlayerMountFoodResult UseMountFood(short slot, int itemId, int expGain)
    {
        if (Mount is null)
        {
            return new PlayerMountFoodResult(false, false, 0, null);
        }

        var item = Inventory.By(InventoryType.Use).Get(slot);
        if (item is null || item.ItemId != itemId || item.Quantity <= 0)
        {
            return new PlayerMountFoodResult(false, false, 0, null);
        }

        var previousFatigue = Mount.Fatigue;
        Mount.ReduceFatigue(30);
        var levelUp = previousFatigue > 0 && Mount.AddFoodExperience(expGain);

        if (!TryConsumeInventoryItem(InventoryType.Use, slot, itemId, 1, out var mutation))
        {
            return new PlayerMountFoodResult(false, false, previousFatigue, null);
        }

        FlushInventory();
        return new PlayerMountFoodResult(true, levelUp, previousFatigue, mutation);
    }
}
