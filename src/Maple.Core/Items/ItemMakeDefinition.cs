using Maple.Core.Inventory;

namespace Maple.Core.Items;

public sealed record ItemMakeIngredient(int ItemId, int Count);

public sealed record ItemMakeRandomReward(int ItemId, int Weight);

public sealed record ItemMakeGemRecipe(
    int ItemId,
    int Cost,
    int RequiredLevel,
    int RequiredMakerLevel,
    int RewardQuantity,
    IReadOnlyList<ItemMakeIngredient> Ingredients,
    IReadOnlyList<ItemMakeRandomReward> RandomRewards);

public sealed record ItemMakeCreateRecipe(
    int ItemId,
    int Cost,
    int RequiredLevel,
    int RequiredMakerLevel,
    int RewardQuantity,
    int UpgradeSlots,
    int StimulatorItemId,
    IReadOnlyList<ItemMakeIngredient> Ingredients);

public sealed record ItemMakeEnhanceStats(
    short Watk,
    short Matk,
    short Acc,
    short Avoid,
    short Speed,
    short Jump,
    short Hp,
    short Mp,
    short Str,
    short Dex,
    short Int,
    short Luk,
    short RandomOption,
    short RandomStat);

public interface IItemMakeCatalog
{
    ItemMakeGemRecipe? GetGemRecipe(int itemId);

    ItemMakeCreateRecipe? GetCreateRecipe(int itemId);

    int GetItemMakeLevel(int itemId);

    int GetRequiredLevel(int itemId);

    bool IsDropRestricted(int itemId);

    bool IsAccountShared(int itemId);

    /// <summary>P213：Java <c>isPickupRestricted</c>（唯一道具：WZ 旗標 0x80 或 4030003/4030004，排除 4001168/4031306/4031307）。</summary>
    bool IsPickupRestricted(int itemId) => false;

    Equip? CreateEquip(int itemId);

    ItemMakeEnhanceStats? GetEnhanceStats(int itemId);

    int GemRecipeCount { get; }

    int CreateRecipeCount { get; }
}
