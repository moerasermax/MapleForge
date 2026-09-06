using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelRingPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaPropertiesAndDocumentedCandidates()
    {
        Assert.Equal(0x81, V113RingPackets.RingActionRecvOpcode);
        Assert.Equal(0x41, V113RingPackets.MarriageRequestSendOpcode);
        Assert.Equal(0x42, V113RingPackets.MarriageResultSendOpcode);
        Assert.Equal(0x62, V113RingPackets.MarriageUpdateSendOpcode);
        Assert.Equal(unchecked((short)0xBF), V113RingPackets.ShowForeignEffectSendOpcode);
    }

    [Fact]
    public void ParseProposal_ReadsJavaModeZeroLayout()
    {
        var body = new PacketWriter()
            .WriteByte(0)
            .WriteMapleString("Target")
            .WriteInt(2240004)
            .ToArray();

        var request = V113RingPackets.ParseRingAction(new PacketReader(body));

        Assert.True(request.IsProposal);
        Assert.Equal("Target", request.TargetName);
        Assert.Equal(2240004, request.ItemId);
    }

    [Fact]
    public void ParseReply_ReadsAcceptedNameAndCharacterId()
    {
        var body = new PacketWriter()
            .WriteByte(2)
            .WriteByte(1)
            .WriteMapleString("Proposer")
            .WriteInt(1234)
            .ToArray();

        var request = V113RingPackets.ParseRingAction(new PacketReader(body));

        Assert.True(request.IsReply);
        Assert.True(request.Accepted);
        Assert.Equal("Proposer", request.TargetName);
        Assert.Equal(1234, request.CharacterId);
    }

    [Fact]
    public void MarriageRequest_WritesJavaLayout()
    {
        var packet = V113RingPackets.MarriageRequest("Alice", 1001);

        byte[] expected =
        {
            0x41, 0x00,
            0x00,
            0x05, 0x00,
            0x41, 0x6C, 0x69, 0x63, 0x65,
            0xE9, 0x03, 0x00, 0x00,
        };
        Assert.Equal(expected, packet);
    }

    [Fact]
    public void MarriageResultError_WritesMessageOnlyLayout()
    {
        Assert.Equal(new byte[] { 0x42, 0x00, 0x12 }, V113RingPackets.MarriageResult(0x12));
    }

    [Fact]
    public void MarriageRingLook_WritesAddMarriageRingLookFragment()
    {
        var character = new Character
        {
            Id = 1001,
            Name = "Alice",
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Equip,
                    IsEquip = true,
                    ItemId = 1112300,
                    Slot = 1,
                    Quantity = 1,
                },
            ],
        };
        var player = new Player(character, new Position(0, 0, 0, 0));
        Assert.True(player.WearMarriageRing(1002, 1112300, 30001));

        var packet = V113RingPackets.MarriageRingLook(player);

        byte[] expected =
        {
            0x01,
            0xE9, 0x03, 0x00, 0x00,
            0xEA, 0x03, 0x00, 0x00,
            0x31, 0x75, 0x00, 0x00,
        };
        Assert.Equal(expected, packet);
    }

    [Fact]
    public void SpawnPlayer_WithVisibleMarriageRing_EmbedsRingLookBytes()
    {
        // P047：V113MapPackets.SpawnPlayer 先前恆寫 0（無戒指），這裡驗證戴戒指時
        // 有正確嵌入 addMarriageRingLook 片段（角色 ID + 對象 ID + 戒指 ID）。
        var character = new Character
        {
            Id = 1001,
            Name = "Alice",
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Equip,
                    IsEquip = true,
                    ItemId = 1112300,
                    Slot = 1,
                    Quantity = 1,
                },
            ],
        };
        var player = new Player(character, new Position(0, 0, 0, 0));
        Assert.True(player.WearMarriageRing(1002, 1112300, 30001));

        var packet = V113MapPackets.SpawnPlayer(player, 10, 20, 0, 30);
        var expectedFragment = V113RingPackets.MarriageRingLook(player);

        var packetSpan = packet.AsSpan();
        var index = -1;
        for (var i = 0; i <= packetSpan.Length - expectedFragment.Length; i++)
        {
            if (packetSpan.Slice(i, expectedFragment.Length).SequenceEqual(expectedFragment))
            {
                index = i;
                break;
            }
        }

        Assert.True(index >= 0, "SpawnPlayer 封包應包含 MarriageRingLook 片段");
    }

    [Fact]
    public void SpawnPlayer_WithoutMarriageRing_PacketIsExactlyRingFragmentShorter()
    {
        // 無戒指跟有戒指的兩包，除了 MarriageRingLook 片段（1 vs 13 bytes）外其餘欄位
        // 完全相同（兩邊都裝備同一枚戒指道具，只差在是否呼叫 WearMarriageRing 記錄配對
        // 狀態，AddCharLook 等其他欄位因此不受影響），用長度差精確驗證接線正確。
        static Character NewCharacterWithRingEquipped() => new()
        {
            Id = 1001,
            Name = "Alice",
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Equip,
                    IsEquip = true,
                    ItemId = 1112300,
                    Slot = 1,
                    Quantity = 1,
                },
            ],
        };

        var noRingPlayer = new Player(NewCharacterWithRingEquipped(), new Position(0, 0, 0, 0));
        var noRingPacket = V113MapPackets.SpawnPlayer(noRingPlayer, 10, 20, 0, 30);

        var ringPlayer = new Player(NewCharacterWithRingEquipped(), new Position(0, 0, 0, 0));
        Assert.True(ringPlayer.WearMarriageRing(1002, 1112300, 30001));
        var ringPacket = V113MapPackets.SpawnPlayer(ringPlayer, 10, 20, 0, 30);

        Assert.Equal(noRingPacket.Length + 12, ringPacket.Length);
    }
}
