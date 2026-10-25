using Maple.Application.Drops;
using Maple.Core.Drops;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Reactors;

public sealed class ReactorScriptContext : IReactorScriptContext
{
    private readonly List<ReactorScriptCall> _calls = new();

    public Player Player { get; }

    public Reactor Reactor { get; }

    public int MapId => Player.Character.MapId;

    public IReadOnlyList<ReactorScriptCall> Calls => _calls;

    public int? PendingWarpMapId { get; private set; }

    public int PendingWarpPortal { get; private set; }

    private readonly FieldInstance? _field;
    private readonly DropService? _drops;
    private readonly IReactorDropCatalog? _reactorDrops;
    private readonly List<MapDrop> _spawnedDrops = new();
    private readonly List<(bool MapWide, int Type, string Message)> _notices = new();
    private readonly List<InventoryStackGain> _inventoryGains = new();
    private readonly List<InventoryQuantityMutation> _inventoryRemovals = new();
    private readonly List<(int ItemId, int Quantity)> _itemGainNotices = new();

    public ReactorScriptContext(
        Player player,
        Reactor reactor,
        FieldInstance? field = null,
        DropService? drops = null,
        IReactorDropCatalog? reactorDrops = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(reactor);
        Player = player;
        Reactor = reactor;
        _field = field;
        _drops = drops;
        _reactorDrops = reactorDrops;
    }

    /// <summary>P182：dropItems 生成的掉落物（已加入 field；呼叫端廣播）。</summary>
    public IReadOnlyList<MapDrop> SpawnedDrops => _spawnedDrops;

    /// <summary>P182：mapMessage（全圖）／playerMessage（本人）的黃字公告。</summary>
    public IReadOnlyList<(bool MapWide, int Type, string Message)> Notices => _notices;

    /// <summary>P182：gainItem 的疊加結果與移除異動、getShowItemGain(id, qty, true)。</summary>
    public IReadOnlyList<InventoryStackGain> InventoryGains => _inventoryGains;

    public IReadOnlyList<InventoryQuantityMutation> InventoryRemovals => _inventoryRemovals;

    public IReadOnlyList<(int ItemId, int Quantity)> ItemGainNotices => _itemGainNotices;

    public void DropItems(bool meso = false, int mesoChance = 0, int minMeso = 0, int maxMeso = 0, int minItems = 0)
    {
        Record(nameof(DropItems), meso, mesoChance, minMeso, maxMeso, minItems);
        if (_field is not null && _drops is not null)
        {
            _spawnedDrops.AddRange(_drops.DropFromReactor(
                _field,
                Player,
                Reactor,
                _reactorDrops?.GetDrops(Reactor.ReactorId) ?? Array.Empty<ReactorDropEntry>(),
                meso,
                mesoChance,
                minMeso,
                maxMeso,
                minItems));
        }
    }

    public void DoHarvest() => Record(nameof(DoHarvest));

    public void SpawnMonster(int monsterId, int quantity = 1)
        => Record(nameof(SpawnMonster), monsterId, quantity);

    public void SpawnNpc(int npcId) => Record(nameof(SpawnNpc), npcId);

    /// <summary>Java AbstractPlayerInteraction.mapMessage(msg) = mapMessage(5, msg)。</summary>
    public void MapMessage(string message) => MapMessage(5, message);

    public void MapMessage(int type, string message)
    {
        Record(nameof(MapMessage), type, message);
        _notices.Add((true, type, message));
    }

    /// <summary>Java playerMessage(msg) = playerMessage(5, msg)。</summary>
    public void PlayerMessage(string message) => PlayerMessage(5, message);

    public void PlayerMessage(int type, string message)
    {
        Record(nameof(PlayerMessage), type, message);
        _notices.Add((false, type, message));
    }

    public void Warp(int mapId, int portal = 0)
    {
        PendingWarpMapId = mapId;
        PendingWarpPortal = portal;
        Record(nameof(Warp), mapId, portal);
    }

    public void WarpS(int mapId, int portal = 0)
    {
        PendingWarpMapId = mapId;
        PendingWarpPortal = portal;
        Record(nameof(WarpS), mapId, portal);
    }

    /// <summary>
    /// P182：Java <c>gainItem(id, quantity)</c>：quantity ≥ 0 → checkSpace 通過才 addById（疊加）；
    /// &lt; 0 → 數量足夠才 removeById。成功都送 getShowItemGain(id, quantity, true)。
    /// </summary>
    public void GainItem(int itemId, int quantity)
    {
        Record(nameof(GainItem), itemId, quantity);
        var type = Player.InventoryTypeOf(itemId);
        if (quantity >= 0)
        {
            if (!Player.HasSpaceFor(itemId, quantity))
            {
                return;
            }

            var stack = Player.GainItemStacking(itemId, (short)Math.Clamp(quantity, 1, short.MaxValue));
            if (!stack.AnyChange)
            {
                return;
            }

            _inventoryGains.Add(stack);
        }
        else
        {
            if (!Player.TryTakeItemById(type, itemId, (short)Math.Clamp(-quantity, 1, short.MaxValue), out var removed))
            {
                return;
            }

            _inventoryRemovals.AddRange(removed.Select(static m => new InventoryQuantityMutation(m.Type, m.Slot, m.ItemId, m.OldQuantity, m.NewQuantity)));
        }

        Player.FlushInventory();
        _itemGainNotices.Add((itemId, quantity));
    }

    /// <summary>P182：Java <c>haveItem(id, quantity)</c>：背包數量 ≥ quantity。</summary>
    public bool HaveItem(int itemId, int quantity = 1)
    {
        var hasItem = Player.Inventory.By(Player.InventoryTypeOf(itemId)).CountById(itemId) >= quantity;
        Record(nameof(HaveItem), itemId, quantity, hasItem);
        return hasItem;
    }

    /// <summary>P182：Java <c>canHold()</c>（五個欄位都有空格）／<c>canHold(id)</c>（該欄有空格）／<c>canHold(id, qty)</c>（checkSpace）。</summary>
    public bool CanHold(int itemId = 0, int quantity = 1)
    {
        var result = itemId == 0
            ? new[] { InventoryType.Equip, InventoryType.Use, InventoryType.Setup, InventoryType.Etc, InventoryType.Cash }
                .All(t => Player.Inventory.By(t).FirstFreeSlot() is not null)
            : Player.HasSpaceFor(itemId, quantity);
        Record(nameof(CanHold), itemId, quantity, result);
        return result;
    }

    public void KillMonster(int monsterId) => Record(nameof(KillMonster), monsterId);

    public void KillAll() => Record(nameof(KillAll));

    public void ChangeMusic(string song) => Record(nameof(ChangeMusic), song);

    public void RecordUnsupported(string name, params object?[] args) => Record(name, args);

    private void Record(string name, params object?[] args)
        => _calls.Add(new ReactorScriptCall(name, args));
}
