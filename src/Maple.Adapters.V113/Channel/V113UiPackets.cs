using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P194：教學／過場 UI 封包（Java <c>UIPacket</c> 與 <c>MaplePacketCreator.getEvanTutorial</c>）。
/// Java-source candidate／unverified：版型照 Java，尚未以真客戶端驗證。
/// </summary>
public static class V113UiPackets
{
    public const short CygnusIntroLockOp = 0xD6;
    public const short CygnusIntroDisableUiOp = 0xD7;
    public const short SummonHintOp = 0xD8;
    public const short SummonHintMsgOp = 0xD9;
    public const short NpcTalkOp = 0x13C;
    public const short ShowItemGainInChatOp = 0xC7;

    /// <summary>UIPacket.summonHelper(summon)。</summary>
    public static byte[] SummonHelper(bool summon) => Flag(SummonHintOp, summon);

    /// <summary>UIPacket.summonMessage(String)：[0][str][int 200][short 0][int 10000]。</summary>
    public static byte[] SummonMessage(string message)
    {
        var w = new PacketWriter();
        w.WriteShort(SummonHintMsgOp);
        w.WriteByte(0);
        w.WriteMapleString(message);
        w.WriteInt(200);
        w.WriteShort(0);
        w.WriteInt(10000);
        return w.ToArray();
    }

    /// <summary>UIPacket.summonMessage(int)：[1][int type][int 7000]。</summary>
    public static byte[] SummonMessage(int type)
    {
        var w = new PacketWriter(11);
        w.WriteShort(SummonHintMsgOp);
        w.WriteByte(1);
        w.WriteInt(type);
        w.WriteInt(7000);
        return w.ToArray();
    }

    /// <summary>UIPacket.AranTutInstructionalBalloon(data)：SHOW_ITEM_GAIN_INCHAT [0x15][str][int 1]。</summary>
    public static byte[] AranTutorialBalloon(string data)
    {
        var w = new PacketWriter();
        w.WriteShort(ShowItemGainInChatOp);
        w.WriteByte(0x15);
        w.WriteMapleString(data);
        w.WriteInt(1);
        return w.ToArray();
    }

    /// <summary>MaplePacketCreator.getEvanTutorial(data)：NPC_TALK [int 8][0][1][1][1][str]。</summary>
    public static byte[] EvanTutorial(string data)
    {
        var w = new PacketWriter();
        w.WriteShort(NpcTalkOp);
        w.WriteInt(8);
        w.WriteByte(0);
        w.WriteByte(1);
        w.WriteByte(1);
        w.WriteByte(1);
        w.WriteMapleString(data);
        return w.ToArray();
    }

    public static byte[] IntroLock(bool enable) => Flag(CygnusIntroLockOp, enable);

    public static byte[] IntroDisableUi(bool enable) => Flag(CygnusIntroDisableUiOp, enable);

    private static byte[] Flag(short op, bool value)
    {
        var w = new PacketWriter(3);
        w.WriteShort(op);
        w.WriteByte(value ? 1 : 0);
        return w.ToArray();
    }
}
