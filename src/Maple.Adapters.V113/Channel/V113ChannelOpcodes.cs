namespace Maple.Adapters.V113.Channel;

/// <summary>v113 Channel 接收 opcode。</summary>
internal static class V113ChannelRecvOp
{
    public const short PlayerLoggedIn = 0x07;
    public const short MovePlayer = 0x21;
    public const short Pong = 0x0E;
}

/// <summary>v113 Channel 送出 opcode。</summary>
internal static class V113ChannelSendOp
{
    public const short SetField = 0x7B;   // WARP_TO_MAP / initial login / map change
    public const short Ping = 0x09;
}
