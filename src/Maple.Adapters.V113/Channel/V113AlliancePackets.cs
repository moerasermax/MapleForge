using Maple.Core.Alliances;
using Maple.Core.Guilds;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal enum V113AllianceClientOperation : byte
{
    Load = 0x01,
    Leave = 0x02,
    Invite = 0x03,
    Accept = 0x04,
    Expel = 0x06,
    ChangeLeader = 0x07,
    TitleUpdate = 0x08,
    RankChange = 0x09,
    NoticeUpdate = 0x0A,
    Deny = 0x16,
}

internal static class V113AlliancePackets
{
    public const short RecvAllianceOperationOpcode = 0x86;
    public const short RecvDenyAllianceRequestOpcode = 0x87;
    public const short SendAllianceOperationOpcode = 0x3B;

    public const byte ChangeAllianceCode = 0x01;
    public const byte ChangeAllianceLeaderCode = 0x02;
    public const byte AllianceInviteCode = 0x03;
    public const byte ChangeGuildInAllianceCode = 0x04;
    public const byte ChangeAllianceRankCode = 0x05;
    public const byte AllianceInfoCode = 0x0C;
    public const byte GuildAllianceCode = 0x0D;
    public const byte AllianceMemberOnlineCode = 0x0E;
    public const byte CreateGuildAllianceCode = 0x0F;
    public const byte RemoveGuildFromAllianceCode = 0x10;
    public const byte AddGuildToAllianceCode = 0x12;
    public const byte AllianceUpdateCode = 0x17;
    public const byte UpdateAllianceMemberCode = 0x18;
    public const byte UpdateAllianceLeaderCode = 0x19;
    public const byte UpdateAllianceRankCode = 0x1B;
    public const byte DisbandAllianceCode = 0x1D;

    public static V113AllianceClientOperation ReadOperation(PacketReader reader) =>
        (V113AllianceClientOperation)reader.ReadByte();

    public static byte[] AllianceInfo(AllianceState? alliance)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(AllianceInfoCode);
        w.WriteByte(alliance is null ? 0 : 1);
        if (alliance is not null)
        {
            AddAllianceInfo(w, alliance);
        }

