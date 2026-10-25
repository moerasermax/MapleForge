using Maple.Core.Characters;
using Maple.Core.World;

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

    /// <summary>
    /// P161：Java <c>new MaplePartyCharacter(chr)</c> 的門欄位：有門 → 村莊、目標地圖、目標座標；沒門 → 999999999 與角色目前座標。
    /// </summary>
    public PartyMember WithDoor(Door? door, Position playerPosition)
        => door is null
            ? this with { DoorTownId = NoDoorMapId, DoorTargetMapId = NoDoorMapId, DoorX = playerPosition.X, DoorY = playerPosition.Y }
            : this with { DoorTownId = door.TownMapId, DoorTargetMapId = door.TargetMapId, DoorX = door.TargetPosition.X, DoorY = door.TargetPosition.Y };
}
