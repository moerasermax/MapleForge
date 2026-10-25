using Maple.Application.Combat;
using Maple.Application.Events;
using Maple.Application.Maps;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>P173：船班事件效果的 v113 編碼（對照 Java MaplePacketCreator.boatPacket／boatEffect／musicChange／getClock）。</summary>
public sealed class V113TransportEffects : ITransportEffects
{
    public const short ClockOp = unchecked((short)0x8D);
    public const short BoatEffectOp = unchecked((short)0x8E);
    public const short BoatPacketOp = unchecked((short)0x8F);

    private readonly IMapSessionRegistry _maps;
    private readonly IFieldInstanceRegistry _fields;
    private readonly CombatService _combat;

    public V113TransportEffects(IMapSessionRegistry maps, IFieldInstanceRegistry fields, CombatService combat)
    {
        _maps = maps;
        _fields = fields;
        _combat = combat;
    }

    /// <summary>Java <c>boatPacket(type)</c>：<c>[0x8F][byte type ? 1 : 2][byte 0]</c>（unverified）。</summary>
    public static byte[] BoatPacket(bool docked)
    {
        var w = new PacketWriter(4);
        w.WriteShort(BoatPacketOp);
        w.WriteByte(docked ? 1 : 2);
        w.WriteByte(0);
        return w.ToArray();
    }

    /// <summary>Java <c>boatEffect(effect)</c>：<c>[0x8E][short effect]</c>（1034 巴洛古船、1548 船來、3 船走）。</summary>
    public static byte[] BoatEffect(int effect)
    {
        var w = new PacketWriter(4);
        w.WriteShort(BoatEffectOp);
        w.WriteShort((short)effect);
        return w.ToArray();
    }

    /// <summary>Java <c>getClock(seconds)</c>：<c>[0x8D][byte 2][int 秒]</c>。</summary>
    public static byte[] Clock(int seconds)
    {
        var w = new PacketWriter(7);
        w.WriteShort(ClockOp);
        w.WriteByte(2);
        w.WriteInt(seconds);
        return w.ToArray();
    }

    public Task BoatPacketAsync(int mapId, bool docked, CancellationToken ct) => BroadcastAsync(mapId, BoatPacket(docked), ct);

    public Task BoatEffectAsync(int mapId, int effect, CancellationToken ct) => BroadcastAsync(mapId, BoatEffect(effect), ct);

    /// <summary>Java <c>musicChange(song)</c> = environmentChange(song, 6)。</summary>
    public Task MusicAsync(int mapId, string song, CancellationToken ct) => BroadcastAsync(mapId, V113CombatPackets.EnvironmentChange(song, 6), ct);

    public Task NoticeAsync(int mapId, int type, string message, CancellationToken ct)
        => BroadcastAsync(mapId, V113BroadcastPackets.ServerNotice(type, message), ct);

    public Task ClockAsync(int mapId, int seconds, CancellationToken ct) => BroadcastAsync(mapId, Clock(seconds), ct);

    public async Task SpawnMonsterAsync(int mapId, int monsterId, short x, short y, CancellationToken ct)
    {
        var field = _fields.GetOrCreate(mapId, out _);
        Mob? mob;
        lock (field)
        {
            mob = _combat.SpawnMonsterOnGroundBelow(field, monsterId, x, y);
        }

        if (mob is not null)
        {
            await BroadcastAsync(mapId, V113CombatPackets.SpawnMonster(mob), ct);
        }
    }

    public async Task KillAllMonstersAsync(int mapId, CancellationToken ct, bool animate = false)
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
            await BroadcastAsync(mapId, V113CombatPackets.KillMonster(objectId, animate ? (byte)1 : (byte)0), ct);
        }
    }

    private async Task BroadcastAsync(int mapId, byte[] packet, CancellationToken ct)
    {
        foreach (var entry in _maps.GetAll(mapId).ToArray())
        {
            try
            {
                await entry.SendPacket(packet, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // best-effort
            }
        }
    }
}
