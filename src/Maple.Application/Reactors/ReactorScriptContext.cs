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
    private readonly Maple.Application.Combat.CombatService? _combat;
    private readonly List<Mob> _spawnedMobs = new();
    private readonly List<(int ObjectId, byte Animation)> _killedMobs = new();
    private readonly List<string> _music = new();
    private readonly List<(int MapId, byte State)> _reactorStates = new();
    private readonly List<(bool MapWide, int Type, string Message)> _notices = new();
    private readonly List<InventoryStackGain> _inventoryGains = new();
    private readonly List<InventoryQuantityMutation> _inventoryRemovals = new();
    private readonly List<(int ItemId, int Quantity)> _itemGainNotices = new();

    public ReactorScriptContext(
        Player player,
        Reactor reactor,
        FieldInstance? field = null,
        DropService? drops = null,
        IReactorDropCatalog? reactorDrops = null,
        Maple.Application.Combat.CombatService? combat = null)
    {
        _combat = combat;
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

    /// <summary>P183：spawnMonster 生成的怪（呼叫端廣播 SPAWN_MONSTER）。</summary>
    public IReadOnlyList<Mob> SpawnedMobs => _spawnedMobs;

    /// <summary>P183：killMonster／killAll 移除的怪（呼叫端廣播 KILL_MONSTER）。</summary>
    public IReadOnlyList<(int ObjectId, byte Animation)> KilledMobs => _killedMobs;

    /// <summary>P185：腳本要求的地圖反應堆狀態變更（呼叫端在鎖外套用，可能是別張圖）。</summary>
    public IReadOnlyList<(int MapId, byte State)> ReactorStateChanges => _reactorStates;

    public void SetMapReactorState(int mapId, byte state)
    {
        Record(nameof(SetMapReactorState), mapId, state);
        _reactorStates.Add((mapId, state));
    }

    /// <summary>P183：changeMusic（全圖 musicChange）。</summary>
    public IReadOnlyList<string> Music => _music;

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

    /// <summary>
    /// P183：Java <c>ReactorActionManager.spawnMonster(id, qty)</c>：在 <c>getPosition()</c>（反應堆位置 y − 10）
    /// 下方地面生 qty 隻（spawnMonsterOnGroundBelow）。
    /// </summary>
    public void SpawnMonster(int monsterId, int quantity = 1)
    {
        Record(nameof(SpawnMonster), monsterId, quantity);
        if (_field is null || _combat is null)
        {
            return;
        }

        SpawnAt(monsterId, quantity, Reactor.Position.X, Reactor.Position.Y - 10);
    }

    /// <summary>P184：Java <c>spawnMonster(id, qty, Point(x, y))</c>：指定座標下方地面生 qty 隻。</summary>
    public void SpawnMonsterAt(int monsterId, int quantity, int x, int y)
    {
        Record(nameof(SpawnMonsterAt), monsterId, quantity, x, y);
        SpawnAt(monsterId, quantity, x, y);
    }

    private void SpawnAt(int monsterId, int quantity, int x, int y)
    {
        if (_field is null || _combat is null)
        {
            return;
        }

        for (var i = 0; i < quantity; i++)
        {
            if (_combat.SpawnMonsterOnGroundBelow(_field, monsterId, x, y) is { } mob)
            {
                _spawnedMobs.Add(mob);
            }
        }
    }

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

    /// <summary>P183：Java <c>MapleMap.killMonster(id)</c>：第一隻該 ID 的怪直接移除（無獎勵、動畫 1）。</summary>
    public void KillMonster(int monsterId)
    {
        Record(nameof(KillMonster), monsterId);
        if (_field?.Objects.OfType<Mob>().FirstOrDefault(m => m.IsAlive && m.Definition.MonsterId == monsterId) is { } mob)
        {
            Kill(mob, 1);
        }
    }

    /// <summary>P183：Java <c>killAll()</c> = <c>killAllMonsters(true)</c>：全部移除、動畫 1。</summary>
    public void KillAll()
    {
        Record(nameof(KillAll));
        foreach (var mob in _field?.Objects.OfType<Mob>().ToArray() ?? [])
        {
            Kill(mob, 1);
        }
    }

    /// <summary>P183：Java <c>changeMusic</c> → 全圖 musicChange。</summary>
    public void ChangeMusic(string song)
    {
        Record(nameof(ChangeMusic), song);
        _music.Add(song);
    }

    private void Kill(Mob mob, byte animation)
    {
        if (_field is not null && _combat is not null && _combat.KillMobWithoutRewards(_field, mob.ObjectId, animation).Killed)
        {
            _killedMobs.Add((mob.ObjectId, animation));
        }
    }

    public void RecordUnsupported(string name, params object?[] args) => Record(name, args);

    private void Record(string name, params object?[] args)
        => _calls.Add(new ReactorScriptCall(name, args));
}
