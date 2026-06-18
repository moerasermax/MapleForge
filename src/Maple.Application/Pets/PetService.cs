using Maple.Core.Inventory;
using Maple.Core.Pets;
using Maple.Core.World;

namespace Maple.Application.Pets;

public enum PetActionStatus
{
    Success,
    NoActivePet,
    InvalidPet,
    InvalidItem,
    Unsupported,
}

public sealed record PetSpawnResult(PetActionStatus Status, Pet? Pet, byte Slot, short CashSlot, bool Despawned = false)
{
    public bool Success => Status == PetActionStatus.Success;
}

public sealed record PetFeedResult(
    PetActionStatus Status,
    Pet? Pet,
    byte Slot,
    short CashSlot,
    PetGrowthResult Growth,
    InventoryQuantityMutation? ConsumedItem)
{
    public bool Success => Status == PetActionStatus.Success;
}

public sealed record PetCommandResult(
    PetActionStatus Status,
    Pet? Pet,
    byte Slot,
    short CashSlot,
    byte Command,
    PetGrowthResult Growth)
{
    public bool Success => Status == PetActionStatus.Success;
}

public sealed record PetChatResult(
    PetActionStatus Status,
    Pet? Pet,
    byte Slot,
    short Command,
    string Text)
{
    public bool Success => Status == PetActionStatus.Success;
}

public sealed record PetMoveResult(
    PetActionStatus Status,
    Pet? Pet,
    byte Slot,
    byte[] RawMovement)
{
    public bool Success => Status == PetActionStatus.Success;
}

public sealed record PetIgnoreResult(PetActionStatus Status, Pet? Pet, IReadOnlyList<int> ExcludedItems)
{
    public bool Success => Status == PetActionStatus.Success;
}

public sealed record PetAutoPotionResult(PetActionStatus Status, short Slot, int ItemId)
{
    public bool Success => Status == PetActionStatus.Success;
}

public sealed record PetMutationResult(
    PetActionStatus Status,
    Pet? Pet,
    byte Slot,
    short CashSlot,
    PetGrowthResult Growth = default)
{
    public bool Success => Status == PetActionStatus.Success;
}

public sealed class PetService
{
    private const int DefaultCommandProbability = 50;
    private const int DefaultCommandIncrease = 1;
    private const byte ActivePetSlot = 0;

    private readonly object _gate = new();
    private readonly Dictionary<int, ActivePetState> _activePets = new();

    public PetSpawnResult SpawnPet(Player player, short cashSlot, bool lead)
    {
        ArgumentNullException.ThrowIfNull(player);

        var cashItem = player.Inventory.By(InventoryType.Cash).Get(cashSlot);
        if (cashItem is null || cashItem.ItemId <= 0)
        {
            return new PetSpawnResult(PetActionStatus.InvalidItem, null, ActivePetSlot, cashSlot);
        }

        var petId = cashItem.UniqueId is > 0 and <= int.MaxValue
            ? (int)cashItem.UniqueId
            : cashItem.ItemId;

        var pet = new Pet(
            petId,
            cashItem.ItemId,
            string.IsNullOrWhiteSpace(cashItem.Owner) ? "Pet" : cashItem.Owner,
            level: 1,
            closeness: 0,
            fullness: 100,
            flags: cashItem.Flag,
            position: player.Position);

        lock (_gate)
        {
            _activePets[player.Character.Id] = new ActivePetState(pet, cashSlot);
        }

        return new PetSpawnResult(PetActionStatus.Success, pet, ActivePetSlot, cashSlot);
    }

