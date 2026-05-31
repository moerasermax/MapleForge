namespace Maple.Adapters.V113.Login;

/// <summary>v113 接收 opcode（來源 recv.properties）。</summary>
internal static class V113RecvOp
{
    public const short LoginPassword = 0x01;
    public const short ServerlistRequest = 0x03;
    public const short CharlistRequest = 0x04;
    public const short Pong = 0x0E;
}

/// <summary>v113 送出 opcode（來源 send.properties）。</summary>
internal static class V113SendOp
{
    public const short LoginStatus = 0x00;
    public const short Ping = 0x09;
}
