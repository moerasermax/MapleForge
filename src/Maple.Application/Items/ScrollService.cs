using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.World;

namespace Maple.Application.Items;

public enum ScrollResult
{
    Success,
    Fail,
    Curse,
}

public sealed record ScrollUseResult(
    ScrollResult Result,
    bool Applied,
    int ScrollId,
    short ScrollSlot,
    short EquipSlot,
    bool EquippedSlot,
    bool WhiteScrollUsed,
    Equip? UpdatedEquip,
    bool EquipDestroyed,
    IReadOnlyList<InventoryQuantityMutation> InventoryMutations);

public sealed class ScrollService
{
    public const int WhiteScrollItemId = 2340000;

    private readonly IScrollCatalog _scrolls;

    public ScrollService(IScrollCatalog scrolls)
    {
        ArgumentNullException.ThrowIfNull(scrolls);
        _scrolls = scrolls;
    }

    public ScrollUseResult UseScroll(
        Player player,
        short scrollSlot,
        short equipSlot,
        bool whiteScroll,
        int randomSeed)
    {
        ArgumentNullException.ThrowIfNull(player);

        var scrollItem = player.Inventory.By(InventoryType.Use).Get(scrollSlot);
        if (scrollItem is null || scrollItem.Quantity <= 0)
        {
            return NotApplied(scrollSlot, equipSlot);
        }

        var scroll = _scrolls.GetScroll(scrollItem.ItemId);
        if (scroll is null)
        {
            return NotApplied(scrollItem.ItemId, scrollSlot, equipSlot);
        }

        var target = ResolveTarget(player, equipSlot);
        if (target.Equip is null)
        {
            return NotApplied(scroll.ScrollId, scrollSlot, equipSlot);
        }

        if (!player.TryConsumeInventoryItem(
                InventoryType.Use,
                scrollSlot,
                scroll.ScrollId,
                1,
                out var scrollMutation) ||
            scrollMutation is null)
        {
            return NotApplied(scroll.ScrollId, scrollSlot, equipSlot);
        }

        var mutations = new List<InventoryQuantityMutation> { scrollMutation };
        var whiteScrollUsed = false;
        if (whiteScroll && player.TryConsumeUseItemById(WhiteScrollItemId, 1, out var whiteMutations))
        {
            whiteScrollUsed = true;
            mutations.AddRange(whiteMutations);
        }

        var equip = target.Equip;
        var result = ResolveScrollResult(equip, scroll, randomSeed);
        var destroyed = false;

        switch (result)
        {
            case ScrollResult.Success:
                ApplySuccess(equip, scroll);
                CommitTarget(player, equipSlot, target.Entry, equip);
                break;

            case ScrollResult.Curse:
                RemoveTarget(player, equipSlot, target.Entry);
                destroyed = true;
                break;

            case ScrollResult.Fail:
                if (equip.UpgradeSlots > 0 && !whiteScrollUsed)
                {
                    equip.UpgradeSlots--;
                }

                CommitTarget(player, equipSlot, target.Entry, equip);
                break;
        }

        player.FlushInventory();
        return new ScrollUseResult(
            result,
            Applied: true,
            scroll.ScrollId,
            scrollSlot,
            equipSlot,
            EquippedSlot: equipSlot < 0,
            WhiteScrollUsed: whiteScrollUsed,
            UpdatedEquip: destroyed ? null : equip,
            EquipDestroyed: destroyed,
            InventoryMutations: mutations);
    }

    private static ScrollUseResult NotApplied(short scrollSlot, short equipSlot)
        => NotApplied(0, scrollSlot, equipSlot);

    private static ScrollUseResult NotApplied(int scrollId, short scrollSlot, short equipSlot)
        => new(
            ScrollResult.Fail,
            Applied: false,
            scrollId,
            scrollSlot,
            equipSlot,
            EquippedSlot: equipSlot < 0,
            WhiteScrollUsed: false,
            UpdatedEquip: null,
            EquipDestroyed: false,
            InventoryMutations: Array.Empty<InventoryQuantityMutation>());

    private static ScrollResult ResolveScrollResult(Equip equip, ScrollEffect scroll, int randomSeed)
    {
        if (equip.UpgradeSlots == 0)
        {
            return ScrollResult.Fail;
        }

        var roll = randomSeed % 100;
        if (roll < 0)
        {
            roll += 100;
        }

        if (roll < scroll.SuccessRate)
        {
            return ScrollResult.Success;
        }

        return scroll.Cursed ? ScrollResult.Curse : ScrollResult.Fail;
    }

    private static void ApplySuccess(Equip equip, ScrollEffect scroll)
    {
        equip.Str += scroll.Str;
        equip.Dex += scroll.Dex;
        equip.Int += scroll.Int;
        equip.Luk += scroll.Luk;
        equip.Hp += scroll.Hp;
        equip.Mp += scroll.Mp;
        equip.Watk += scroll.Watk;
        equip.Matk += scroll.Matk;
        equip.Wdef += scroll.Wdef;
        equip.Mdef += scroll.Mdef;
        equip.Acc += scroll.Acc;
        equip.Avoid += scroll.Avoid;
        equip.Speed += scroll.Speed;
        equip.Jump += scroll.Jump;

        if (equip.UpgradeSlots > 0)
        {
            equip.UpgradeSlots--;
        }

        if (equip.Level < byte.MaxValue)
        {
            equip.Level++;
        }
    }

    private static ScrollTarget ResolveTarget(Player player, short equipSlot)
    {
        if (equipSlot < 0)
        {
            var entry = player.Character.Equips.FirstOrDefault(e => e.Position == equipSlot);
            return new ScrollTarget(entry?.ToEquip(), entry);
        }

        return new ScrollTarget(player.Inventory.By(InventoryType.Equip).Get(equipSlot) as Equip, null);
    }

    private static void CommitTarget(Player player, short equipSlot, Maple.Core.Characters.EquipEntry? entry, Equip equip)
    {
        if (equipSlot < 0)
        {
            entry?.CopyFrom(equip);
        }
    }

    private static void RemoveTarget(Player player, short equipSlot, Maple.Core.Characters.EquipEntry? entry)
    {
        if (equipSlot < 0)
        {
            if (entry is not null)
            {
                player.Character.Equips.Remove(entry);
            }

            return;
        }

        player.Inventory.By(InventoryType.Equip).TryTake(equipSlot, out _);
    }

    private readonly record struct ScrollTarget(Equip? Equip, Maple.Core.Characters.EquipEntry? Entry);
}