    public PetSpawnResult DespawnPet(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            if (!_activePets.Remove(player.Character.Id, out var state))
            {
                return new PetSpawnResult(PetActionStatus.NoActivePet, null, ActivePetSlot, 0, Despawned: true);
            }

            return new PetSpawnResult(PetActionStatus.Success, state.Pet, ActivePetSlot, state.CashSlot, Despawned: true);
        }
    }

    public Pet? GetActivePet(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_gate)
        {
            return _activePets.GetValueOrDefault(player.Character.Id)?.Pet;
        }
    }

    public PetMutationResult RenameActivePet(Player player, string name)
    {
        ArgumentNullException.ThrowIfNull(player);

        var state = GetActiveState(player);
        if (state is null)
        {
            return FailedMutation(PetActionStatus.NoActivePet);
        }

        state.Pet.Name = name;
        if (player.Inventory.By(InventoryType.Cash).Get(state.CashSlot) is { } cashItem)
        {
            cashItem.Owner = state.Pet.Name;
        }

        return new PetMutationResult(PetActionStatus.Success, state.Pet, ActivePetSlot, state.CashSlot);
    }

    public PetMutationResult ChangeActivePetFlag(Player player, long uniqueId, int flag, bool add)
    {
        ArgumentNullException.ThrowIfNull(player);

        var state = GetActiveState(player);
        if (state is null)
        {
            return FailedMutation(PetActionStatus.NoActivePet);
        }

        var pet = state.Pet;
        if (pet.PetId != uniqueId || flag <= 0)
        {
            return FailedMutation(PetActionStatus.InvalidPet, pet);
        }

        var hasFlag = (pet.Flags & flag) == flag;
        if (add)
        {
            if (hasFlag)
            {
                return FailedMutation(PetActionStatus.Unsupported, pet);
            }

            pet.Flags |= flag;
        }
        else
        {
            if (!hasFlag)
            {
                return FailedMutation(PetActionStatus.Unsupported, pet);
            }

            pet.Flags &= ~flag;
        }

        if (player.Inventory.By(InventoryType.Cash).Get(state.CashSlot) is { } cashItem)
        {
            cashItem.Flag = (short)pet.Flags;
        }

        return new PetMutationResult(PetActionStatus.Success, pet, ActivePetSlot, state.CashSlot);
    }

    public PetMutationResult FeedActivePetToFull(Player player, int closenessGain)
    {
        ArgumentNullException.ThrowIfNull(player);

        var state = GetActiveState(player);
        if (state is null)
        {
            return FailedMutation(PetActionStatus.NoActivePet);
        }

        var growth = state.Pet.FeedToFull(closenessGain);
        return new PetMutationResult(PetActionStatus.Success, state.Pet, ActivePetSlot, state.CashSlot, growth);
    }

    public PetFeedResult FeedPet(Player player, short useSlot, int itemId, bool gainCloseness)
    {
        ArgumentNullException.ThrowIfNull(player);

        var state = GetActiveState(player);
        if (state is null)
        {
            return FailedFeed(PetActionStatus.NoActivePet);
        }

        var pet = state.Pet;

        if (itemId / 10000 != 212 ||
            !player.TryConsumeInventoryItem(InventoryType.Use, useSlot, itemId, 1, out var mutation) ||
            mutation is null)
        {
            return FailedFeed(PetActionStatus.InvalidItem, pet);
        }

        var growth = pet.Feed(gainCloseness);
        player.FlushInventory();
        return new PetFeedResult(PetActionStatus.Success, pet, ActivePetSlot, state.CashSlot, growth, mutation);
    }

    public PetCommandResult HandleCommand(Player player, int petId, byte command, int? roll = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        var state = GetActiveState(player);
        if (state is null)
        {
            return new PetCommandResult(PetActionStatus.NoActivePet, null, ActivePetSlot, 0, command, default);
        }

        var pet = state.Pet;

        if (pet.PetId != petId)
        {
            return new PetCommandResult(PetActionStatus.InvalidPet, pet, ActivePetSlot, state.CashSlot, command, default);
        }

        var growth = pet.ExecuteCommand(DefaultCommandProbability, DefaultCommandIncrease, roll);
        return new PetCommandResult(PetActionStatus.Success, pet, ActivePetSlot, state.CashSlot, command, growth);
    }

    public PetChatResult HandleChat(Player player, int petId, short command, string text)
    {
        ArgumentNullException.ThrowIfNull(player);

        var pet = GetActivePet(player);
        if (pet is null)
        {
            return new PetChatResult(PetActionStatus.NoActivePet, null, ActivePetSlot, command, string.Empty);
        }

        if (pet.PetId != petId)
        {
            return new PetChatResult(PetActionStatus.InvalidPet, pet, ActivePetSlot, command, string.Empty);
        }

        return new PetChatResult(
            PetActionStatus.Success,
            pet,
            ActivePetSlot,
            command,
            text.Length > 100 ? text[..100] : text);
    }

    public PetMoveResult HandleMove(Player player, int petId, byte[] rawMovement)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(rawMovement);

        var pet = GetActivePet(player);
        if (pet is null)
        {
            return new PetMoveResult(PetActionStatus.NoActivePet, null, ActivePetSlot, Array.Empty<byte>());
        }

        if (pet.PetId != petId)
        {
            return new PetMoveResult(PetActionStatus.InvalidPet, pet, ActivePetSlot, Array.Empty<byte>());
        }

        return new PetMoveResult(PetActionStatus.Success, pet, ActivePetSlot, rawMovement);
    }

    public PetIgnoreResult HandleIgnore(Player player, int petId, IReadOnlyList<int> excludedItems)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(excludedItems);

        var pet = GetActivePet(player);
        if (pet is null)
        {
            return new PetIgnoreResult(PetActionStatus.NoActivePet, null, Array.Empty<int>());
        }

        if (pet.PetId != petId)
        {
            return new PetIgnoreResult(PetActionStatus.InvalidPet, pet, Array.Empty<int>());
        }

        if ((pet.Flags & PetConstants.UnpickableFlag) == 0)
        {
            return new PetIgnoreResult(PetActionStatus.Unsupported, pet, Array.Empty<int>());
        }

        pet.ReplaceExcludedItems(excludedItems);
        return new PetIgnoreResult(PetActionStatus.Success, pet, pet.ExcludedItems.ToArray());
    }

    public PetAutoPotionResult HandleAutoPotion(Player player, short slot)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!player.IsAlive)
        {
            return new PetAutoPotionResult(PetActionStatus.Unsupported, slot, 0);
        }

        var item = player.Inventory.By(InventoryType.Use).Get(slot);
        if (item is null || item.Quantity < 1)
        {
            return new PetAutoPotionResult(PetActionStatus.InvalidItem, slot, 0);
        }

        return new PetAutoPotionResult(PetActionStatus.Success, slot, item.ItemId);
    }

    private ActivePetState? GetActiveState(Player player)
    {
        lock (_gate)
        {
            return _activePets.GetValueOrDefault(player.Character.Id);
        }
    }

    private static PetFeedResult FailedFeed(PetActionStatus status, Pet? pet = null)
        => new(status, pet, ActivePetSlot, 0, default, null);

    private static PetMutationResult FailedMutation(PetActionStatus status, Pet? pet = null)
        => new(status, pet, ActivePetSlot, 0);

    private sealed record ActivePetState(Pet Pet, short CashSlot);
}
