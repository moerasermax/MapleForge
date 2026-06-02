using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>移動解析結果：最終座標/朝向(stance)/foothold 與指令數（供 server 追蹤玩家位置）。</summary>
public readonly record struct MovementResult(short X, short Y, byte Stance, short Foothold, int Commands);

/// <summary>
/// v113 移動指令串解析（逐欄對照舊 Java <c>MovementParse.parseMovement</c>）。
/// 輸入＝MOVE_PLAYER 封包中「movement list」起點（byte numCommands 開頭）。
/// 只抽取 server 需要的最終位置/stance/foothold（不保留每個 fragment 物件）；
/// 各 command 型別的欄位長度需精確消費，否則後續解析錯位。
/// </summary>
internal static class V113MovementParser
{
    /// <summary>解析移動串，回傳最終位置等。讀取量＝整個 movement list。</summary>
    public static MovementResult Parse(PacketReader r)
    {
        byte numCommands = r.ReadByte();
        short x = 0, y = 0, foothold = 0;
        byte stance = 0;

        for (var i = 0; i < numCommands; i++)
        {
            byte command = r.ReadByte();
            switch (command)
            {
                // normal move / float（有座標；command 15 多一個 foothold）
                case 0: case 5: case 15: case 17:
                    x = r.ReadShort();           // xpos
                    y = r.ReadShort();           // ypos
                    r.ReadShort();               // xwobble
                    r.ReadShort();               // ywobble
                    r.ReadShort();               // unk
                    if (command == 15) foothold = r.ReadShort();
                    stance = r.ReadByte();       // newstate
                    r.ReadShort();               // duration
                    break;

                // 無座標（jump/teleport 等，只有 wobble + state + duration）
                case 1: case 2: case 6: case 12: case 13:
                case 16: case 18: case 19: case 20: case 22:
                    r.ReadShort();               // xwobble
                    r.ReadShort();               // ywobble
                    stance = r.ReadByte();       // newstate
                    r.ReadShort();               // duration
                    break;

                // 有座標、無 wobble/duration（瞬移類）
                case 3: case 4: case 7: case 8: case 9: case 11:
                    x = r.ReadShort();           // xpos
                    y = r.ReadShort();           // ypos
                    r.ReadShort();               // unk
                    stance = r.ReadByte();       // newstate
                    r.ReadShort();               // duration
                    break;

                // change equip：只有 1 byte
                case 10:
                    r.ReadByte();                // wui
                    break;

                // wobble + foothold + state + duration
                case 14:
                    r.ReadShort();               // xwobble
                    r.ReadShort();               // ywobble
                    foothold = r.ReadShort();    // fh
                    stance = r.ReadByte();       // newstate
                    r.ReadShort();               // duration
                    break;

                // 其餘：state + duration
                default:
                    stance = r.ReadByte();       // newstate
                    r.ReadShort();               // duration
                    break;
            }
        }

        return new MovementResult(x, y, stance, foothold, numCommands);
    }
}
