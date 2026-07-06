using Maple.Adapters.V113.Channel;
using Maple.Application.Items;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Items;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelItemMakerHandlerTests
{
    [Fact]
    public void ParseCreateEquip_ReadsStimulatorAndEnchanterList()
    {
        var request = V113ItemMakerHandler.Parse(Reader(w => w
            .WriteInt(1)
            .WriteInt(1302000)
            .WriteByte(1)
            .WriteInt(2)
            .WriteInt(4250000)
            .WriteInt(4250100)));

        Assert.Equal(ItemMakerRequestKind.CreateItem, request.Kind);
        Assert.Equal(1302000, request.ItemId);
        Assert.True(request.UseStimulator);
        Assert.Equal(new[] { 4250000, 4250100 }, request.EnchanterItemIds);
    }

    [Fact]
    public void ParseCrystalAndDisassemble_ReadJavaLayouts()
    {
        var crystal = V113ItemMakerHandler.Parse(Reader(w => w.WriteInt(3).WriteInt(4000000)));
        Assert.Equal(ItemMakerRequestKind.CreateCrystal, crystal.Kind);
        Assert.Equal(4000000, crystal.ItemId);

        var disassemble = V113ItemMakerHandler.Parse(Reader(w => w
            .WriteInt(4)
            .WriteInt(1302000)
            .WriteInt(123456)
            .WriteInt(2)));
        Assert.Equal(ItemMakerRequestKind.DisassembleEquip, disassemble.Kind);
        Assert.Equal(1302000, disassemble.ItemId);
        Assert.Equal(123456, disassemble.Tick);
        Assert.Equal(2, disassemble.Slot);
    }

    [Fact]
    public void HandleGemSuccess_SendsInventoryMesoSuccessAndUnverifiedBroadcast()
    {
        var catalog = new FakeItemMakeCatalog();
        catalog.Gems[4250000] = new ItemMakeGemRecipe(
            4250000,
            Cost: 100,
            RequiredLevel: 0,
            RequiredMakerLevel: 1,
            RewardQuantity: 1,
            Ingredients: new[] { new ItemMakeIngredient(4000000, 1) },
            RandomRewards: new[] { new ItemMakeRandomReward(4250001, 1) });
        var service = new ItemMakerService(catalog, new SequenceRandom());
        var player = PlayerWith(
            1000,
            new CharacterSkillRecord { SkillId = 1007, Level = 1, MasterLevel = 3 },
            new ItemRecord { Type = (byte)InventoryType.Etc, Slot = 1, ItemId = 4000000, Quantity = 1 });

        var result = V113ItemMakerHandler.Handle(
            Reader(w => w.WriteInt(1).WriteInt(4250000)),
            player,
            service);

        Assert.True(result.CharacterMutated);
        Assert.Equal(ItemMakerStatus.Success, result.Result.Status);
        Assert.Equal(4, result.SelfPackets.Count);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.SelfPackets[0], 0));
        Assert.Equal(V113ChannelSendOp.UpdateStats, BitConverter.ToInt16(result.SelfPackets[1], 0));
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.SelfPackets[2], 0));
        Assert.Equal(V113ItemMakerHandler.ItemMakerSuccess(), result.SelfPackets[3]);
        Assert.Single(result.BroadcastPackets);
        Assert.Equal(V113ItemMakerHandler.ItemMakerSuccessThirdParty(42), result.BroadcastPackets[0]);
        Assert.Equal(900, player.Character.Meso);
    }

    [Fact]
    public void HandleFailure_EnablesActionsWithoutMutation()
    {
        var catalog = new FakeItemMakeCatalog();
        catalog.Gems[4250000] = new ItemMakeGemRecipe(
            4250000,
            100,
            0,
            RequiredMakerLevel: 2,
            1,
            Array.Empty<ItemMakeIngredient>(),
            new[] { new ItemMakeRandomReward(4250001, 1) });
        var service = new ItemMakerService(catalog, new SequenceRandom());
        var player = PlayerWith(1000, new CharacterSkillRecord { SkillId = 1007, Level = 1, MasterLevel = 3 });

        var result = V113ItemMakerHandler.Handle(
            Reader(w => w.WriteInt(1).WriteInt(4250000)),
            player,
            service);

        Assert.False(result.CharacterMutated);
        Assert.Single(result.SelfPackets);
        Assert.Equal(V113StatsPackets.EnableActions(), result.SelfPackets[0]);
        Assert.Empty(result.BroadcastPackets);
    }

    [Fact]
    public void ItemMakerSuccess_WritesJavaSourceCandidateFixture()
    {
        Assert.Equal(new byte[] { 0xC7, 0x00, 0x11, 0, 0, 0, 0 }, V113ItemMakerHandler.ItemMakerSuccess());
        Assert.Equal(
            new byte[] { 0xBF, 0x00, 42, 0, 0, 0, 0x11, 0, 0, 0, 0 },
            V113ItemMakerHandler.ItemMakerSuccessThirdParty(42));
    }

    private static PacketReader Reader(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return new PacketReader(writer.ToArray());
    }

    private static Player PlayerWith(int meso, CharacterSkillRecord skills, params ItemRecord[] items)
        => PlayerWith(meso, new[] { skills }, items);

    private static Player PlayerWith(int meso, IReadOnlyList<CharacterSkillRecord>? skills = null, params ItemRecord[] items)
        => new(
            new Character
            {
                Id = 42,
                Name = "Maker",
                Meso = meso,
                Skills = skills?.ToList() ?? new List<CharacterSkillRecord>(),
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private sealed class SequenceRandom : IItemMakerRandomSource
    {
        public int NextInt(int exclusiveMax) => 0;
        public int NextInclusive(int minInclusive, int maxInclusive) => minInclusive;
        public bool NextBool() => true;
    }

    private sealed class FakeItemMakeCatalog : IItemMakeCatalog
    {
        public Dictionary<int, ItemMakeGemRecipe> Gems { get; } = new();
        public Dictionary<int, ItemMakeCreateRecipe> Creates { get; } = new();

        public ItemMakeGemRecipe? GetGemRecipe(int itemId) => Gems.GetValueOrDefault(itemId);
        public ItemMakeCreateRecipe? GetCreateRecipe(int itemId) => Creates.GetValueOrDefault(itemId);
        public int GetItemMakeLevel(int itemId) => 0;
        public int GetRequiredLevel(int itemId) => 0;
        public bool IsDropRestricted(int itemId) => false;
        public bool IsAccountShared(int itemId) => false;
        public Equip? CreateEquip(int itemId) => new() { ItemId = itemId, Quantity = 1 };
        public ItemMakeEnhanceStats? GetEnhanceStats(int itemId) => null;
        public int GemRecipeCount => Gems.Count;
        public int CreateRecipeCount => Creates.Count;
    }
}
