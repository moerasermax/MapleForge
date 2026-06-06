using Maple.Application.Guilds;
using Maple.Core.Guilds;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal enum V113GuildClientOperation : byte
{
    Create = 0x02,
    Invite = 0x05,
    Accepted = 0x06,
    Leaving = 0x07,
    Expel = 0x08,
    ChangeRankTitle = 0x0D,
    ChangeRank = 0x0E,
    ChangeEmblem = 0x0F,
    ChangeNotice = 0x10,
}

internal static class V113GuildPackets
{
    public const short RecvGuildOperationOpcode = 0x76;
    public const short RecvDenyGuildRequestOpcode = 0x77;
    public const short SendGuildOperationOpcode = 0x3A;

    public const byte ShowGuildInfoCode = 0x1A;
    public const byte GuildInviteCode = 0x05;
    public const byte NewGuildMemberCode = 0x27;
    public const byte MemberLeftCode = 0x2C;
    public const byte MemberExpelledCode = 0x2F;
    public const byte GuildDisbandCode = 0x32;
    public const byte DenyGuildInvitationCode = 0x37;
    public const byte GuildCapacityChangedCode = 0x3A;
    public const byte GuildMemberLevelJobChangedCode = 0x3C;
    public const byte GuildMemberOnlineCode = 0x3D;
    public const byte RankTitleChangedCode = 0x3E;
    public const byte RankChangedCode = 0x40;
    public const byte GuildEmblemChangedCode = 0x42;
    public const byte GuildNoticeChangedCode = 0x44;
    public const byte GuildPointsChangedCode = 0x48;

    public const byte StatusCreateFailed = 0x1C;
    public const byte StatusAlreadyInGuild = 0x28;
    public const byte StatusNotInChannel = 0x2A;
    public const byte StatusNotInGuild = 0x2D;

    public static V113GuildClientOperation ReadOperation(PacketReader reader) =>
        (V113GuildClientOperation)reader.ReadByte();

    public static byte[] ShowGuildInfo(GuildState? guild)
    {
        var w = new PacketWriter();
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(ShowGuildInfoCode);

        if (guild is null)
        {
            w.WriteByte(0);
            return w.ToArray();
        }

        w.WriteByte(1);
        AddGuildInfo(w, guild);
        return w.ToArray();
    }

    public static byte[] GuildInvite(int guildId, string inviterName)
    {
        var w = new PacketWriter();
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(GuildInviteCode);
        w.WriteInt(guildId);
        w.WriteMapleString(inviterName);
        return w.ToArray();
    }

    public static byte[] DenyGuildInvitation(string characterName)
    {
        var w = new PacketWriter();
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(DenyGuildInvitationCode);
        w.WriteMapleString(characterName);
        return w.ToArray();
    }

    public static byte[] GenericGuildMessage(byte code)
    {
        var w = new PacketWriter(3);
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(code);
        return w.ToArray();
    }

    public static byte[] NewGuildMember(GuildMember member)
    {
        var w = new PacketWriter();
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(NewGuildMemberCode);
        AddNewMemberData(w, member);
        return w.ToArray();
    }

    public static byte[] MemberLeft(GuildMember member, bool expelled)
    {
        var w = new PacketWriter();
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(expelled ? MemberExpelledCode : MemberLeftCode);
        w.WriteInt(member.GuildId);
        w.WriteInt(member.CharacterId);
        w.WriteMapleString(member.Name);
        return w.ToArray();
    }

    public static byte[] ChangeRank(GuildMember member)
    {
        var w = new PacketWriter(12);
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(RankChangedCode);
        w.WriteInt(member.GuildId);
        w.WriteInt(member.CharacterId);
        w.WriteByte(member.GuildRank);
        return w.ToArray();
    }

    public static byte[] GuildNotice(int guildId, string notice)
    {
        var w = new PacketWriter();
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(GuildNoticeChangedCode);
        w.WriteInt(guildId);
        w.WriteMapleString(notice);
        return w.ToArray();
    }

    public static byte[] GuildMemberLevelJobUpdate(GuildMember member)
    {
        var w = new PacketWriter(19);
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(GuildMemberLevelJobChangedCode);
        w.WriteInt(member.GuildId);
        w.WriteInt(member.CharacterId);
        w.WriteInt(member.Level);
        w.WriteInt(member.JobId);
        return w.ToArray();
    }

