using System.Net;
using System.Net.Sockets;
using Maple.Adapters.V113.Crypto;
using Maple.Net;
using Maple.Versioning;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple.Net.Tests;

/// <summary>
/// MapleSession 送出路徑回歸測試（效能稽核 P0，任務歷程 07）。
/// 用 loopback socket pair 起真連線，server 端 MapleSession 加密送出、
/// 測試端用相同初始狀態的 cipher 解密，驗證：
///   ①廣播同一 byte[] 給多個 session 時每人都正確（不再因原地加密共用 buffer 損壞第 2+ 收件人）；
///   ②呼叫者的 plaintext 入參不被 mutate；
///   ③多封包入列順序 == 送出順序（cipher IV 由單一 pump 序列化推進，兩端持續同步）。
/// </summary>
public sealed class MapleSessionSendTests
{
    // 一條測試連線：server 端 MapleSession + 測試端解密 cipher/stream。
    private sealed class Link : IAsyncDisposable
    {
        public required MapleSession Server { get; init; }
        public required NetworkStream ClientStream { get; init; }
        public required IPacketCipher ClientRecv { get; init; }   // 與 server send cipher 同初始狀態
        private readonly List<IAsyncDisposable> _ownedAsync = new();
        private readonly List<IDisposable> _owned = new();

        public void Own(IAsyncDisposable d) => _ownedAsync.Add(d);
        public void Own(IDisposable d) => _owned.Add(d);

        /// <summary>讀一個完整 frame（4-byte 頭 + body），驗頭、解密 body 後回傳 plaintext。</summary>
        public async Task<byte[]> ReadDecryptedAsync()
        {
            var header = new byte[4];
            await ClientStream.ReadExactlyAsync(header);
            Assert.True(ClientRecv.Check(header), "封包頭驗證失敗（cipher 不同步＝frame 損壞）");
            int len = ClientRecv.ReadLength(header);
            var body = new byte[len];
            await ClientStream.ReadExactlyAsync(body);
            ClientRecv.Crypt(body);
            return body;
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            foreach (var d in _ownedAsync) await d.DisposeAsync();
            foreach (var d in _owned) d.Dispose();
        }
    }

    private static async Task<Link> ConnectAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var client = new TcpClient();
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        var serverSocket = await listener.AcceptSocketAsync();
        await connectTask;
        listener.Stop();

        // 每連線一組獨立 IV（cipher 實例各自演化）。
        byte[] recvIv = [0x12, 0x34, 0x56, 0x78];
        byte[] sendIv = [0x9A, 0xBC, 0xDE, 0xF0];
        var factory = new V113CipherFactory();
        var (recv, send) = factory.CreateSessionPair(recvIv, sendIv);
        // 測試端解密用：與 server 的 send cipher 同初始狀態（同 sendIv）。
        var (_, clientDecrypt) = factory.CreateSessionPair(recvIv, sendIv);

        var session = new MapleSession(serverSocket, NullLogger<MapleSession>.Instance);
        session.SetCiphers(recv, send);   // 啟用 cipher + 啟動 outbound pump

        var link = new Link
        {
            Server = session,
            ClientStream = client.GetStream(),
            ClientRecv = clientDecrypt,
        };
        link.Own(client);
        return link;
    }

    private static byte[] MakePayload(int seed, int len = 32)
    {
        var p = new byte[len];
        for (int i = 0; i < len; i++) p[i] = (byte)(seed * 31 + i * 7 + 3);
        return p;
    }

    [Fact]
    public async Task Broadcast_SamePacketToTwoSessions_BothDecryptCorrectly_AndInputNotMutated()
    {
        await using var a = await ConnectAsync();
        await using var b = await ConnectAsync();

        // 模擬 BroadcastPacketToOthersAsync：建一個封包，把「同一個 byte[]」送給多個 session。
        byte[] packet = MakePayload(seed: 1);
        byte[] pristine = (byte[])packet.Clone();

        await a.Server.SendAsync(packet, default);
        await b.Server.SendAsync(packet, default);

        byte[] gotA = await a.ReadDecryptedAsync();
        byte[] gotB = await b.ReadDecryptedAsync();

        // 修補前：第 2 收件人 (b) 對「已被 a 加密過的 bytes」再加密 → 損壞，這裡會不相等。
        Assert.Equal(pristine, gotA);
        Assert.Equal(pristine, gotB);
        // 送出不可 mutate 呼叫者的 plaintext（廣播會重複用同一 byte[]）。
        Assert.Equal(pristine, packet);
    }

    [Fact]
    public async Task Send_MultiplePackets_ArriveInOrder_AndDecryptCorrectly()
    {
        await using var link = await ConnectAsync();

        var sent = new List<byte[]>();
        for (int i = 0; i < 16; i++)
        {
            var p = MakePayload(seed: i + 2, len: 10 + i);
            sent.Add((byte[])p.Clone());
            await link.Server.SendAsync(p, default);
        }

        for (int i = 0; i < sent.Count; i++)
        {
            byte[] got = await link.ReadDecryptedAsync();
            Assert.Equal(sent[i], got);   // 順序 + 內容 + IV 同步
        }
    }
}
