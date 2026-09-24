using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Maps;

/// <summary>
/// P075（M4-2 世界 tick）：地圖環境對玩家的持續效果。目前只有地圖持續扣血，對照 Java
/// <c>World.handleMap</c> 的 <c>boolean hurt = map.canHurt()</c> 與 <c>handleCooldowns</c> 的
/// <c>if (chr.isAlive()) { … if (hurt) { … chr.addHP(-map.getHPDec()) } }</c>。
/// 呼叫端負責 <c>lock(field)</c>。
/// </summary>
public sealed class FieldHazardService
{
    /// <summary>Java 特例：迷你地城 749040100 身上（CASH 欄）有此道具即免扣血。</summary>
    public const int MiniDungeonProtectCashItemId = 5451000;

    /// <summary>
    /// 到了扣血週期就對場上活著、沒有防護的玩家扣 <see cref="FieldHpDecay.DecHp"/>，回傳 HP 實際
    /// 有變動的玩家。場上沒有玩家時不推進週期（Java 只在 <c>characterSize() &gt; 0</c> 才呼叫 <c>canHurt</c>）。
    /// </summary>
    public IReadOnlyList<Player> ApplyHpDecay(FieldInstance field, IReadOnlyCollection<Player> players, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(players);

        var decay = field.HpDecay;
        if (decay is null || players.Count == 0 || !decay.TryHurt(now))
        {
            return Array.Empty<Player>();
        }

        var damaged = new List<Player>();
        foreach (var player in players)
        {
            if (!player.IsAlive || IsProtected(player, decay, field.MapId))
            {
                continue;
            }

            if (player.TakeDamage(decay.DecHp) > 0)
            {
                damaged.Add(player);
            }
        }

        return damaged;
    }

    private static bool IsProtected(Player player, FieldHpDecay decay, int mapId)
    {
        // Java：EQUIPPED.findById(protectItem) != null 即免疫；protectItem = 0 時 findById 找不到 → 不免疫。
        if (decay.ProtectItemId != 0 && player.Character.Equips.Any(e => e.ItemId == decay.ProtectItemId))
        {
            return true;
        }

        return mapId == FieldHpDecay.MiniDungeonMapId && player.HasItem(InventoryType.Cash, MiniDungeonProtectCashItemId);
    }
}