    public static byte[] RankTitleChange(int guildId, IReadOnlyList<string> titles)
    {
        var w = new PacketWriter();
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(RankTitleChangedCode);
        w.WriteInt(guildId);

        for (var i = 0; i < Guild.RankCount; i++)
        {
            w.WriteMapleString(i < titles.Count ? titles[i] : string.Empty);
        }

        return w.ToArray();
    }

    public static byte[] GuildMemberOnline(int guildId, int characterId, bool online)
    {
        var w = new PacketWriter(12);
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(GuildMemberOnlineCode);
        w.WriteInt(guildId);
        w.WriteInt(characterId);
        w.WriteByte(online ? 1 : 0);
        return w.ToArray();
    }

    public static byte[] GuildDisband(int guildId)
    {
        var w = new PacketWriter(8);
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(GuildDisbandCode);
        w.WriteInt(guildId);
        w.WriteByte(1);
        return w.ToArray();
    }

    public static byte[] GuildEmblemChange(int guildId, GuildEmblem emblem)
    {
        var w = new PacketWriter(12);
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(GuildEmblemChangedCode);
        w.WriteInt(guildId);
        w.WriteShort(emblem.LogoBackground);
        w.WriteByte(emblem.LogoBackgroundColor);
        w.WriteShort(emblem.Logo);
        w.WriteByte(emblem.LogoColor);
        return w.ToArray();
    }

    public static byte[] GuildCapacityChange(int guildId, int capacity)
    {
        var w = new PacketWriter(8);
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(GuildCapacityChangedCode);
        w.WriteInt(guildId);
        w.WriteByte(capacity);
        return w.ToArray();
    }

    public static byte[] UpdateGuildPoints(int guildId, int guildPoints)
    {
        var w = new PacketWriter(11);
        w.WriteShort(SendGuildOperationOpcode);
        w.WriteByte(GuildPointsChangedCode);
        w.WriteInt(guildId);
        w.WriteInt(guildPoints);
        return w.ToArray();
    }

    public static byte ToGenericStatus(GuildCommandStatus status) => status switch
    {
        GuildCommandStatus.AlreadyInGuild or GuildCommandStatus.TargetAlreadyInGuild => StatusAlreadyInGuild,
        GuildCommandStatus.NotInGuild => StatusNotInGuild,
        GuildCommandStatus.TargetNotFound => StatusNotInChannel,
        _ => StatusCreateFailed,
    };

    private static void AddGuildInfo(PacketWriter w, GuildState guild)
    {
        w.WriteInt(guild.Id);
        w.WriteMapleString(guild.Name);

        for (var i = 0; i < Guild.RankCount; i++)
        {
            w.WriteMapleString(i < guild.RankTitles.Count ? guild.RankTitles[i] : string.Empty);
        }

        AddMemberData(w, guild);
        w.WriteInt(guild.Capacity);
        w.WriteShort(guild.Emblem.LogoBackground);
        w.WriteByte(guild.Emblem.LogoBackgroundColor);
        w.WriteShort(guild.Emblem.Logo);
        w.WriteByte(guild.Emblem.LogoColor);
        w.WriteMapleString(guild.Notice);
        w.WriteInt(guild.GuildPoints);
        w.WriteInt(guild.AllianceId > 0 ? guild.AllianceId : 0);
    }

    private static void AddMemberData(PacketWriter w, GuildState guild)
    {
        w.WriteByte(guild.Members.Count);

        foreach (var member in guild.Members)
        {
            w.WriteInt(member.CharacterId);
        }

        foreach (var member in guild.Members)
        {
            w.WriteFixedAsciiString(member.Name, 15);
            w.WriteInt(member.JobId);
            w.WriteInt(member.Level);
            w.WriteInt(member.GuildRank);
            w.WriteInt(member.IsOnline ? 1 : 0);
            w.WriteInt(guild.Signature);
            w.WriteInt(member.AllianceRank);
        }
    }

    private static void AddNewMemberData(PacketWriter w, GuildMember member)
    {
        w.WriteInt(member.GuildId);
        w.WriteInt(member.CharacterId);
        w.WriteFixedAsciiString(member.Name, 15);
        w.WriteInt(member.JobId);
        w.WriteInt(member.Level);
        w.WriteInt(member.GuildRank);
        w.WriteInt(member.IsOnline ? 1 : 0);
        w.WriteInt(1);
        w.WriteInt(member.AllianceRank);
    }
}
