using Maple.Application.Combat;
using Maple.Application.Events;
using Maple.Application.Maps;
using Maple.Application.OnlinePlayers;
using Maple.Application.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P177：事件腳本世界操作的 v113 實作。送包直接入各連線送出佇列（<c>MapleSession.SendAsync</c> 不阻塞），
/// 換圖排進該玩家連線序列（<see cref="IPlayerTransferService"/>），所以事件腳本可在自己的鎖內同步呼叫。
/// </summary>
public sealed class V113EventWorld : IEventWorld
{
    private readonly IMapSessionRegistry _maps;
    private readonly IOnlinePlayerRegistry _online;
    private readonly IPlayerTransferService _transfer;
    private readonly IFieldInstanceRegistry _fields;
    private readonly CombatService _combat;
    private readonly TimeProvider _time;
    private readonly IPartyRegistry? _parties;

    public V113EventWorld(
        IMapSessionRegistry maps,
        IOnlinePlayerRegistry online,
        IPlayerTransferService transfer,
        IFieldInstanceRegistry fields,
        CombatService combat,
        TimeProvider? time = null,
        IPartyRegistry? parties = null)
    {
        _parties = parties;
        _maps = maps;
        _online = online;
        _transfer = transfer;
        _fields = fields;
        _combat = combat;
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<int> GetCharacterIdsOnMap(int mapId) => _maps.GetAll(mapId).Select(static e => e.CharId).ToArray();

    public EventCharacterInfo? GetCharacter(int characterId)
        => _online.FindById(characterId) is { } online
            ? new EventCharacterInfo(characterId, online.Character.Name, online.Character.Level, online.Character.Job, online.Character.MapId)
            : null;

    public void Warp(int characterId, int mapId, int portal) => _transfer.Warp(characterId, mapId, portal);

    public void SendNotice(int characterId, int type, string message)
        => SendTo(characterId, V113BroadcastPackets.ServerNotice(type, message));

    public void SendClock(int characterId, int seconds) => SendTo(characterId, V113TransportEffects.Clock(seconds));

    public void SendEnvironment(int characterId, int mode, string name)
        => SendTo(characterId, V113CombatPackets.EnvironmentChange(name, mode));

    public EventPartyInfo? GetParty(int characterId)
        => _parties?.GetPartyForCharacter(characterId) is { } party
            ? new EventPartyInfo(party.Id, party.LeaderId, party.Members.Select(static m => m.CharacterId).ToArray())
            : null;

    public void BroadcastNotice(int mapId, int type, string message) => Broadcast(mapId, V113BroadcastPackets.ServerNotice(type, message));

    public void BroadcastEnvironment(int mapId, int mode, string name) => Broadcast(mapId, V113CombatPackets.EnvironmentChange(name, mode));

    public void BroadcastClock(int mapId, int seconds) => Broadcast(mapId, V113TransportEffects.Clock(seconds));

    public void ResetMap(int mapId)
    {
        var field = _fields.GetOrCreate(mapId, out _);
        (IReadOnlyList<int> Removed, IReadOnlyList<Mob> Spawned) reset;
        lock (field)
        {
            reset = _combat.ResetFieldMonsters(field, _time.GetUtcNow());
        }

        foreach (var objectId in reset.Removed)
        {
            Broadcast(mapId, V113CombatPackets.KillMonster(objectId, 0));
        }

        foreach (var mob in reset.Spawned)
        {
            Broadcast(mapId, V113CombatPackets.SpawnMonster(mob));
        }
    }

    public void KillAllMonsters(int mapId, bool animate)
    {
        var field = _fields.GetOrCreate(mapId, out _);
        List<int> killed = [];
        lock (field)
        {
            foreach (var mob in field.Objects.OfType<Mob>().ToArray())
            {
                if (_combat.KillMobWithoutRewards(field, mob.ObjectId, animation: animate ? (byte)1 : (byte)0).Killed)
                {
                    killed.Add(mob.ObjectId);
                }
            }
        }

        foreach (var objectId in killed)
        {
            Broadcast(mapId, V113CombatPackets.KillMonster(objectId, animate ? (byte)1 : (byte)0));
        }
    }

    public int SpawnMonster(int mapId, int monsterId, int x, int y)
    {
        var field = _fields.GetOrCreate(mapId, out _);
        Mob? mob;
        lock (field)
        {
            mob = _combat.SpawnMonsterOnGroundBelow(field, monsterId, x, y);
        }

        if (mob is null)
        {
            return -1;
        }

        Broadcast(mapId, V113CombatPackets.SpawnMonster(mob));
        return mob.ObjectId;
    }

    public IReadOnlyList<int> GetMonsterIds(int mapId)
    {
        var field = _fields.GetOrCreate(mapId, out _);
        lock (field)
        {
            return field.Objects.OfType<Mob>().Where(static m => m.IsAlive).Select(static m => m.Definition.MonsterId).ToArray();
        }
    }

    private void SendTo(int characterId, byte[] packet)
    {
        if (_online.FindById(characterId) is { } online)
        {
            Fire(online.SendPacket, packet);
        }
    }

    private void Broadcast(int mapId, byte[] packet)
    {
        foreach (var entry in _maps.GetAll(mapId).ToArray())
        {
            Fire(entry.SendPacket, packet);
        }
    }

    /// <summary>送出佇列入列即返回；例外（連線關閉中）忽略。</summary>
    private static void Fire(Func<byte[], CancellationToken, Task> send, byte[] packet)
    {
        try
        {
            _ = send(packet, CancellationToken.None);
        }
        catch
        {
            // best-effort
        }
    }
}
