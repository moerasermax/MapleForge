using System.Security.Cryptography;
using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.World;

namespace Maple.Application.Items;

public enum ItemMakerRequestKind
{
    CreateItem,
    CreateCrystal,
    DisassembleEquip,
}

public sealed record ItemMakerRequest(
    ItemMakerRequestKind Kind,
    int ItemId,
    bool UseStimulator = false,
    IReadOnlyList<int>? EnchanterItemIds = null,
    int Tick = 0,
    short Slot = 0)
{
    public static ItemMakerRequest CreateItem(int itemId, bool useStimulator, IReadOnlyList<int> enchanterItemIds)
        => new(ItemMakerRequestKind.CreateItem, itemId, useStimulator, enchanterItemIds);

    public static ItemMakerRequest CreateCrystal(int itemId)
        => new(ItemMakerRequestKind.CreateCrystal, itemId);

    public static ItemMakerRequest DisassembleEquip(int itemId, int tick, short slot)
        => new(ItemMakerRequestKind.DisassembleEquip, itemId, Tick: tick, Slot: slot);
}

public enum ItemMakerStatus
{
    Success,
    InvalidRequest,
    RecipeNotFound,
    SkillLevelTooLow,
    NotEnoughMeso,
    InventoryFull,
    MissingMaterials,
    TooManyEnchanters,
    RestrictedItem,
}

public sealed record ItemMakerResult(
    ItemMakerStatus Status,
    bool CharacterMutated = false,
    int? CreatedItemId = null,
    InventoryType? CreatedInventoryType = null,
    Item? CreatedItem = null,
    IReadOnlyList<InventoryQuantityMutation>? InventoryMutations = null,
    bool MesoChanged = false,
    int Meso = 0)
{
    public bool Success => Status == ItemMakerStatus.Success;

    public IReadOnlyList<InventoryQuantityMutation> Mutations => InventoryMutations ?? Array.Empty<InventoryQuantityMutation>();

    public static ItemMakerResult Failure(ItemMakerStatus status, int meso = 0)
        => new(status, Meso: meso);
}

public interface IItemMakerRandomSource
{
    int NextInt(int exclusiveMax);

    int NextInclusive(int minInclusive, int maxInclusive);

    bool NextBool();
}

public sealed class ItemMakerRandomSource : IItemMakerRandomSource
{
    public int NextInt(int exclusiveMax)
        => exclusiveMax <= 1 ? 0 : RandomNumberGenerator.GetInt32(exclusiveMax);

    public int NextInclusive(int minInclusive, int maxInclusive)
    {
        if (maxInclusive <= minInclusive)
        {
            return minInclusive;
        }

        return RandomNumberGenerator.GetInt32(minInclusive, maxInclusive + 1);
    }

    public bool NextBool() => RandomNumberGenerator.GetInt32(2) == 0;
}

public sealed class ItemMakerService
{
    private const int BeginnerMakerSkillId = 1007;
    private const int CygnusMakerSkillId = 10001007;
    private const int AranMakerSkillId = 20001007;

    private readonly IItemMakeCatalog _catalog;
    private readonly IItemMakerRandomSource _random;

    public ItemMakerService(IItemMakeCatalog catalog, IItemMakerRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(random);
        _catalog = catalog;
        _random = random;
    }

    public ItemMakerResult Handle(Player player, ItemMakerRequest request)
    {
        ArgumentNullException.ThrowIfNull(player);

        return request.Kind switch
        {
            ItemMakerRequestKind.CreateItem => CreateItem(player, request),
            ItemMakerRequestKind.CreateCrystal => CreateCrystal(player, request.ItemId),
            ItemMakerRequestKind.DisassembleEquip => DisassembleEquip(player, request.ItemId, request.Slot),
            _ => ItemMakerResult.Failure(ItemMakerStatus.InvalidRequest, player.Character.Meso),
        };
    }

    private ItemMakerResult CreateItem(Player player, ItemMakerRequest request)
    {
        if (IsGem(request.ItemId))
        {
            return CreateGem(player, request.ItemId);
        }

        if (IsOtherGem(request.ItemId))
        {
            return CreateOtherGem(player, request.ItemId);
        }

        return CreateEquip(player, request);
    }

    private ItemMakerResult CreateGem(Player player, int itemId)
    {
        var recipe = _catalog.GetGemRecipe(itemId);
        if (recipe is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.RecipeNotFound, player.Character.Meso);
        }

