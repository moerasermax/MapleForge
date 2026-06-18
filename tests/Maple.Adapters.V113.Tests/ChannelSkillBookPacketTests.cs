using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelSkillBookPacketTests
{
    [Fact]
    public void OpcodeConstants_MatchJava()
    {
        Assert.Equal(0x4C, V113ChannelRecvOp.UseSkillBook);
        Assert.Equal(0x31, V113ChannelSendOp.UseSkillBook);
        Assert.Equal(0x4C, V113SkillBookPackets.RecvUseSkillBook);
        Assert.Equal(0x31, V113SkillBookPackets.SendUseSkillBook);
    }

    [Fact]
    public void ParseUseSkillBook_ReadsTickSlotAndItemId()
    {
        var request = V113SkillBookPackets.ParseUseSkillBook(Request(slot: 2, itemId: 2290000, tick: 1234));

        Assert.Equal(1234, request.Tick);
        Assert.Equal(2, request.Slot);
        Assert.Equal(2290000, request.ItemId);
    }

    [Fact]
    public void HandleUseSkillBook_CatalogMissOnlyEnablesActions()
    {
        var player = NewPlayer(job: 112, skillLevel: 10, masterLevel: 10);
        AddUseItem(player, itemId: 2290000, slot: 1, quantity: 1);
        var catalog = new FakeSkillBookCatalog();

        var result = V113SkillBookHandler.HandleUseSkillBook(Request(slot: 1, itemId: 2290000), player, catalog);

        Assert.True(result.SendEnableActions);
        Assert.False(result.CharacterMutated);
        Assert.Null(result.BroadcastPacket);
        Assert.Empty(result.SelfPackets);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
    }

    [Fact]
    public void HandleUseSkillBook_JobMismatchBroadcastsCanUseFalseWithoutConsuming()
    {
        var player = NewPlayer(job: 112, skillLevel: 10, masterLevel: 10);
        AddUseItem(player, itemId: 2290000, slot: 1, quantity: 1);
        var catalog = Catalog(Book(itemId: 2290000, skillIds: [1221000], successRate: 100, reqSkillLevel: 10, masterLevel: 20));

        var result = V113SkillBookHandler.HandleUseSkillBook(Request(slot: 1, itemId: 2290000), player, catalog);

        Assert.False(result.CharacterMutated);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        var packet = DecodeUseSkillBook(result.BroadcastPacket!);
        Assert.Equal(0, packet.SkillId);
        Assert.False(packet.CanUse);
        Assert.False(packet.Success);
    }

    [Fact]
    public void HandleUseSkillBook_LevelTooLowBroadcastsCanUseFalseWithoutConsuming()
    {
        var player = NewPlayer(job: 112, skillLevel: 9, masterLevel: 10);
        AddUseItem(player, itemId: 2290000, slot: 1, quantity: 1);
        var catalog = Catalog(Book(itemId: 2290000, skillIds: [1121000], successRate: 100, reqSkillLevel: 10, masterLevel: 20));

        var result = V113SkillBookHandler.HandleUseSkillBook(Request(slot: 1, itemId: 2290000), player, catalog);

        Assert.False(result.CharacterMutated);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        var packet = DecodeUseSkillBook(result.BroadcastPacket!);
        Assert.Equal(1121000, packet.SkillId);
        Assert.Equal(20, packet.MasterLevel);
        Assert.False(packet.CanUse);
        Assert.False(packet.Success);
    }

    [Fact]
    public void HandleUseSkillBook_MasterLevelAlreadyHighBroadcastsCanUseFalseWithoutConsuming()
    {
        var player = NewPlayer(job: 112, skillLevel: 10, masterLevel: 20);
        AddUseItem(player, itemId: 2290000, slot: 1, quantity: 1);
        var catalog = Catalog(Book(itemId: 2290000, skillIds: [1121000], successRate: 100, reqSkillLevel: 10, masterLevel: 20));

        var result = V113SkillBookHandler.HandleUseSkillBook(Request(slot: 1, itemId: 2290000), player, catalog);

        Assert.False(result.CharacterMutated);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        var packet = DecodeUseSkillBook(result.BroadcastPacket!);
        Assert.Equal(1121000, packet.SkillId);
        Assert.False(packet.CanUse);
        Assert.False(packet.Success);
    }

    [Fact]
    public void HandleUseSkillBook_SuccessRate100ConsumesBookAndUpdatesMasterLevel()
    {
        var player = NewPlayer(job: 112, skillLevel: 10, masterLevel: 10);
        AddUseItem(player, itemId: 2290000, slot: 1, quantity: 1);
        var catalog = Catalog(Book(itemId: 2290000, skillIds: [1121000], successRate: 100, reqSkillLevel: 10, masterLevel: 20));

        var result = V113SkillBookHandler.HandleUseSkillBook(Request(slot: 1, itemId: 2290000), player, catalog);

        Assert.True(result.CharacterMutated);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
        Assert.Equal(10, player.GetSkillLevel(1121000));
        Assert.Equal(20, player.GetMasterLevel(1121000));
        Assert.Equal(2, result.SelfPackets.Count);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.SelfPackets[0], 0));
        Assert.Equal(V113StatsPackets.SendUpdateSkills, BitConverter.ToInt16(result.SelfPackets[1], 0));
        var packet = DecodeUseSkillBook(result.BroadcastPacket!);
        Assert.True(packet.CanUse);
        Assert.True(packet.Success);
        Assert.Equal(1121000, packet.SkillId);
        Assert.Equal(20, packet.MasterLevel);
    }

    [Fact]
    public void HandleUseSkillBook_SuccessRate0ConsumesBookButDoesNotUpdateMasterLevel()
    {
        var player = NewPlayer(job: 112, skillLevel: 10, masterLevel: 10);
        AddUseItem(player, itemId: 2290000, slot: 1, quantity: 2);
        var catalog = Catalog(Book(itemId: 2290000, skillIds: [1121000], successRate: 0, reqSkillLevel: 10, masterLevel: 20));

        var result = V113SkillBookHandler.HandleUseSkillBook(Request(slot: 1, itemId: 2290000), player, catalog);

        Assert.True(result.CharacterMutated);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Equal(10, player.GetMasterLevel(1121000));
        Assert.Single(result.SelfPackets);
        var packet = DecodeUseSkillBook(result.BroadcastPacket!);
        Assert.True(packet.CanUse);
        Assert.False(packet.Success);
        Assert.Equal(1121000, packet.SkillId);
        Assert.Equal(20, packet.MasterLevel);
    }

    private static PacketReader Request(short slot, int itemId, int tick = 1234)
    {
        var w = new PacketWriter();
        w.WriteInt(tick);
        w.WriteShort(slot);
        w.WriteInt(itemId);
        return new PacketReader(w.ToArray());
    }

    private static Player NewPlayer(short job, byte skillLevel, byte masterLevel)
    {
        var character = new Character { Id = 123, Name = "BookUser", Job = job };
        var player = new Player(character, new Position(0, 0, 0, 0));
        player.ChangeSkillLevel(job * 10000 + 1000, skillLevel, masterLevel);
        return player;
    }

    private static void AddUseItem(Player player, int itemId, short slot, short quantity)
        => player.Inventory.By(InventoryType.Use).Put(new Item
        {
            ItemId = itemId,
            Slot = slot,
            Quantity = quantity,
        });

    private static SkillBookDefinition Book(
        int itemId,
        int[] skillIds,
        int successRate,
        int reqSkillLevel,
        int masterLevel)
        => new(itemId, skillIds, successRate, reqSkillLevel, masterLevel);

    private static FakeSkillBookCatalog Catalog(params SkillBookDefinition[] books) => new(books);

    private static UseSkillBookPacket DecodeUseSkillBook(byte[] packet)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.UseSkillBook, reader.ReadShort());
        var characterId = reader.ReadInt();
        var isUsed = reader.ReadByte();
        var skillId = reader.ReadInt();
        var masterLevel = reader.ReadInt();
        var canUse = reader.ReadByte() != 0;
        var success = reader.ReadByte() != 0;
        return new UseSkillBookPacket(characterId, isUsed, skillId, masterLevel, canUse, success);
    }

    private sealed class FakeSkillBookCatalog : ISkillBookCatalog
    {
        private readonly Dictionary<int, SkillBookDefinition> _books;

        public FakeSkillBookCatalog(params SkillBookDefinition[] books)
        {
            _books = books.ToDictionary(static b => b.ItemId);
        }

        public SkillBookDefinition? GetByItemId(int itemId)
            => _books.GetValueOrDefault(itemId);
    }

    private readonly record struct UseSkillBookPacket(
        int CharacterId,
        byte IsUsed,
        int SkillId,
        int MasterLevel,
        bool CanUse,
        bool Success);
}
