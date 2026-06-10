using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113KeymapChange(int Key, byte Type, int Action);

internal sealed record V113ChangeKeymapRequest(
    int Tick,
    IReadOnlyList<V113KeymapChange> Changes,
    bool IsPetAutoPot,
    int PetAutoPotType = 0,
    int PetAutoPotItemId = 0);

internal static class V113KeymapPackets
{
    private const int KeySlotCount = 90;
    private const int ChangeSize = 9;

    public static V113ChangeKeymapRequest ParseChangeKeymap(PacketReader reader)
    {
        if (reader.Remaining <= 8)
        {
            if (reader.Remaining != 8)
            {
                throw new InvalidDataException($"CHANGE_KEYMAP pet auto pot branch invalid remaining={reader.Remaining}");
            }

            var type = reader.ReadInt();
            var itemId = reader.ReadInt();
            return new V113ChangeKeymapRequest(
                0,
                Array.Empty<V113KeymapChange>(),
                IsPetAutoPot: true,
                PetAutoPotType: type,
                PetAutoPotItemId: itemId);
        }

        var tick = reader.ReadInt();
        var count = reader.ReadInt();
        if (count < 0 || count > reader.Remaining / ChangeSize)
        {
            throw new InvalidDataException($"CHANGE_KEYMAP count invalid: {count}, remaining={reader.Remaining}");
        }

        var changes = new V113KeymapChange[count];
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadInt();
            var type = reader.ReadByte();
            var action = reader.ReadInt();
            changes[i] = new V113KeymapChange(key, type, action);
        }

        return new V113ChangeKeymapRequest(tick, changes, IsPetAutoPot: false);
    }

    public static byte[] Keymap(Character character)
    {
        var w = new PacketWriter(2 + 1 + (KeySlotCount * 5));
        w.WriteShort(V113ChannelSendOp.Keymap);
        w.WriteByte(0);

        var keymap = character.Keymap
            .Where(k => k.Key is >= 0 and < KeySlotCount)
            .GroupBy(k => k.Key)
            .ToDictionary(g => g.Key, g => g.Last());

        for (var key = 0; key < KeySlotCount; key++)
        {
            if (keymap.TryGetValue(key, out var binding))
            {
                w.WriteByte(binding.Type);
                w.WriteInt(binding.Action);
            }
            else
            {
                w.WriteByte(0);
                w.WriteInt(0);
            }
        }

        return w.ToArray();
    }
}
