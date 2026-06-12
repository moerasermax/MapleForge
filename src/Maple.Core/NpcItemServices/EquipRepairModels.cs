using Maple.Core.Inventory;

namespace Maple.Core.NpcItemServices;

public sealed record EquipRepairDefinition(
    int ItemId,
    int MaxDurability,
    double Price,
    int RequiredLevel);

public interface IEquipRepairCatalog
{
    EquipRepairDefinition? GetRepairDefinition(int itemId);
}

public readonly record struct PlayerEquipRepairState(
    short Position,
    int ItemId,
    int CurrentDurability)
{
    public InventoryType InventoryType => InventoryType.Equip;
}

public sealed record EquipRepairMutation(
    short Position,
    int ItemId,
    int PreviousDurability,
    int NewDurability);