        if (!HasMakerSkill(player, recipe.RequiredMakerLevel))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.SkillLevelTooLow, player.Character.Meso);
        }

        if (player.Character.Meso < recipe.Cost)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.NotEnoughMeso, player.Character.Meso);
        }

        var rewardItemId = SelectWeightedReward(recipe.RandomRewards);
        if (rewardItemId <= 0)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.RecipeNotFound, player.Character.Meso);
        }

        var rewardType = Player.InventoryTypeOf(rewardItemId);
        if (!player.CanGainItem(rewardType))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        if (!HasIngredients(player, recipe.Ingredients))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.MissingMaterials, player.Character.Meso);
        }

        var mutations = ConsumeIngredients(player, recipe.Ingredients);
        if (mutations.Count == 0 && recipe.Ingredients.Count > 0)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.MissingMaterials, player.Character.Meso);
        }

        player.GainMeso(-recipe.Cost);
        var rewardQuantity = (short)(recipe.Ingredients.Count > 0 &&
                                     recipe.Ingredients[^1].ItemId == rewardItemId
            ? 9
            : 1);
        var created = GainItem(player, rewardType, rewardItemId, rewardQuantity);
        if (created is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        player.FlushInventory();
        return Success(player, rewardItemId, rewardType, created, mutations, recipe.Cost > 0);
    }

    private ItemMakerResult CreateOtherGem(Player player, int itemId)
    {
        var recipe = _catalog.GetGemRecipe(itemId);
        if (recipe is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.RecipeNotFound, player.Character.Meso);
        }

        if (!HasMakerSkill(player, recipe.RequiredMakerLevel))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.SkillLevelTooLow, player.Character.Meso);
        }

        if (player.Character.Meso < recipe.Cost)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.NotEnoughMeso, player.Character.Meso);
        }

        var type = Player.InventoryTypeOf(itemId);
        if (!player.CanGainItem(type))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        if (!HasIngredients(player, recipe.Ingredients))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.MissingMaterials, player.Character.Meso);
        }

        var mutations = ConsumeIngredients(player, recipe.Ingredients);
        if (mutations.Count == 0 && recipe.Ingredients.Count > 0)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.MissingMaterials, player.Character.Meso);
        }

        player.GainMeso(-recipe.Cost);
        var created = type == InventoryType.Equip
            ? GainEquip(player, itemId)
            : GainItem(player, type, itemId, 1);
        if (created is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        player.FlushInventory();
        return Success(player, itemId, type, created, mutations, recipe.Cost > 0);
    }

    private ItemMakerResult CreateEquip(Player player, ItemMakerRequest request)
    {
        var recipe = _catalog.GetCreateRecipe(request.ItemId);
        if (recipe is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.RecipeNotFound, player.Character.Meso);
        }

        var enchanters = request.EnchanterItemIds ?? Array.Empty<int>();
        if (enchanters.Count > recipe.UpgradeSlots)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.TooManyEnchanters, player.Character.Meso);
        }

        if (!HasMakerSkill(player, recipe.RequiredMakerLevel))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.SkillLevelTooLow, player.Character.Meso);
        }

        if (player.Character.Meso < recipe.Cost)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.NotEnoughMeso, player.Character.Meso);
        }

        if (!player.CanGainItem(InventoryType.Equip))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        if (!HasIngredients(player, recipe.Ingredients))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.MissingMaterials, player.Character.Meso);
        }

        var mutations = ConsumeIngredients(player, recipe.Ingredients);
        if (mutations.Count == 0 && recipe.Ingredients.Count > 0)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.MissingMaterials, player.Character.Meso);
        }

        player.GainMeso(-recipe.Cost);
        var equip = _catalog.CreateEquip(request.ItemId) ?? new Equip { ItemId = request.ItemId, Quantity = 1 };

        if (request.UseStimulator &&
            recipe.StimulatorItemId > 0 &&
            player.Inventory.By(InventoryType.Etc).CountById(recipe.StimulatorItemId) > 0)
        {
            RandomizeStats(equip);
            if (player.TryConsumeItemById(InventoryType.Etc, recipe.StimulatorItemId, 1, out var stimulatorMutations))
            {
                mutations.AddRange(stimulatorMutations);
            }
        }

        foreach (var enchanterItemId in enchanters)
        {
            if (enchanterItemId <= 0 ||
                player.Inventory.By(InventoryType.Etc).CountById(enchanterItemId) <= 0)
            {
                continue;
            }

            var stats = _catalog.GetEnhanceStats(enchanterItemId);
            if (stats is null)
            {
                continue;
            }

            ApplyEnhanceStats(equip, stats);
            if (player.TryConsumeItemById(InventoryType.Etc, enchanterItemId, 1, out var enchanterMutations))
            {
                mutations.AddRange(enchanterMutations);
            }
        }

        var created = player.Inventory.By(InventoryType.Equip).Gain(equip);
        if (created is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        player.FlushInventory();
        return Success(player, request.ItemId, InventoryType.Equip, created, mutations, recipe.Cost > 0);
    }

    private ItemMakerResult CreateCrystal(Player player, int sourceItemId)
    {
        var level = _catalog.GetItemMakeLevel(sourceItemId);
        var crystalItemId = CrystalForLevel(level);
        if (crystalItemId <= 0)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.RecipeNotFound, player.Character.Meso);
        }

        if (!player.CanGainItem(InventoryType.Etc))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        if (player.Inventory.By(InventoryType.Etc).CountById(sourceItemId) < 100)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.MissingMaterials, player.Character.Meso);
        }

        if (!player.TryConsumeItemById(InventoryType.Etc, sourceItemId, 100, out var mutations))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.MissingMaterials, player.Character.Meso);
        }

        var created = GainItem(player, InventoryType.Etc, crystalItemId, 1);
        if (created is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        player.FlushInventory();
        return Success(player, crystalItemId, InventoryType.Etc, created, mutations.ToList(), mesoChanged: false);
    }

    private ItemMakerResult DisassembleEquip(Player player, int itemId, short slot)
    {
        var equip = player.Inventory.By(InventoryType.Equip).Get(slot);
        if (equip is null || equip.ItemId != itemId || equip.Quantity < 1)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InvalidRequest, player.Character.Meso);
        }

        if (_catalog.IsDropRestricted(itemId) || _catalog.IsAccountShared(itemId))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.RestrictedItem, player.Character.Meso);
        }

        var crystalItemId = CrystalForLevel(_catalog.GetRequiredLevel(itemId));
        if (crystalItemId <= 0)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.RecipeNotFound, player.Character.Meso);
        }

        if (!player.CanGainItem(InventoryType.Etc))
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        if (!player.TryConsumeInventoryItem(InventoryType.Equip, slot, itemId, 1, out var mutation) || mutation is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InvalidRequest, player.Character.Meso);
        }

        var quantity = (short)(IsWeapon(itemId) || IsOverall(itemId)
            ? _random.NextInclusive(5, 11)
            : _random.NextInclusive(3, 7));
        var created = GainItem(player, InventoryType.Etc, crystalItemId, quantity);
        if (created is null)
        {
            return ItemMakerResult.Failure(ItemMakerStatus.InventoryFull, player.Character.Meso);
        }

        player.FlushInventory();
        return Success(player, crystalItemId, InventoryType.Etc, created, new List<InventoryQuantityMutation> { mutation }, mesoChanged: false);
    }

    private Item? GainEquip(Player player, int itemId)
    {
        var equip = _catalog.CreateEquip(itemId) ?? new Equip { ItemId = itemId, Quantity = 1 };
        return player.Inventory.By(InventoryType.Equip).Gain(equip);
    }

    private static Item? GainItem(Player player, InventoryType type, int itemId, short quantity)
        => player.Inventory.By(type).Gain(type == InventoryType.Equip
            ? new Equip { ItemId = itemId, Quantity = 1 }
            : new Item { ItemId = itemId, Quantity = quantity });

    private bool HasMakerSkill(Player player, int requiredLevel)
        => player.GetSkillLevel(MakerSkillIdForJob(player.Character.Job)) >= requiredLevel;

    private static int MakerSkillIdForJob(short job)
    {
        if (job >= 1000 && job < 2000)
        {
            return CygnusMakerSkillId;
        }

        return job is >= 2000 and <= 2112 ? AranMakerSkillId : BeginnerMakerSkillId;
    }

    private static bool HasIngredients(Player player, IReadOnlyList<ItemMakeIngredient> ingredients)
    {
        foreach (var ingredient in ingredients)
        {
            var type = Player.InventoryTypeOf(ingredient.ItemId);
            if (player.Inventory.By(type).CountById(ingredient.ItemId) < ingredient.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static List<InventoryQuantityMutation> ConsumeIngredients(Player player, IReadOnlyList<ItemMakeIngredient> ingredients)
    {
        var mutations = new List<InventoryQuantityMutation>();
        foreach (var ingredient in ingredients)
        {
            var type = Player.InventoryTypeOf(ingredient.ItemId);
            if (player.TryConsumeItemById(type, ingredient.ItemId, ingredient.Count, out var itemMutations))
            {
                mutations.AddRange(itemMutations);
            }
        }

        return mutations;
    }

    private int SelectWeightedReward(IReadOnlyList<ItemMakeRandomReward> rewards)
    {
        var total = rewards.Sum(static reward => Math.Max(0, reward.Weight));
        if (total <= 0)
        {
            return 0;
        }

        var roll = _random.NextInt(total);
        foreach (var reward in rewards)
        {
            var weight = Math.Max(0, reward.Weight);
            if (roll < weight)
            {
                return reward.ItemId;
            }

            roll -= weight;
        }

        return rewards[^1].ItemId;
    }

    private void RandomizeStats(Equip equip)
    {
        equip.Str = RandomizeStat(equip.Str, 5);
        equip.Dex = RandomizeStat(equip.Dex, 5);
        equip.Int = RandomizeStat(equip.Int, 5);
        equip.Luk = RandomizeStat(equip.Luk, 5);
        equip.Matk = RandomizeStat(equip.Matk, 5);
        equip.Watk = RandomizeStat(equip.Watk, 5);
        equip.Acc = RandomizeStat(equip.Acc, 5);
        equip.Avoid = RandomizeStat(equip.Avoid, 5);
        equip.Jump = RandomizeStat(equip.Jump, 5);
        equip.Hands = RandomizeStat(equip.Hands, 5);
        equip.Speed = RandomizeStat(equip.Speed, 5);
        equip.Wdef = RandomizeStat(equip.Wdef, 10);
        equip.Mdef = RandomizeStat(equip.Mdef, 10);
        equip.Hp = RandomizeStat(equip.Hp, 10);
        equip.Mp = RandomizeStat(equip.Mp, 10);
    }

    private short RandomizeStat(short current, int maxRange)
    {
        if (current == 0)
        {
            return 0;
        }

        var range = Math.Min((int)Math.Ceiling(current * 0.1), maxRange);
        if (range <= 0)
        {
            return current;
        }

        return (short)Math.Clamp(current - range + _random.NextInt(range * 2 + 1), short.MinValue, short.MaxValue);
    }

    private void ApplyEnhanceStats(Equip equip, ItemMakeEnhanceStats stats)
    {
        equip.Watk = Add(equip.Watk, stats.Watk);
        equip.Matk = Add(equip.Matk, stats.Matk);
        equip.Acc = Add(equip.Acc, stats.Acc);
        equip.Avoid = Add(equip.Avoid, stats.Avoid);
        equip.Speed = Add(equip.Speed, stats.Speed);
        equip.Jump = Add(equip.Jump, stats.Jump);
        equip.Hp = Add(equip.Hp, stats.Hp);
        equip.Mp = Add(equip.Mp, stats.Mp);
        equip.Str = Add(equip.Str, stats.Str);
        equip.Dex = Add(equip.Dex, stats.Dex);
        equip.Int = Add(equip.Int, stats.Int);
        equip.Luk = Add(equip.Luk, stats.Luk);

        if (stats.RandomOption > 0)
        {
            var delta = _random.NextBool() ? stats.RandomOption : -stats.RandomOption;
            if (equip.Watk > 0)
            {
                equip.Watk = Add(equip.Watk, delta);
            }

            if (equip.Matk > 0)
            {
                equip.Matk = Add(equip.Matk, delta);
            }
        }

        if (stats.RandomStat > 0)
        {
            var delta = _random.NextBool() ? stats.RandomStat : -stats.RandomStat;
            if (equip.Str > 0)
            {
                equip.Str = Add(equip.Str, delta);
            }

            if (equip.Dex > 0)
            {
                equip.Dex = Add(equip.Dex, delta);
            }

            if (equip.Int > 0)
            {
                equip.Int = Add(equip.Int, delta);
            }

            if (equip.Luk > 0)
            {
                equip.Luk = Add(equip.Luk, delta);
            }
        }
    }

    private static short Add(short current, int delta)
        => (short)Math.Clamp(current + delta, short.MinValue, short.MaxValue);

    private static ItemMakerResult Success(
        Player player,
        int createdItemId,
        InventoryType type,
        Item created,
        IReadOnlyList<InventoryQuantityMutation> mutations,
        bool mesoChanged)
        => new(
            ItemMakerStatus.Success,
            CharacterMutated: true,
            CreatedItemId: createdItemId,
            CreatedInventoryType: type,
            CreatedItem: created,
            InventoryMutations: mutations,
            MesoChanged: mesoChanged,
            Meso: player.Character.Meso);

    private static int CrystalForLevel(int level)
        => level switch
        {
            >= 31 and <= 50 => 4260000,
            >= 51 and <= 60 => 4260001,
            >= 61 and <= 70 => 4260002,
            >= 71 and <= 80 => 4260003,
            >= 81 and <= 90 => 4260004,
            >= 91 and <= 100 => 4260005,
            >= 101 and <= 110 => 4260006,
            >= 111 and <= 120 => 4260007,
            >= 121 and <= 200 => 4260008,
            _ => 0,
        };

    private static bool IsGem(int itemId)
        => itemId is >= 4250000 and <= 4251402;

    private static bool IsOtherGem(int itemId)
        => itemId is 4001174 or 4001175 or 4001176 or 4001177 or 4001178 or 4001179
            or 4001180 or 4001181 or 4001182 or 4001183 or 4001184 or 4001185
            or 4001186 or 4031980 or 2041058 or 2040727 or 1032062 or 4032334
            or 4032312 or 1142156 or 1142157;

    private static bool IsWeapon(int itemId)
        => itemId is >= 1300000 and < 1500000;

    private static bool IsOverall(int itemId)
        => itemId / 10000 == 105;
}
