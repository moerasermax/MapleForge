using Maple.Core.IO;
using Maple.Core.Pets;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113SpawnPetRequest(int Tick, short CashSlot, bool Lead);

internal readonly record struct V113MovePetRequest(int PetId, byte[] RawMovement);

internal readonly record struct V113PetFoodRequest(int Tick, short Slot, int ItemId);

internal readonly record struct V113PetChatRequest(int PetId, short Command, string Text);

internal readonly record struct V113PetCommandRequest(int PetId, byte Command);

internal readonly record struct V113PetAutoPotionRequest(short Slot);

internal readonly record struct V113PetIgnoreRequest(int PetId, IReadOnlyList<int> ExcludedItemIds);

internal readonly record struct V113PetLootRequest(int PetId, int Tick, Position ClientPosition, int DropObjectId);

internal static class V113PetPackets
{
    public const short RecvPetFood = 0x46;
    public const short RecvSpawnPet = 0x5C;
    public const short RecvMovePet = unchecked((short)0xA4);
    public const short RecvPetChat = unchecked((short)0xA5);
    public const short RecvPetCommand = unchecked((short)0xA6);
    public const short RecvPetLoot = unchecked((short)0xA7);
    public const short RecvPetAutoPot = unchecked((short)0xA8);
    public const short RecvPetIgnore = unchecked((short)0xA9);

    public const short SendSpawnPet = unchecked((short)0xA2);
    public const short SendMovePet = unchecked((short)0xA5);
    public const short SendPetChat = unchecked((short)0xA6);
    public const short SendPetNameChange = unchecked((short)0xA7);
    public const short SendPetCommand = unchecked((short)0xA9);

    public const short SendModifyInventoryItem = 0x1B;
    public const short SendShowItemGainInChat = unchecked((short)0xC7);
    public const short SendShowForeignEffect = unchecked((short)0xBF);
    public const short SendPetLoadExceptionList = unchecked((short)0xA8);
    public const short SendPetFlagChange = unchecked((short)0xCE);

    public static V113SpawnPetRequest ParseSpawnPet(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var cashSlot = reader.ReadShort();
        var lead = reader.ReadByte() > 0;
        return new V113SpawnPetRequest(tick, cashSlot, lead);
    }

    public static V113MovePetRequest ParseMovePet(PacketReader reader)
    {
        var petId = reader.ReadInt();
        SkipExactly(reader, 8);
        var rawMovement = reader.ReadBytes(reader.Remaining);
        return new V113MovePetRequest(petId, rawMovement);
    }

    public static V113PetFoodRequest ParsePetFood(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var slot = reader.ReadShort();
        var itemId = reader.ReadInt();
        return new V113PetFoodRequest(tick, slot, itemId);
    }

    public static V113PetChatRequest ParsePetChat(PacketReader reader)
    {
        var petId = (int)ReadLong(reader);
        var command = reader.ReadShort();
        var textLength = reader.ReadShort();
        var text = MapleTextEncoding.Value.GetString(reader.ReadBytes(textLength));
        return new V113PetChatRequest(petId, command, text);
    }

    public static V113PetCommandRequest ParsePetCommand(PacketReader reader)
    {
        var petId = reader.ReadInt();
        SkipExactly(reader, 5);
        var command = reader.ReadByte();
        return new V113PetCommandRequest(petId, command);
    }

    public static V113PetAutoPotionRequest ParsePetAutoPot(PacketReader reader)
    {
        SkipExactly(reader, 13);
        return new V113PetAutoPotionRequest(reader.ReadByte());
    }

    public static V113PetIgnoreRequest ParsePetIgnore(PacketReader reader)
    {
        var petId = (int)ReadLong(reader);
        var amount = reader.ReadByte();
        var excluded = new List<int>(amount);
        for (var i = 0; i < amount; i++)
        {
            excluded.Add(reader.ReadInt());
        }

        return new V113PetIgnoreRequest(petId, excluded);
    }

    public static V113PetLootRequest ParsePetLoot(PacketReader reader)
    {
        var petId = (int)ReadLong(reader);
        reader.ReadByte();
        var tick = reader.ReadInt();
        var x = reader.ReadShort();
        var y = reader.ReadShort();
        var objectId = reader.ReadInt();
        return new V113PetLootRequest(petId, tick, new Position(x, y, 0, 0), objectId);
    }

    public static byte[] SpawnPet(int characterId, byte slot, Pet pet)
    {
        var w = new PacketWriter(48);
        w.WriteShort(SendSpawnPet);
        w.WriteInt(characterId);
        w.WriteByte(slot);
        w.WriteByte(1);
        w.WriteByte(0);
        w.WriteInt(pet.ItemId);
        w.WriteMapleString(pet.Name);
        w.WriteLong(pet.PetId);
        w.WriteShort(pet.Position.X);
        w.WriteShort(pet.Position.Y - 20);
        w.WriteByte(pet.Position.Stance);
        w.WriteInt(pet.Position.Foothold);
        return w.ToArray();
    }

