using Maple.Core.Characters;

namespace Maple.Core.Guilds;

public sealed class GuildMember
{
    public int CharacterId { get; set; }

    public string Name { get; set; } = string.Empty;

    public short Level { get; set; }

    public int JobId { get; set; }

    public byte Channel { get; set; } = byte.MaxValue;

    public byte GuildRank { get; set; } = Guild.DefaultMemberRank;

    public byte AllianceRank { get; set; } = Guild.DefaultAllianceRank;

    public int GuildId { get; set; }

    public bool IsOnline { get; set; }

    public GuildMember Clone() => new()
    {
        CharacterId = CharacterId,
        Name = Name,
        Level = Level,
        JobId = JobId,
        Channel = Channel,
        GuildRank = GuildRank,
        AllianceRank = AllianceRank,
        GuildId = GuildId,
        IsOnline = IsOnline,
    };

    public static GuildMember FromCharacter(
        Character character,
        int channel,
        bool isOnline = true,
        byte? rank = null,
        int? guildId = null)
    {
        ArgumentNullException.ThrowIfNull(character);

        return new GuildMember
        {
            CharacterId = character.Id,
            Name = character.Name,
            Level = character.Level,
            JobId = character.Job,
            Channel = channel > 0 ? (byte)Math.Min(channel, byte.MaxValue - 1) : byte.MaxValue,
            GuildRank = rank ?? character.GuildRank,
            AllianceRank = character.AllianceRank,
            GuildId = guildId ?? character.GuildId,
            IsOnline = isOnline,
        };
    }
}
