using Maple.Adapters.V113.Crypto;
using Maple.Net;
using Maple.Versioning;
using Microsoft.Extensions.Logging;

namespace Maple.Adapters.V113.Login;

/// <summary>
/// M1 的 v113 登入連線處理：送握手 → 啟用 cipher → 收 LOGIN_PASSWORD → 回登入失敗。
/// 目標＝證明 cipher+握手+framing 整條管線端到端正確（客戶端顯示登入失敗）。
/// </summary>
public sealed class V113LoginConnectionHandler : IConnectionHandler
{
    private readonly IVersionCipherFactory _ciphers = new V113CipherFactory();
    private readonly ILogger<V113LoginConnectionHandler> _log;

    public V113LoginConnectionHandler(ILogger<V113LoginConnectionHandler> log) => _log = log;

    public async Task HandleConnectionAsync(MapleSession session, CancellationToken ct)
    {
        // IV：對照舊碼，前 3 byte 固定、末 byte 隨機。
        byte[] recvIv = { 0x46, 0x72, 0x7A, (byte)Random.Shared.Next(256) };
        byte[] sendIv = { 0x52, 0x30, 0x78, (byte)Random.Shared.Next(256) };

        var hello = V113LoginPackets.Hello(recvIv, sendIv);
        await session.SendRawAsync(hello, ct);

        var (recv, send) = _ciphers.CreateSessionPair(recvIv, sendIv);
        session.SetCiphers(recv, send);
        _log.LogInformation("[v113] 握手送出（getHello {Bytes} bytes），cipher 啟用 {Remote}", hello.Length, session.Remote);

        await session.RunAsync(OnPacketAsync, ct);
    }

    private async Task OnPacketAsync(byte[] body, MapleSession session, CancellationToken ct)
    {
        if (body.Length < 2) return;

        short opcode = (short)(body[0] | (body[1] << 8));
        _log.LogInformation("[v113] 解密封包 opcode=0x{Op:X2} len={Len} {Remote}", opcode, body.Length, session.Remote);

        if (opcode == V113RecvOp.LoginPassword)
        {
            _log.LogInformation("[v113] ✓ 收到並解密 LOGIN_PASSWORD → 回登入失敗(5) {Remote}（管線打通）", session.Remote);
            await session.SendAsync(V113LoginPackets.LoginFailed(5), ct);
        }
    }
}
