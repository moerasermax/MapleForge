using Maple.Core.World;

namespace Maple.Application.Reactors;

public sealed record ReactorScriptCall(string Name, IReadOnlyList<object?> Arguments);

/// <summary>
/// 暴露給 reactor 腳本的 <c>rm</c> surface。MVP 先支援 act() 常見呼叫並記錄副作用意圖；
/// 掉落、召怪、環境變更、事件管理等完整行為由後續系統接手。
/// </summary>
public interface IReactorScriptContext
{
    Player Player { get; }
    Reactor Reactor { get; }
    int MapId { get; }
    IReadOnlyList<ReactorScriptCall> Calls { get; }

    void DropItems(bool meso = false, int mesoChance = 0, int minMeso = 0, int maxMeso = 0, int minItems = 0);
    void DoHarvest();
    void SpawnMonster(int monsterId, int quantity = 1);
    void SpawnNpc(int npcId);
    void MapMessage(string message);
    void MapMessage(int type, string message);
    void PlayerMessage(string message);
    void PlayerMessage(int type, string message);
    void Warp(int mapId, int portal = 0);
    void WarpS(int mapId, int portal = 0);
    void GainItem(int itemId, int quantity);
    bool HaveItem(int itemId, int quantity = 1);
    bool CanHold(int itemId = 0, int quantity = 1);
    void KillMonster(int monsterId);
    void KillAll();
    void ChangeMusic(string song);
    void RecordUnsupported(string name, params object?[] args);
}
