using Maple.Adapters.V113.Channel;
using Maple.Application.Items;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelUseConsumableHandlerTests
{
    [Fact]
    public void OpcodeConstant_MatchesJavaValue()
    {
        Assert.Equal(0x42, V113ChannelRecvOp.UseItem);
    }

    [Fact]
    public void Handle_Success_ConsumesUpdatesStatsAndEnablesActions()
    {
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50, UseItem(2000000, 1, 1));
        var handler = NewHandler();
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteShort(1)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal((short)75, player.Hp);
        Assert.Equal(3, result.Packets.Count);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, new PacketReader(result.Packets[0]).ReadShort());
        Assert.Equal(V113ChannelSendOp.UpdateStats, new PacketReader(result.Packets[1]).ReadShort());
        Assert.Equal(V113StatsPackets.EnableActions(), result.Packets[2]);
    }

    [Fact]
    public void Handle_FieldLimitBlocked_DoesNotConsumeOrApplyEffect()
    {
        // 對照 Java InventoryHandler.UseItem：場地限制生效時整個套用+消耗都跳過，道具不消耗。
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50, UseItem(2000000, 1, 1));
        var handler = NewHandler();
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteShort(1)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player, canUsePotion: false);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        Assert.Equal((short)25, player.Hp);
        Assert.Equal((short)1, player.Character.Items.Single(i => i.ItemId == 2000000).Quantity);
        var packet = Assert.Single(result.Packets);
        Assert.Equal(V113StatsPackets.EnableActions(), packet);
    }

    [Fact]
    public void Handle_MissingItem_ReturnsEnableActions()
    {
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50);
        var handler = NewHandler();
        var body = new PacketWriter()
            .WriteInt(1234)
            .WriteShort(1)
            .WriteInt(2000000)
            .ToArray();

        var result = handler.Handle(new PacketReader(body), player);

        Assert.True(result.Handled);
        Assert.False(result.CharacterMutated);
        var packet = Assert.Single(result.Packets);
        Assert.Equal(V113StatsPackets.EnableActions(), packet);
    }

    [Fact]
    public void HandleKnownItem_Success_ConsumesUpdatesStatsAndEnablesActions()
    {
        // 對照 Java PetHandler.Pet_AutoPotion 共用 InventoryHandler.UseItem 的套用邏輯，
        // 差別只在 itemId 不是從封包讀而是先查庫存拿到（呼叫端職責）。
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50, UseItem(2000000, 1, 1));
        var handler = NewHandler();

        var result = handler.HandleKnownItem(player, slot: 1, itemId: 2000000);

        Assert.True(result.CharacterMutated);
        Assert.Equal((short)75, player.Hp);
        Assert.Equal(3, result.Packets.Count);
    }

    [Fact]
    public void HandleKnownItem_FieldLimitBlocked_DoesNotConsume()
    {
        var player = NewPlayer(hp: 25, maxHp: 100, mp: 10, maxMp: 50, UseItem(2000000, 1, 1));
        var handler = NewHandler();

        var result = handler.HandleKnownItem(player, slot: 1, itemId: 2000000, canUsePotion: false);

        Assert.False(result.CharacterMutated);
        Assert.Equal((short)25, player.Hp);
        var packet = Assert.Single(result.Packets);
        Assert.Equal(V113StatsPackets.EnableActions(), packet);
    }

    private static V113UseConsumableHandler NewHandler()
        => new(new UseItemService(new HardcodedItemEffectCatalog()));

    private static Player NewPlayer(short hp, short maxHp, short mp, short maxMp, params ItemRecord[] items)
        => new(
            new Character
            {
                Id = 1,
                Name = "UseItemUser",
                Stats = new CharacterStats
                {
                    Hp = hp,
                    MaxHp = maxHp,
                    Mp = mp,
                    MaxMp = maxMp,
                },
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private static ItemRecord UseItem(int itemId, short slot, short quantity)
        => new()
        {
            Type = (byte)InventoryType.Use,
            ItemId = itemId,
            Slot = slot,
            Quantity = quantity,
            Expiration = -1,
        };
}
