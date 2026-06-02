using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// V113MovementParser 單元測試（對照舊 Java MovementParse.parseMovement）。
/// 重點：各 command 型別欄位長度精確消費、抽出最終位置/stance/foothold。
/// </summary>
public class MovementParserTests
{
    /// <summary>little-endian 位元組組裝小工具。</summary>
    private sealed class Mv
    {
        private readonly List<byte> _b = new();
        public Mv Byte(int v) { _b.Add((byte)v); return this; }
        public Mv Short(int v) { _b.Add((byte)(v & 0xFF)); _b.Add((byte)((v >> 8) & 0xFF)); return this; }
        public byte[] ToArray() => _b.ToArray();
    }

    [Fact]
    public void NormalMove_Command0_ParsesPositionAndConsumesExactly()
    {
        // numCommands=1, cmd=0, xpos=100, ypos=200, xwob=0, ywob=0, unk=0, newstate=3, duration=10
        var bytes = new Mv().Byte(1).Byte(0)
            .Short(100).Short(200).Short(0).Short(0).Short(0).Byte(3).Short(10)
            .ToArray();
        var r = new PacketReader(bytes);

        var m = V113MovementParser.Parse(r);

        Assert.Equal((short)100, m.X);
        Assert.Equal((short)200, m.Y);
        Assert.Equal((byte)3, m.Stance);
        Assert.Equal(1, m.Commands);
        Assert.Equal(0, r.Remaining); // 精確消費整個 movement list
    }

    [Fact]
    public void Command15_ReadsExtraFoothold()
    {
        // cmd=15 多一個 fh：xpos=50,ypos=60,xwob,ywob,unk,fh=7,newstate=2,duration=5
        var bytes = new Mv().Byte(1).Byte(15)
            .Short(50).Short(60).Short(0).Short(0).Short(0).Short(7).Byte(2).Short(5)
            .ToArray();
        var r = new PacketReader(bytes);

        var m = V113MovementParser.Parse(r);

        Assert.Equal((short)50, m.X);
        Assert.Equal((short)60, m.Y);
        Assert.Equal((short)7, m.Foothold);
        Assert.Equal((byte)2, m.Stance);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void MultiCommand_LastPositionAndStanceWin()
    {
        // cmd0(pos 10,20,state1) → cmd1(無座標,state4)。最終位置取最後有座標者，stance 取最後。
        var bytes = new Mv().Byte(2)
            .Byte(0).Short(10).Short(20).Short(0).Short(0).Short(0).Byte(1).Short(0)   // cmd0
            .Byte(1).Short(0).Short(0).Byte(4).Short(0)                                 // cmd1
            .ToArray();
        var r = new PacketReader(bytes);

        var m = V113MovementParser.Parse(r);

        Assert.Equal((short)10, m.X);
        Assert.Equal((short)20, m.Y);
        Assert.Equal((byte)4, m.Stance); // 最後一個 fragment 的 newstate
        Assert.Equal(2, m.Commands);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Command10_ChangeEquip_ConsumesSingleByte()
    {
        // cmd=10 只讀 1 byte(wui)；接著 cmd0 帶位置確認沒錯位
        var bytes = new Mv().Byte(2)
            .Byte(10).Byte(0xAB)                                                        // cmd10: wui
            .Byte(0).Short(7).Short(8).Short(0).Short(0).Short(0).Byte(5).Short(0)       // cmd0
            .ToArray();
        var r = new PacketReader(bytes);

        var m = V113MovementParser.Parse(r);

        Assert.Equal((short)7, m.X);
        Assert.Equal((short)8, m.Y);
        Assert.Equal((byte)5, m.Stance);
        Assert.Equal(0, r.Remaining); // cmd10 只吃 1 byte，沒錯位
    }
}