    public static byte[] RemovePet(int characterId, byte slot, bool hunger = false)
    {
        var w = new PacketWriter(9);
        w.WriteShort(SendSpawnPet);
        w.WriteInt(characterId);
        w.WriteByte(slot);
        w.WriteByte(0);
        w.WriteByte(hunger ? 1 : 0);
        return w.ToArray();
    }

    public static byte[] MovePet(int characterId, byte slot, int petId, ReadOnlySpan<byte> rawMovement)
    {
        var w = new PacketWriter(rawMovement.Length + 11);
        w.WriteShort(SendMovePet);
        w.WriteInt(characterId);
        w.WriteByte(slot);
        w.WriteInt(petId);
        w.WriteBytes(rawMovement);
        return w.ToArray();
    }

    public static byte[] PetChat(int characterId, byte slot, short command, string text)
    {
        var w = new PacketWriter(text.Length + 16);
        w.WriteShort(SendPetChat);
        w.WriteInt(characterId);
        w.WriteByte(slot);
        w.WriteByte(command);
        w.WriteByte(0);
        w.WriteMapleString(text);
        return w.ToArray();
    }

    public static byte[] PetCommand(int characterId, byte slot, byte command, bool success, bool food)
    {
        var w = new PacketWriter(12);
        w.WriteShort(SendPetCommand);
        w.WriteInt(characterId);
        w.WriteByte(slot);
        w.WriteByte(command == 1 ? 1 : 0);
        w.WriteByte(command);
        if (command == 1)
        {
            w.WriteByte(0);
        }
        else
        {
            w.WriteShort(success ? 1 : 0);
        }

        return w.ToArray();
    }

    public static byte[] PetNameChanged(int characterId, byte slot, string name)
    {
        var w = new PacketWriter(name.Length + 10);
        w.WriteShort(SendPetNameChange);
        w.WriteInt(characterId);
        w.WriteByte(0);
        w.WriteMapleString(name);
        w.WriteByte(slot);
        return w.ToArray();
    }

    public static byte[] PetFlagChanged(long uniqueId, bool added, int flag)
    {
        var w = new PacketWriter(13);
        w.WriteShort(SendPetFlagChange);
        w.WriteLong(uniqueId);
        w.WriteByte(added ? 1 : 0);
        w.WriteShort(flag);
        return w.ToArray();
    }

    public static byte[] UpdatePet(Pet pet, short cashSlot, long expiration = -1)
    {
        var w = new PacketWriter(96);
        w.WriteShort(SendModifyInventoryItem);
        w.WriteByte(0);
        w.WriteByte(2);
        w.WriteByte(3);
        w.WriteByte(5);
        w.WriteShort(cashSlot);
        w.WriteByte(0);
        w.WriteByte(5);
        w.WriteShort(cashSlot);
        w.WriteByte(3);
        w.WriteInt(pet.ItemId);
        w.WriteByte(1);
        w.WriteLong(pet.PetId);
        AddPetItemInfo(w, pet, expiration);
        return w.ToArray();
    }

    public static byte[] ShowOwnPetLevelUp(byte slot)
    {
        var w = new PacketWriter(5);
        w.WriteShort(SendShowItemGainInChat);
        w.WriteByte(4);
        w.WriteByte(0);
        w.WriteByte(slot);
        return w.ToArray();
    }

    public static byte[] ShowPetLevelUp(int characterId, byte slot)
    {
        var w = new PacketWriter(9);
        w.WriteShort(SendShowForeignEffect);
        w.WriteInt(characterId);
        w.WriteByte(4);
        w.WriteByte(0);
        w.WriteByte(slot);
        return w.ToArray();
    }

    public static byte[] LoadExceptionList(int characterId, byte slot, Pet pet)
    {
        var w = new PacketWriter(32 + pet.ExcludedItems.Count * 4);
        w.WriteShort(SendPetLoadExceptionList);
        w.WriteInt(characterId);
        w.WriteByte(slot);
        w.WriteLong(pet.PetId);
        w.WriteByte(pet.ExcludedItems.Count);
        foreach (var excluded in pet.ExcludedItems)
        {
            w.WriteInt(excluded);
        }

        return w.ToArray();
    }

    private static void AddPetItemInfo(PacketWriter w, Pet pet, long expiration)
    {
        w.WriteLong(GetTime(expiration));
        w.WriteFixedAsciiString(pet.Name, 13);
        w.WriteByte(pet.Level);
        w.WriteShort(pet.Closeness);
        w.WriteByte(pet.Fullness);
        w.WriteLong(GetTime(expiration));
        w.WriteShort(0);
        w.WriteShort(pet.Flags);
        w.WriteShort(0);
        w.WriteZeroBytes(4);
    }

    private static long ReadLong(PacketReader reader)
    {
        var low = (uint)reader.ReadInt();
        var high = reader.ReadInt();
        return low | ((long)high << 32);
    }

    private static void SkipExactly(PacketReader reader, int count)
    {
        if (count > 0)
        {
            reader.ReadBytes(count);
        }
    }

    private static long GetTime(long offset)
    {
        const long KoreanEpochOffset = 116444736000000000L;
        if (offset < 0)
        {
            return KoreanEpochOffset + offset;
        }

        return KoreanEpochOffset + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10000);
    }
}
