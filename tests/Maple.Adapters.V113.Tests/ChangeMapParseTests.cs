using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// CHANGE_MAP (c2s 0x1E) 解析測試（腳走傳送點換圖，任務歷程 08）。
/// 對照 Java PlayerHandler.ChangeMap：byte mode + int targetId + MapleAsciiString portalName + skip(1) + short wheel。
/// </summary>
public class ChangeMapParseTests
{
    private static byte[] Body(byte mode, int targetId, string portalName, bool withTail = true)
    {
        var w = new PacketWriter(32);
        w.WriteByte(mode);
        w.WriteInt(targetId);
        w.WriteMapleString(portalName);
        if (withTail)
        {
            w.WriteByte(0);     // skip(1)
            w.WriteShort(0);    // wheel
        }
        return w.ToArray();     // 注意：不含 2-byte opcode（handler 取得 reader 時已讀掉 opcode）
    }

    [Fact]
    public void ParseChangeMap_RegularFootPortal_ReadsAllFields()
    {
        var req = V113MapPackets.ParseChangeMap(new PacketReader(Body(2, -1, "west00")));
        Assert.Equal(2, req.Mode);
        Assert.Equal(-1, req.TargetId);             // 一般腳走 portal 慣例 = -1
        Assert.Equal("west00", req.PortalName);
    }

    [Fact]
    public void ParseChangeMap_DeathRevive_ModeOne()
    {
        var req = V113MapPackets.ParseChangeMap(new PacketReader(Body(1, 0, "sp")));
        Assert.Equal(1, req.Mode);
        Assert.Equal("sp", req.PortalName);
    }

    [Fact]
    public void ParseChangeMap_ToleratesMissingTail_NoThrow()
    {
        // 某些客戶端/情境尾端可能缺 skip/wheel — Remaining 守衛應寬容不丟例外
        var req = V113MapPackets.ParseChangeMap(new PacketReader(Body(2, -1, "east00", withTail: false)));
        Assert.Equal("east00", req.PortalName);
        Assert.Equal(-1, req.TargetId);
    }
}
