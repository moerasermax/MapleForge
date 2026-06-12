using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113DamageReactorRequest(int ObjectId, int CharacterPosition, short Stance);

internal readonly record struct V113TouchReactorRequest(int ObjectId, bool Touched);

internal static class V113ReactorPackets
{
    public const short DamageReactorRecvOp = unchecked((short)0xC9);
    public const short TouchReactorRecvOp = unchecked((short)0xCA);
    public const short ReactorHitSendOp = 0x113;
    public const short ReactorSpawnSendOp = 0x115;
    public const short ReactorDestroySendOp = 0x116;

    public static V113DamageReactorRequest ParseDamageReactor(PacketReader reader)
    {
        var oid = reader.ReadInt();
        var charPosition = reader.ReadInt();
        var stance = reader.ReadShort();
        return new V113DamageReactorRequest(oid, charPosition, stance);
    }

    public static V113TouchReactorRequest ParseTouchReactor(PacketReader reader)
    {
        var oid = reader.ReadInt();
        var touched = reader.ReadByte() > 0;
        return new V113TouchReactorRequest(oid, touched);
    }

    public static byte[] SpawnReactor(Reactor reactor)
    {
        var w = new PacketWriter(24 + reactor.Name.Length);
        w.WriteShort(ReactorSpawnSendOp);
        w.WriteInt(reactor.ObjectId);
        w.WriteInt(reactor.ReactorId);
        w.WriteByte(reactor.State);
        WritePosition(w, reactor);
        w.WriteByte(reactor.FacingDirection);
        w.WriteMapleString(reactor.Name);
        return w.ToArray();
    }

    public static byte[] TriggerReactor(Reactor reactor, short stance)
    {
        var w = new PacketWriter(18);
        w.WriteShort(ReactorHitSendOp);
        w.WriteInt(reactor.ObjectId);
        w.WriteByte(reactor.State);
        WritePosition(w, reactor);
        w.WriteShort(stance);
        w.WriteByte(0);
        w.WriteByte(4);
        return w.ToArray();
    }

    public static byte[] DestroyReactor(Reactor reactor)
    {
        var w = new PacketWriter(11);
        w.WriteShort(ReactorDestroySendOp);
        w.WriteInt(reactor.ObjectId);
        w.WriteByte(reactor.State);
        WritePosition(w, reactor);
        return w.ToArray();
    }

    public static byte[]? EncodeHitResult(ReactorHitResult hit) => hit.PacketAction switch
    {
        ReactorPacketAction.Hit => TriggerReactor(hit.Reactor, hit.Stance),
        ReactorPacketAction.Destroy => DestroyReactor(hit.Reactor),
        _ => null,
    };

    private static void WritePosition(PacketWriter w, Reactor reactor)
    {
        w.WriteShort(reactor.Position.X);
        w.WriteShort(reactor.Position.Y);
    }
}
