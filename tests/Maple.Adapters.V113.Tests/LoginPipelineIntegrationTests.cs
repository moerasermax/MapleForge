using System.Net;
using System.Net.Sockets;
using Maple.Adapters.V113.Crypto;
using Maple.Adapters.V113.Login;
using Maple.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// L3 合成客戶端整合測試（全自動 E2E）：真 loopback socket，
/// 假客戶端走完整 v113 握手 → 送 LOGIN_PASSWORD → 收登入失敗。
/// 驗證 cipher + 握手 + framing + opcode 路由整條管線接線正確。
/// （cipher 的 bit 級正確性已由 L2 黃金測試獨立保證。）
/// </summary>
public class LoginPipelineIntegrationTests
{
    [Fact]
    public async Task Loopback_Handshake_Login_ReturnsLoginFailed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var handler = new V113LoginConnectionHandler(NullLogger<V113LoginConnectionHandler>.Instance);

        var serverTask = Task.Run(async () =>
        {
            var sock = await listener.AcceptSocketAsync(cts.Token);
            var session = new MapleSession(sock, NullLogger<MapleSession>.Instance);
            await using (session)
                await handler.HandleConnectionAsync(session, cts.Token);
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        var stream = client.GetStream();

        // 1) 讀 getHello：[short payloadLen][payload]
        var lenBuf = new byte[2];
        await stream.ReadExactlyAsync(lenBuf, cts.Token);
        int payloadLen = lenBuf[0] | (lenBuf[1] << 8);
        var payload = new byte[payloadLen];
        await stream.ReadExactlyAsync(payload, cts.Token);

        // payload: version(2) | patch(short len + bytes) | recvIv(4) | sendIv(4) | locale(1)
        int pos = 0;
        short version = (short)(payload[pos] | (payload[pos + 1] << 8)); pos += 2;
        int patchLen = payload[pos] | (payload[pos + 1] << 8); pos += 2 + patchLen;
        var recvIv = payload.AsSpan(pos, 4).ToArray(); pos += 4;
        var sendIv = payload.AsSpan(pos, 4).ToArray(); pos += 4;
        byte locale = payload[pos];

        Assert.Equal((short)113, version);
        Assert.Equal(6, locale);

        // 2) 鏡像 cipher：client.send 對應 server.recv（recvIv,113）；client.recv 對應 server.send（sendIv,0xFFFF-113）
        var clientSend = new MapleAesOfb(recvIv, 113);
        var clientRecv = new MapleAesOfb(sendIv, unchecked((short)(0xFFFF - 113)));

        // 3) 送 LOGIN_PASSWORD（opcode 0x01 + dummy）：[4-byte header][crypt body]
        var body = new byte[] { 0x01, 0x00, 0xAA, 0xBB };
        var frame = new byte[body.Length + 4];
        clientSend.WriteHeader(frame.AsSpan(0, 4), body.Length);
        clientSend.Crypt(body);
        body.CopyTo(frame.AsSpan(4));
        await stream.WriteAsync(frame, cts.Token);
        await stream.FlushAsync(cts.Token);

        // 4) 讀回應並解密：應為 LOGIN_STATUS(0x00) + reason 5
        var rh = new byte[4];
        await stream.ReadExactlyAsync(rh, cts.Token);
        Assert.True(clientRecv.Check(rh), "回應封包頭驗證失敗");
        int rlen = clientRecv.ReadLength(rh);
        var rbody = new byte[rlen];
        await stream.ReadExactlyAsync(rbody, cts.Token);
        clientRecv.Crypt(rbody);

        short respOp = (short)(rbody[0] | (rbody[1] << 8));
        Assert.Equal((short)0x00, respOp);   // LOGIN_STATUS
        Assert.Equal(5, rbody[2]);           // reason = 未註冊帳號

        client.Close(); // 通知 server EOF
        await serverTask;
    }
}