        return w.ToArray();
    }

    public static byte[] AllianceUpdate(AllianceState alliance)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(AllianceUpdateCode);
        AddAllianceInfo(w, alliance);
        return w.ToArray();
    }

    public static byte[] GuildAlliance(AllianceState? alliance, IReadOnlyList<GuildState> guilds)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(GuildAllianceCode);
        if (alliance is null)
        {
            w.WriteInt(0);
            return w.ToArray();
        }

        w.WriteInt(guilds.Count);
        foreach (var guild in guilds)
        {
            AddGuildInfo(w, guild);
        }

        return w.ToArray();
    }

    public static byte[] CreateGuildAlliance(AllianceState alliance, IReadOnlyList<GuildState> guilds)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(CreateGuildAllianceCode);
        AddAllianceInfo(w, alliance);
        foreach (var guild in guilds)
        {
            AddGuildInfo(w, guild);
        }

        return w.ToArray();
    }

    public static byte[] AllianceInvite(string allianceName, int inviterGuildId, string inviterName)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(AllianceInviteCode);
        w.WriteInt(inviterGuildId);
        w.WriteMapleString(inviterName);
        w.WriteMapleString(allianceName);
        return w.ToArray();
    }

    public static byte[] ChangeAlliance(AllianceState alliance, IReadOnlyList<GuildState> guilds, bool inAlliance)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(ChangeAllianceCode);
        w.WriteByte(inAlliance ? 1 : 0);
        w.WriteInt(inAlliance ? alliance.Id : 0);
        w.WriteByte(guilds.Count);
        foreach (var guild in guilds)
        {
            w.WriteInt(guild.Id);
            w.WriteInt(guild.Members.Count);
            foreach (var member in guild.Members)
            {
                w.WriteInt(member.CharacterId);
                w.WriteByte(inAlliance ? member.AllianceRank : 0);
            }
        }

        return w.ToArray();
    }

    public static byte[] ChangeAllianceLeader(int allianceId, int newLeader, int oldLeader)
    {
        var w = new PacketWriter(16);
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(ChangeAllianceLeaderCode);
        w.WriteInt(allianceId);
        w.WriteInt(oldLeader);
        w.WriteInt(newLeader);
        return w.ToArray();
    }

    public static byte[] UpdateAllianceLeader(int allianceId, int newLeader, int oldLeader)
    {
        var w = new PacketWriter(16);
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(UpdateAllianceLeaderCode);
        w.WriteInt(allianceId);
        w.WriteInt(oldLeader);
        w.WriteInt(newLeader);
        return w.ToArray();
    }

    public static byte[] ChangeGuildInAlliance(AllianceState alliance, GuildState guild, bool add)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(ChangeGuildInAllianceCode);
        w.WriteInt(add ? alliance.Id : 0);
        w.WriteInt(guild.Id);
        w.WriteInt(guild.Members.Count);
        foreach (var member in guild.Members)
        {
            w.WriteInt(member.CharacterId);
            w.WriteByte(add ? member.AllianceRank : 0);
        }

        return w.ToArray();
    }

    public static byte[] ChangeAllianceRank(int allianceId, int characterId, byte allianceRank)
    {
        var w = new PacketWriter(15);
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(ChangeAllianceRankCode);
        w.WriteInt(allianceId);
        w.WriteInt(characterId);
        w.WriteInt(allianceRank);
        return w.ToArray();
    }

    public static byte[] UpdateAllianceRank(int allianceId, int characterId, byte allianceRank)
    {
        var w = new PacketWriter(15);
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(UpdateAllianceRankCode);
        w.WriteInt(allianceId);
        w.WriteInt(characterId);
        w.WriteInt(allianceRank);
        return w.ToArray();
    }

    public static byte[] RemoveGuildFromAlliance(AllianceState alliance, GuildState removedGuild, bool expelled)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(RemoveGuildFromAllianceCode);
        AddAllianceInfo(w, alliance);
        AddGuildInfo(w, removedGuild);
        w.WriteByte(expelled ? 1 : 0);
        return w.ToArray();
    }

    public static byte[] AddGuildToAlliance(AllianceState alliance, GuildState newGuild)
    {
        var w = new PacketWriter();
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(AddGuildToAllianceCode);
        AddAllianceInfo(w, alliance);
        w.WriteInt(newGuild.Id);
        AddGuildInfo(w, newGuild);
        w.WriteByte(0);
        return w.ToArray();
    }

    public static byte[] AllianceMemberOnline(int allianceId, int guildId, int characterId, bool online)
    {
        var w = new PacketWriter(16);
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(AllianceMemberOnlineCode);
        w.WriteInt(allianceId);
        w.WriteInt(guildId);
        w.WriteInt(characterId);
        w.WriteByte(online ? 1 : 0);
        return w.ToArray();
    }

    public static byte[] UpdateAllianceMember(int allianceId, int guildId, int characterId, int level, int jobId)
    {
        var w = new PacketWriter(23);
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(UpdateAllianceMemberCode);
        w.WriteInt(allianceId);
        w.WriteInt(guildId);
        w.WriteInt(characterId);
        w.WriteInt(level);
        w.WriteInt(jobId);
        return w.ToArray();
    }

    public static byte[] DisbandAlliance(int allianceId)
    {
        var w = new PacketWriter(7);
        w.WriteShort(SendAllianceOperationOpcode);
        w.WriteByte(DisbandAllianceCode);
        w.WriteInt(allianceId);
        return w.ToArray();
    }

    private static void AddAllianceInfo(PacketWriter w, AllianceState alliance)
    {
        w.WriteInt(alliance.Id);
        w.WriteMapleString(alliance.Name);
        for (var i = 0; i < Alliance.RankCount; i++)
        {
            w.WriteMapleString(i < alliance.Ranks.Count ? alliance.Ranks[i] : string.Empty);
        }

        w.WriteByte(alliance.GuildIds.Count);
        foreach (var guildId in alliance.GuildIds)
        {
            w.WriteInt(guildId);
        }

        w.WriteInt(alliance.Capacity);
        w.WriteMapleString(alliance.Notice);
    }

    private static void AddGuildInfo(PacketWriter w, GuildState guild)
    {
        w.WriteInt(guild.Id);
        w.WriteMapleString(guild.Name);

        for (var i = 0; i < Guild.RankCount; i++)
        {
            w.WriteMapleString(i < guild.RankTitles.Count ? guild.RankTitles[i] : string.Empty);
        }

        AddGuildMemberData(w, guild);
        w.WriteInt(guild.Capacity);
        w.WriteShort(guild.Emblem.LogoBackground);
        w.WriteByte(guild.Emblem.LogoBackgroundColor);
        w.WriteShort(guild.Emblem.Logo);
        w.WriteByte(guild.Emblem.LogoColor);
        w.WriteMapleString(guild.Notice);
        w.WriteInt(guild.GuildPoints);
        w.WriteInt(guild.AllianceId > 0 ? guild.AllianceId : 0);
    }

    private static void AddGuildMemberData(PacketWriter w, GuildState guild)
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
}
