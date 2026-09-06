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

        return HandleKnownItem(player, slot, itemId, canUsePotion);
    }

    /// <summary>
    /// 套用+消耗一個已知 slot/itemId 的 USE 道具，不經 c2s 封包解析（供 <c>PET_AUTO_POT</c> 等
    /// 呼叫端使用——那類封包只給 slot，itemId 要先查庫存才知道，對照 Java
    /// <c>PetHandler.Pet_AutoPotion</c> 與 <c>InventoryHandler.UseItem</c> 共用同一套「套用道具
    /// 效果」邏輯，僅解析封包/驗證前置條件的方式不同）。
    /// </summary>
    internal V113UseConsumableResult HandleKnownItem(Player player, short slot, int itemId, bool canUsePotion = true)
    {
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
