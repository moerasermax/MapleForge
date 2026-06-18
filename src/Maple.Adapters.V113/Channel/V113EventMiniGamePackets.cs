using Maple.Core.Events;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal static class V113EventMiniGamePackets
{
    public const short SendRpsGame = 0x144;
    public const short SendHitCoconut = 0x11B;
    public const short SendCoconutScore = 0x11C;
    public const short SendUpdateBeans = 0x6A;
    public const short SendBeansTips = 0x152;
    public const short SendBeanGameShow = 0x153;
    public const short SendBeanGameShoot = 0x154;

    public static byte[] RpsMode(byte mode, int mesos = -1, int selection = -1, int answer = -1)
    {
        var w = new PacketWriter(12);
        w.WriteShort(SendRpsGame);
        w.WriteByte(mode);
        switch (mode)
        {
            case 6:
                if (mesos != -1)
                {
                    w.WriteInt(mesos);
                }
                break;
            case 8:
                w.WriteInt(9209002);
                break;
            case 11:
                w.WriteByte(selection);
                w.WriteByte(answer);
                break;
        }

        return w.ToArray();
    }

    public static byte[] HitCoconut(bool spawn, int id, int type)
    {
        var w = new PacketWriter(8);
        w.WriteShort(SendHitCoconut);
        if (spawn)
        {
            w.WriteByte(0);
            w.WriteInt(0x80);
        }
        else
        {
            w.WriteInt(id);
            w.WriteByte(type);
        }

        return w.ToArray();
    }

    public static byte[] CoconutScore(int mapleScore, int storyScore)
    {
        var w = new PacketWriter(6);
        w.WriteShort(SendCoconutScore);
        w.WriteShort(mapleScore);
        w.WriteShort(storyScore);
        return w.ToArray();
    }

    public static byte[] UpdateBeans(int characterId, int beansCount)
    {
        var w = new PacketWriter(14);
        w.WriteShort(SendUpdateBeans);
        w.WriteInt(characterId);
        w.WriteInt(beansCount);
        w.WriteInt(0);
        return w.ToArray();
    }

    public static byte[] ShowBeans(int beansCount)
    {
        var w = new PacketWriter(6);
        w.WriteShort(SendBeanGameShow);
        w.WriteInt(beansCount);
        return w.ToArray();
    }

    public static byte[] SetBeanLightLevel(int light)
    {
        var w = new PacketWriter(4);
        w.WriteShort(SendBeanGameShoot);
        w.WriteByte(3);
        w.WriteByte(light);
        return w.ToArray();
    }

    public static byte[] RewardBeans(int beansCount, int openStage = 0)
    {
        var w = new PacketWriter(8);
        w.WriteShort(SendBeanGameShoot);
        w.WriteByte(5);
        w.WriteInt(beansCount);
        w.WriteByte(openStage);
        return w.ToArray();
    }

    public static byte[] ExitBeans()
    {
        var w = new PacketWriter(3);
        w.WriteShort(SendBeanGameShoot);
        w.WriteByte(6);
        return w.ToArray();
    }

    public static byte[] BeansMarquee(string playerName)
    {
        var w = new PacketWriter(8 + playerName.Length);
        w.WriteShort(SendBeansTips);
        w.WriteInt(1);
        w.WriteMapleString(playerName);
        return w.ToArray();
    }

    public static int CoconutPacketType(CoconutHitOutcome outcome)
        => outcome switch
        {
            CoconutHitOutcome.Stopped => 1,
            CoconutHitOutcome.Bombed => 2,
            CoconutHitOutcome.Fell => 3,
            _ => 1,
        };
}
