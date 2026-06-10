using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113SkillMacroChange(
    int Position,
    string Name,
    byte Shout,
    int Skill1,
    int Skill2,
    int Skill3);

internal static class V113SkillMacroPackets
{
    private const int MaxMacros = 5;

    public static IReadOnlyList<V113SkillMacroChange> ParseChangeSkillMacro(PacketReader reader)
    {
        var count = reader.ReadByte();
        if (count > MaxMacros)
        {
            throw new InvalidDataException($"SKILL_MACRO count invalid: {count}");
        }

        var changes = new V113SkillMacroChange[count];
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadMapleString();
            var shout = reader.ReadByte();
            var skill1 = reader.ReadInt();
            var skill2 = reader.ReadInt();
            var skill3 = reader.ReadInt();
            changes[i] = new V113SkillMacroChange(i, name, shout, skill1, skill2, skill3);
        }

        return changes;
    }

    public static byte[]? SkillMacros(Character character)
    {
        var macros = character.SkillMacros
            .Where(m => m.Position is >= 0 and < MaxMacros)
            .GroupBy(m => m.Position)
            .Select(g => g.Last())
            .OrderBy(m => m.Position)
            .ToArray();

        if (macros.Length == 0)
        {
            return null;
        }

        var w = new PacketWriter(128);
        w.WriteShort(V113ChannelSendOp.SkillMacro);
        w.WriteByte(macros.Length);
        foreach (var macro in macros)
        {
            w.WriteMapleString(macro.Name);
            w.WriteByte(macro.Shout);
            w.WriteInt(macro.Skill1);
            w.WriteInt(macro.Skill2);
            w.WriteInt(macro.Skill3);
        }

        return w.ToArray();
    }
}
