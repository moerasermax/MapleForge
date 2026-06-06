using Maple.Core.Characters;

namespace Maple.Core.Parties;

public sealed record PartyMember(
    int CharacterId,
    string Name,
    int Level,
    int JobId,
    int MapId,
    int ChannelIndex,
    bool IsOnline = true,
    int DoorTownId = 999_999_999,
    int DoorTargetMapId = 999_999_999,
    int DoorX = 0,
    int DoorY = 0)
{
    public const int NoDoorMapId = 999_999_999;

    public static PartyMember FromCharacter(Character character, int channelIndex, bool isOnline = true)
    {
        ArgumentNullException.ThrowIfNull(character);

        return new PartyMember(
            character.Id,
            character.Name,
            character.Level,
            character.Job,
            character.MapId,
            channelIndex,
            isOnline);
    }
}
