using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113CharacterInfoSocial(string GuildName, string AllianceName)
{
    public static V113CharacterInfoSocial Empty { get; } = new(string.Empty, string.Empty);
}

internal enum V113CharacterInfoUpdateKind : byte
{
    None = 0xFF,
    CharacterMessage = 0,
    Expression = 1,
    Birthday = 2,
    Unknown = 0xFE,
}

internal sealed record V113CharacterInfoUpdate(
    V113CharacterInfoUpdateKind Kind,
    byte RawKind,
    string Message,
    byte Expression,
    byte Blood,
    byte BirthMonth,
    byte BirthDay,
    byte Constellation)
{
    public static V113CharacterInfoUpdate None { get; } = new(
        V113CharacterInfoUpdateKind.None,
        0,
        string.Empty,
        0,
        0,
        0,
        0,
        0);
}

internal static class V113CharacterInfoPackets
{
    public static int ParseCharInfoRequest(PacketReader reader) => reader.ReadInt();

    public static V113CharacterInfoUpdate ParseUpdateCharInfo(PacketReader reader)
    {
        if (reader.Remaining == 0)
        {
            return V113CharacterInfoUpdate.None;
        }

        var type = reader.ReadByte();
        return type switch
        {
            0 => new V113CharacterInfoUpdate(
                V113CharacterInfoUpdateKind.CharacterMessage,
                type,
                reader.ReadMapleString(),
                0,
                0,
                0,
                0,
                0),
            1 => new V113CharacterInfoUpdate(
                V113CharacterInfoUpdateKind.Expression,
                type,
                string.Empty,
                reader.ReadByte(),
                0,
                0,
                0,
                0),
            2 => new V113CharacterInfoUpdate(
                V113CharacterInfoUpdateKind.Birthday,
                type,
                string.Empty,
                0,
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte()),
            _ => new V113CharacterInfoUpdate(
                V113CharacterInfoUpdateKind.Unknown,
                type,
                string.Empty,
                0,
                0,
                0,
                0,
                0),
        };
    }

    public static byte[] CharInfo(Character character, V113CharacterInfoSocial social)
    {
        var w = new PacketWriter(96 + character.Name.Length + social.GuildName.Length + social.AllianceName.Length);
        w.WriteShort(V113ChannelSendOp.CharInfo);
        w.WriteInt(character.Id);
        w.WriteByte(character.Level);
        w.WriteShort(character.Job);
        w.WriteShort(character.Fame);
        w.WriteByte(0); // marriage heart; marriage runtime is not ported yet

        w.WriteMapleString(social.GuildName);
        w.WriteMapleString(social.AllianceName);
        w.WriteMapleString(character.CharacterMessage);
        w.WriteByte(character.ProfileExpression);
        w.WriteByte(character.Constellation);
        w.WriteByte(character.Blood);
        w.WriteByte(character.BirthMonth);
        w.WriteByte(character.BirthDay);

        w.WriteByte(0); // pet list terminator
        w.WriteByte(0); // no mount info
        w.WriteByte(0); // wishlist count

        AddMonsterBookCharInfo(w);
        w.WriteInt(0);   // equipped medal item id
        w.WriteShort(0); // viewable medal quest count
        return w.ToArray();
    }

    private static void AddMonsterBookCharInfo(PacketWriter w)
    {
        w.WriteInt(1); // Java MonsterBook default BookLevel
        w.WriteInt(0); // normal card count
        w.WriteInt(0); // special card count
        w.WriteInt(0); // total card count
        w.WriteInt(0); // cover mob id
    }
}
