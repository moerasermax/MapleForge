using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// P126：v113 玩家異常狀態封包（對照 Java <c>MapleDisease</c> 遮罩值／isFirst 與 <c>MaplePacketCreator.giveDebuff</c>、
/// <c>giveForeignDebuff</c>、<c>cancelDebuff</c>、<c>cancelForeignDebuff</c>、<c>writeLongDiseaseMask</c>）。全部 unverified。
/// </summary>
internal static class V113DiseasePackets
{
    private const short GiveBuffOp = 0x1E;
    private const short CancelBuffOp = 0x1F;
    private const short GiveForeignBuffOp = unchecked((short)0xC0);
    private const short CancelForeignBuffOp = unchecked((short)0xC1);
    private const int PoisonMobSkillId = 125;

    /// <summary>Java 列舉值與 first 旗標（first → 寫在第一個 long）。</summary>
    public static (long Mask, bool First) Encode(PlayerDisease disease) => disease switch
    {
        PlayerDisease.Potion => (0x80000000000L, true),
        PlayerDisease.Shadow => (0x100000000000L, true),
        PlayerDisease.Blind => (0x200000000000L, true),
        PlayerDisease.Freeze => (0x8000000000000L, true),
        PlayerDisease.Slow => (0x1, false),
        PlayerDisease.Morph => (0x2, false),
        PlayerDisease.Seduce => (0x80, false),
        PlayerDisease.Zombify => (0x4000, false),
        PlayerDisease.ReverseDirection => (0x80000, false),
        PlayerDisease.WeirdFlame => (0x8000000, false),
        PlayerDisease.Stun => (0x2000000000000L, false),
        PlayerDisease.Poison => (0x4000000000000L, false),
        PlayerDisease.Seal => (0x8000000000000L, false),
        PlayerDisease.Darkness => (0x10000000000000L, false),
        PlayerDisease.Weaken => (0x4000000000000000L, false),
        PlayerDisease.Curse => (unchecked((long)0x8000000000000000UL), false),
        _ => (0, false),
    };

    public static byte[] GiveDebuff(ActiveDisease disease)
    {
        var w = new PacketWriter();
        w.WriteShort(GiveBuffOp);
        WriteMask(w, disease.Disease);
        w.WriteShort((short)disease.X);
        w.WriteShort((short)disease.SkillId);
        w.WriteShort((short)disease.Level);
        w.WriteInt(disease.DurationMilliseconds);
        w.WriteShort(0);
        w.WriteShort(900);
        w.WriteByte(1);
        return w.ToArray();
    }

    public static byte[] GiveForeignDebuff(int characterId, ActiveDisease disease)
    {
        var w = new PacketWriter();
        w.WriteShort(GiveForeignBuffOp);
        w.WriteInt(characterId);
        WriteMask(w, disease.Disease);
        if (disease.SkillId == PoisonMobSkillId)
        {
            w.WriteShort(0);
        }

        w.WriteShort((short)disease.SkillId);
        w.WriteShort((short)disease.Level);
        w.WriteShort(0);
        w.WriteShort(900);
        return w.ToArray();
    }

    public static byte[] CancelDebuff(PlayerDisease disease)
    {
        var w = new PacketWriter();
        w.WriteShort(CancelBuffOp);
        WriteMask(w, disease);
        w.WriteByte(1);
        return w.ToArray();
    }

    public static byte[] CancelForeignDebuff(int characterId, PlayerDisease disease)
    {
        var w = new PacketWriter();
        w.WriteShort(CancelForeignBuffOp);
        w.WriteInt(characterId);
        WriteMask(w, disease);
        return w.ToArray();
    }

    private static void WriteMask(PacketWriter w, PlayerDisease disease)
    {
        var (mask, first) = Encode(disease);
        w.WriteLong(first ? mask : 0);
        w.WriteLong(first ? 0 : mask);
    }
}
