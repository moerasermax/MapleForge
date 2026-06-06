using Maple.Core.Guilds;

namespace Maple.Core.World;

public sealed partial class Player
{
    public bool IsInGuild => Character.GuildId > 0;

    public void JoinGuild(int guildId, byte guildRank, byte allianceRank = Guild.DefaultAllianceRank)
    {
        Character.GuildId = guildId;
        Character.GuildRank = guildRank;
        Character.AllianceRank = allianceRank;
    }

    public void LeaveGuild()
    {
        Character.GuildId = 0;
        Character.GuildRank = Guild.DefaultMemberRank;
        Character.AllianceRank = Guild.DefaultAllianceRank;
    }

    public void ChangeGuildRank(byte guildRank)
    {
        if (!IsInGuild)
        {
            return;
        }

        Character.GuildRank = guildRank;
    }
}
