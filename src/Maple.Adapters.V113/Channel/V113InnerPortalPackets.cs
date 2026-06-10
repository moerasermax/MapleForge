using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113InnerPortalRequest(string PortalName, short X, short Y);

internal static class V113InnerPortalPackets
{
    public static V113InnerPortalRequest ParseUseInnerPortal(PacketReader reader)
    {
        reader.Skip(1);
        var portalName = reader.ReadMapleString();
        var x = reader.ReadShort();
        var y = reader.ReadShort();
        return new V113InnerPortalRequest(portalName, x, y);
    }

    public static byte[] CurrentMapWarp(byte portalId)
    {
        var w = new PacketWriter(4);
        w.WriteShort(V113ChannelSendOp.CurrentMapWarp);
        w.WriteByte(0);
        w.WriteByte(portalId);
        return w.ToArray();
    }
}
