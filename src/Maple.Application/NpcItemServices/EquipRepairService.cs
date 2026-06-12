using Maple.Core.NpcItemServices;
using Maple.Core.World;

namespace Maple.Application.NpcItemServices;

public enum EquipRepairStatus
{
    Success,
    NotInRepairMap,
    InvalidPosition,
    ItemMissing,
    NotRepairable,
    AlreadyFull,
    NoRepairableItems,
    NotEnoughMeso,
}

public sealed record EquipRepairResult(
    EquipRepairStatus Status,
    int Price,
    int Meso,
    IReadOnlyList<EquipRepairMutation> Mutations)
{
    public bool Applied => Status == EquipRepairStatus.Success;

    public static EquipRepairResult Failed(EquipRepairStatus status, int meso = 0)
        => new(status, 0, meso, Array.Empty<EquipRepairMutation>());
}

public sealed class EmptyEquipRepairCatalog : IEquipRepairCatalog
{
    public EquipRepairDefinition? GetRepairDefinition(int itemId) => null;
}

/// <summary>NPC-gated equip repair use case. Protocol and opcode details stay in V113 adapters.</summary>
public sealed class EquipRepairService
{
    public const int RepairMapId = 240000000;

    private readonly IEquipRepairCatalog _catalog;

    public EquipRepairService(IEquipRepairCatalog catalog)
    {
        _catalog = catalog;
    }

    public EquipRepairResult Repair(Player player, int position)
    {
        if (player.Character.MapId != RepairMapId)
        {
            return EquipRepairResult.Failed(EquipRepairStatus.NotInRepairMap, player.Character.Meso);
        }

        if (position is < short.MinValue or > short.MaxValue || position == 0)
        {
            return EquipRepairResult.Failed(EquipRepairStatus.InvalidPosition, player.Character.Meso);
        }

        if (!player.TryGetEquipForRepair((short)position, out var equip))
        {
            return EquipRepairResult.Failed(EquipRepairStatus.ItemMissing, player.Character.Meso);
        }

        var definition = _catalog.GetRepairDefinition(equip.ItemId);
        if (!IsRepairable(equip, definition))
        {
            return EquipRepairResult.Failed(EquipRepairStatus.NotRepairable, player.Character.Meso);
        }

        if (equip.CurrentDurability >= definition!.MaxDurability)
        {
            return EquipRepairResult.Failed(EquipRepairStatus.AlreadyFull, player.Character.Meso);
        }

        var price = CalculateRepairPrice(equip.CurrentDurability, definition);
        if (player.Character.Meso < price)
        {
            return EquipRepairResult.Failed(EquipRepairStatus.NotEnoughMeso, player.Character.Meso);
        }

        player.GainMeso(-price);
        player.SetEquipDurability(equip.Position, equip.ItemId, definition.MaxDurability);

        return new EquipRepairResult(
            EquipRepairStatus.Success,
            price,
            player.Character.Meso,
            new[]
            {
                new EquipRepairMutation(
                    equip.Position,
                    equip.ItemId,
                    equip.CurrentDurability,
                    definition.MaxDurability),
            });
    }

    public EquipRepairResult RepairAll(Player player)
    {
        if (player.Character.MapId != RepairMapId)
        {
            return EquipRepairResult.Failed(EquipRepairStatus.NotInRepairMap, player.Character.Meso);
        }

        var repairable = new List<(PlayerEquipRepairState Equip, EquipRepairDefinition Definition, int Price)>();
        foreach (var equip in player.GetEquipRepairStates())
        {
            var definition = _catalog.GetRepairDefinition(equip.ItemId);
            if (!IsRepairable(equip, definition) || equip.CurrentDurability >= definition!.MaxDurability)
            {
                continue;
            }

            repairable.Add((equip, definition, CalculateRepairPrice(equip.CurrentDurability, definition)));
        }

        if (repairable.Count == 0)
        {
            return EquipRepairResult.Failed(EquipRepairStatus.NoRepairableItems, player.Character.Meso);
        }

        var totalPrice = repairable.Sum(static r => r.Price);
        if (player.Character.Meso < totalPrice)
        {
            return EquipRepairResult.Failed(EquipRepairStatus.NotEnoughMeso, player.Character.Meso);
        }

        player.GainMeso(-totalPrice);

        var mutations = new List<EquipRepairMutation>(repairable.Count);
        foreach (var (equip, definition, _) in repairable)
        {
            player.SetEquipDurability(equip.Position, equip.ItemId, definition.MaxDurability);
            mutations.Add(new EquipRepairMutation(
                equip.Position,
                equip.ItemId,
                equip.CurrentDurability,
                definition.MaxDurability));
        }

        return new EquipRepairResult(EquipRepairStatus.Success, totalPrice, player.Character.Meso, mutations);
    }

    public static int CalculateRepairPrice(int currentDurability, EquipRepairDefinition definition)
    {
        if (definition.MaxDurability <= 0 || definition.Price <= 0)
        {
            return 0;
        }

        var current = Math.Clamp(currentDurability, 0, definition.MaxDurability);
        var repairPercentage = 100.0 - Math.Ceiling((current * 1000.0) / (definition.MaxDurability * 10.0));
        var divisor = definition.RequiredLevel < 70 ? 100.0 : 1.0;
        var price = Math.Ceiling(repairPercentage * definition.Price / divisor);
        return price <= 0 ? 0 : price >= int.MaxValue ? int.MaxValue : (int)price;
    }

    private static bool IsRepairable(PlayerEquipRepairState equip, EquipRepairDefinition? definition)
        => equip.CurrentDurability >= 0 && definition is { MaxDurability: > 0 };
}
