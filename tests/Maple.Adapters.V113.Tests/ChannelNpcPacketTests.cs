using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// SPAWN_NPC(0xF9) / SPAWN_NPC_REQUEST_CONTROLLER(0xFB) / REMOVE_NPC(0xFA) 封包結構測試。
/// 對照 Java MaplePacketCreator.spawnNPC / spawnNPCRequestController（little-endian）。
/// 黃金向量自 Java 布局推得，待 Codex 二次交叉核對。
/// </summary>
public class ChannelNpcPacketTests
{
    // 範例 NPC：objectId=1000(0x3E8) npcId=9000000(0x895440) x=100 cy=200 f=0 fh=5 rx0=80 rx1=120
    private static Npc SampleNpc() => new(
        new MapNpc { NpcId = 9000000, X = 100, Cy = 200, F = 0, Fh = 5, Rx0 = 80, Rx1 = 120 },
        objectId: 1000);

    [Fact]
    public void SpawnNpc_MatchesGoldenVector()
    {
        var pkt = V113MapPackets.SpawnNpc(SampleNpc(), show: true);

        byte[] golden =
        {
            0xF9, 0x00,                 // opcode SPAWN_NPC
            0xE8, 0x03, 0x00, 0x00,     // objectId 1000
            0x40, 0x54, 0x89, 0x00,     // npcId 9000000
            0x64, 0x00,                 // x 100
            0xC8, 0x00,                 // cy 200
            0x01,                       // dir = (f==1?0:1) → f=0 → 1
            0x05, 0x00,                 // fh 5
            0x50, 0x00,                 // rx0 80
            0x78, 0x00,                 // rx1 120
            0x01,                       // show
        };
        Assert.Equal(golden, pkt);
    }

    [Fact]
    public void SpawnNpcRequestController_MatchesGoldenVector()
    {
        var pkt = V113MapPackets.SpawnNpcRequestController(SampleNpc(), miniMap: true);

        byte[] golden =
        {
            0xFB, 0x00,                 // opcode SPAWN_NPC_REQUEST_CONTROLLER
            0x01,                       // control flag = 1 (取得控制權)
            0xE8, 0x03, 0x00, 0x00,     // objectId 1000
            0x40, 0x54, 0x89, 0x00,     // npcId 9000000
            0x64, 0x00,                 // x 100
            0xC8, 0x00,                 // cy 200
            0x01,                       // dir
            0x05, 0x00,                 // fh 5
            0x50, 0x00,                 // rx0 80
            0x78, 0x00,                 // rx1 120
            0x01,                       // miniMap
        };
        Assert.Equal(golden, pkt);
    }

    [Fact]
    public void SpawnNpc_FacingOne_WritesDirZero()
    {
        var npc = new Npc(new MapNpc { NpcId = 1, F = 1 }, objectId: 1000);
        var pkt = V113MapPackets.SpawnNpc(npc);

        // dir byte 在 opcode(2)+objectId(4)+npcId(4)+x(2)+cy(2) = offset 14
        Assert.Equal(0x00, pkt[14]);
    }

    [Fact]
    public void RemoveNpc_BuildsCorrectStructure()
    {
        var pkt = V113MapPackets.RemoveNpc(1000);
        var r = new PacketReader(pkt);

        Assert.Equal(V113ChannelSendOp.RemoveNpc, r.ReadShort());
        Assert.Equal(1000, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }
}
