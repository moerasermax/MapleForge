using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113GamePollRequest(int Tick, int Selection, bool Complete);

internal readonly record struct V113MapleTvRequest(byte[] Payload);

internal static class V113UserInterfaceHandler
{
    public static V113GamePollRequest ParseGamePoll(PacketReader reader)
    {
        if (reader.Remaining < 8)
        {
            _ = reader.ReadBytes(reader.Remaining);
            return new V113GamePollRequest(0, 0, Complete: false);
        }

        return new V113GamePollRequest(reader.ReadInt(), reader.ReadInt(), Complete: true);
    }

    public static V113MapleTvRequest ParseMapleTv(PacketReader reader)
        => new(reader.ReadBytes(reader.Remaining));

    public static byte[] GamePollReply(string message)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.GamePollReply);
        w.WriteMapleString(message);
        return w.ToArray();
    }

    public static byte[] HandleGamePoll(PacketReader reader)
    {
        _ = ParseGamePoll(reader);
        // Java gates this behind ServerConstants.PollEnabled=false in this tree.
        return V113StatsPackets.EnableActions();
    }

    public static byte[] HandleMapleTv(PacketReader reader)
    {
        _ = ParseMapleTv(reader);
        // This Java tree has no MAPLETV dispatch body; cash-item MapleTV also reports no MapleTV broadcaster.
        return V113StatsPackets.EnableActions();
    }
}
