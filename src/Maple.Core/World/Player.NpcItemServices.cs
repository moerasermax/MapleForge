using Maple.Core.Inventory;
using Maple.Core.NpcItemServices;
using InventoryEquip = Maple.Core.Inventory.Equip;

namespace Maple.Core.World;

public sealed partial class Player
{
    private readonly Dictionary<short, int> _equipDurabilityByPosition = new();

    /// <summary>
    /// Records current durability for repairable equips. A value of -1 mirrors Java's "not durability equipment".
    /// Persisted durability columns are not present in this worktree yet, so this state is runtime-only.
    /// </summary>
    public void TrackEquipDurability(short position, int durability)
    {
        if (!TryFindEquipItemId(position, out _))
        {
            throw new InvalidOperationException($"No equip exists at position {position}.");
        }

        _equipDurabilityByPosition[position] = durability;
    }

    public bool TryGetEquipForRepair(short position, out PlayerEquipRepairState state)
    {
        state = default;
        if (!TryFindEquipItemId(position, out var itemId))
        {
            return false;
        }

        var durability = _equipDurabilityByPosition.GetValueOrDefault(position, -1);
        state = new PlayerEquipRepairState(position, itemId, durability);
        return true;
    }

    public IReadOnlyList<PlayerEquipRepairState> GetEquipRepairStates()
    {
        var states = new List<PlayerEquipRepairState>();
        foreach (var item in Inventory.By(InventoryType.Equip).Items.OfType<InventoryEquip>())
        {
            var durability = _equipDurabilityByPosition.GetValueOrDefault(item.Slot, -1);
            states.Add(new PlayerEquipRepairState(item.Slot, item.ItemId, durability));
        }

        foreach (var equip in Character.Equips)
        {
            var durability = _equipDurabilityByPosition.GetValueOrDefault(equip.Position, -1);
            states.Add(new PlayerEquipRepairState(equip.Position, equip.ItemId, durability));
        }

        return states;
    }

    public bool SetEquipDurability(short position, int itemId, int durability)
    {
        if (!TryFindEquipItemId(position, out var foundItemId) || foundItemId != itemId)
        {
            return false;
        }

        _equipDurabilityByPosition[position] = durability;
        return true;
    }

    private bool TryFindEquipItemId(short position, out int itemId)
    {
        itemId = 0;
        if (position < 0)
        {
            var equipped = Character.Equips.FirstOrDefault(e => e.Position == position);
            if (equipped is null)
            {
                return false;
            }

            itemId = equipped.ItemId;
            return true;
        }

        if (position == 0 || Inventory.By(InventoryType.Equip).Get(position) is not InventoryEquip equip)
        {
            return false;
        }

        itemId = equip.ItemId;
        return true;
    }
}
