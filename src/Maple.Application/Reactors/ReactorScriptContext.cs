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

    public ReactorScriptContext(Player player, Reactor reactor)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(reactor);
        Player = player;
        Reactor = reactor;
    }

    public void DropItems(bool meso = false, int mesoChance = 0, int minMeso = 0, int maxMeso = 0, int minItems = 0)
        => Record(nameof(DropItems), meso, mesoChance, minMeso, maxMeso, minItems);

    public void DoHarvest() => Record(nameof(DoHarvest));

    public void SpawnMonster(int monsterId, int quantity = 1)
        => Record(nameof(SpawnMonster), monsterId, quantity);

    public void SpawnNpc(int npcId) => Record(nameof(SpawnNpc), npcId);

    public void MapMessage(string message) => Record(nameof(MapMessage), message);

    public void MapMessage(int type, string message) => Record(nameof(MapMessage), type, message);

    public void PlayerMessage(string message) => Record(nameof(PlayerMessage), message);

    public void PlayerMessage(int type, string message) => Record(nameof(PlayerMessage), type, message);

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

    public void GainItem(int itemId, int quantity)
    {
        Player.GainItem(Player.InventoryTypeOf(itemId), itemId, (short)quantity);
        Record(nameof(GainItem), itemId, quantity);
    }

    public bool HaveItem(int itemId, int quantity = 1)
    {
        // Core 目前只有「是否持有」查詢；quantity 精確檢查待 inventory script API 擴充。
        var hasItem = Player.HasItem(Player.InventoryTypeOf(itemId), itemId);
        Record(nameof(HaveItem), itemId, quantity, hasItem);
        return hasItem;
    }

    public bool CanHold(int itemId = 0, int quantity = 1)
    {
        Record(nameof(CanHold), itemId, quantity, true);
        return true;
    }

    public void KillMonster(int monsterId) => Record(nameof(KillMonster), monsterId);

    public void KillAll() => Record(nameof(KillAll));

    public void ChangeMusic(string song) => Record(nameof(ChangeMusic), song);

    public void RecordUnsupported(string name, params object?[] args) => Record(name, args);

    private void Record(string name, params object?[] args)
        => _calls.Add(new ReactorScriptCall(name, args));
}
