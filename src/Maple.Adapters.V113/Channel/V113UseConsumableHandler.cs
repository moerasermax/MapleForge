using Maple.Application.Items;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113UseConsumableResult(
    bool Handled,
    bool CharacterMutated,
    IReadOnlyList<byte[]> Packets);

public sealed class V113UseConsumableHandler
{
    private readonly UseItemService _service;

    public V113UseConsumableHandler(UseItemService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>
    /// 對照 Java InventoryHandler.UseItem：<paramref name="canUsePotion"/> 為 false（場地限制
    /// FieldLimitType.PotionUse 生效且不在硬編外的地圖）時整個套用+消耗都跳過，只回 EnableActions
    /// （道具不消耗，語意同 <see cref="V113ItemUseHandler.HandleUseReturnScroll"/>，跟一般補藥
    /// 共用同一個場地限制旗標）。預設 true 維持既有呼叫端（如測試）不需要改動。
    /// </summary>
    internal V113UseConsumableResult Handle(PacketReader reader, Player player, bool canUsePotion = true)
    {
        if (reader.Remaining < 10)
        {
            return EnableActionsOnly();
        }

        reader.Skip(4);
        var slot = reader.ReadShort();
        var itemId = reader.ReadInt();

        if (!canUsePotion)
        {
            return EnableActionsOnly();
        }

        var result = _service.Use(player, slot, itemId);
        if (!result.Success || result.InventoryMutation is null)
        {
            return EnableActionsOnly();
        }

        var packets = new List<byte[]>
        {
            V113ShopPackets.ModifyInventoryQuantity(result.InventoryMutation),
        };

        if (result.StatUpdates.Count > 0)
        {
            packets.Add(V113StatsPackets.UpdateStats(result.StatUpdates));
        }

        packets.Add(V113StatsPackets.EnableActions());
        return new V113UseConsumableResult(true, true, packets);
    }

    private static V113UseConsumableResult EnableActionsOnly()
        => new(true, false, [V113StatsPackets.EnableActions()]);
}
