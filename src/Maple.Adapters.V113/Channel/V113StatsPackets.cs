using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113DistributeApRequest(int Tick, int RawStat, AbilityPointTarget? Target);

internal readonly record struct V113DistributeSpRequest(int Tick, int SkillId);

internal readonly record struct V113HealOverTimeRequest(int Tick, int Hp, int Mp);

/// <summary>v113 AP/SP/vitals stat packets. Mirrors Java CwvsContext UPDATE_STATS layout.</summary>
internal static class V113StatsPackets
{
    public const short RecvDistributeAp = 0x51;
    public const short RecvHealOverTime = 0x53;
    public const short RecvDistributeSp = 0x54;
    public const short SendUpdateStats = 0x1D;
    public const short SendUpdateSkills = 0x22;

    public static V113DistributeApRequest ParseDistributeAp(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var rawStat = reader.ReadInt();
        return new V113DistributeApRequest(tick, rawStat, rawStat switch
        {
            0x40 => AbilityPointTarget.Str,
            0x80 => AbilityPointTarget.Dex,
            0x100 => AbilityPointTarget.Int,
            0x200 => AbilityPointTarget.Luk,
            _ => null,
        });
    }

    public static V113DistributeSpRequest ParseDistributeSp(PacketReader reader)
        => new(reader.ReadInt(), reader.ReadInt());

    public static V113HealOverTimeRequest ParseHealOverTime(PacketReader reader)
        => new(reader.ReadInt(), reader.ReadShort(), reader.ReadShort());

    public static byte[] UpdateStats(IEnumerable<PlayerStatUpdate> updates, bool itemReaction = false)
    {
        var ordered = updates
            .Select(static u => new EncodedStat(GetMask(u.Kind), u.Value))
            .OrderBy(static s => s.Mask)
            .ToList();

        var mask = 0;
        foreach (var stat in ordered)
        {
            mask |= stat.Mask;
        }

        var w = new PacketWriter(8 + ordered.Count * 4);
        w.WriteShort(SendUpdateStats);
        w.WriteByte(itemReaction ? 1 : 0);
        w.WriteInt(mask);

        foreach (var stat in ordered)
        {
            WriteStatValue(w, stat.Mask, stat.Value);
        }

        return w.ToArray();
    }

    public static byte[] EnableActions() => UpdateStats(Array.Empty<PlayerStatUpdate>(), itemReaction: true);

    public static byte[] UpdateSkill(int skillId, int level, int masterLevel, long expiration = -1)
    {
        var w = new PacketWriter(28);
        w.WriteShort(SendUpdateSkills);
        w.WriteByte(1);
        w.WriteShort(1);
        w.WriteInt(skillId);
        w.WriteInt(level);
        w.WriteInt(masterLevel);
        w.WriteLong(GetSkillExpirationTime(expiration));
        w.WriteByte(4);
        return w.ToArray();
    }

    private static void WriteStatValue(PacketWriter w, int mask, int value)
    {
        if (mask == 0x1)
        {
            w.WriteShort(value);
        }
        else if (mask <= 0x4)
        {
            w.WriteInt(value);
        }
        else if (mask < 0x20)
        {
            w.WriteByte(value);
        }
        else if (mask == 0x8000)
        {
            w.WriteShort(value);
        }
        else if (mask < 0xFFFF)
        {
            w.WriteShort(value);
        }
        else
        {
            w.WriteInt(value);
        }
    }

    private static int GetMask(PlayerStatKind stat)
        => stat switch
        {
            PlayerStatKind.Skin => 0x1,
            PlayerStatKind.Face => 0x2,
            PlayerStatKind.Hair => 0x4,
            PlayerStatKind.Level => 0x10,
            PlayerStatKind.Job => 0x20,
            PlayerStatKind.Str => 0x40,
            PlayerStatKind.Dex => 0x80,
            PlayerStatKind.Int => 0x100,
            PlayerStatKind.Luk => 0x200,
            PlayerStatKind.Hp => 0x400,
            PlayerStatKind.MaxHp => 0x800,
            PlayerStatKind.Mp => 0x1000,
            PlayerStatKind.MaxMp => 0x2000,
            PlayerStatKind.AvailableAp => 0x4000,
            PlayerStatKind.AvailableSp => 0x8000,
            PlayerStatKind.Exp => 0x10000,
            PlayerStatKind.Fame => 0x20000,
            PlayerStatKind.Meso => 0x40000,
            PlayerStatKind.GachaponExp => 0x200000,
            _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
        };

    private static long GetSkillExpirationTime(long realTimestamp)
    {
        const long FileTimeUnixOffset = 116444592000000000L;
        const long MaxTime = 150842304000000000L;
        return realTimestamp == -1 ? MaxTime : ((realTimestamp / 1000) * 10000000) + FileTimeUnixOffset;
    }

    private readonly record struct EncodedStat(int Mask, int Value);
}
