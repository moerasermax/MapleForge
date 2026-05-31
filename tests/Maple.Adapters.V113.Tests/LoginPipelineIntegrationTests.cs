using System.Net;
using System.Net.Sockets;
using Maple.Adapters.V113.Crypto;
using Maple.Adapters.V113.Login;
using Maple.Application.Accounts;
using Maple.Application.Characters;
using Maple.Application.Security;
using Maple.Core.Accounts;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// L3 合成客戶端整合測試（全自動 E2E）：真 loopback socket，
/// 假客戶端走完整 v113 握手 → 送 LOGIN_PASSWORD(帳號/密碼) → 帳密驗證(autoRegister) → 收登入成功。
/// 驗證 cipher + 握手 + framing + opcode 路由 + 帳密驗證整條管線（M1+M2-4）。
/// </summary>
public class LoginPipelineIntegrationTests
{
    /// <summary>測試用記憶體帳號庫（取代 LiteDB，專注驗證 handler+auth+封包流程）。</summary>
    private sealed class FakeAccountRepository : IAccountRepository
    {
        private readonly Dictionary<string, Account> _byName = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 1;

        public Task<Account?> FindByNameAsync(string accountName, CancellationToken ct = default)
            => Task.FromResult(_byName.TryGetValue(accountName, out var a) ? a : null);

        public Task AddAsync(Account account, CancellationToken ct = default)
        {
            account.Id = _nextId++;
            _byName[account.AccountName] = account;
            return Task.CompletedTask;
        }

        public Task<bool> TryAddAsync(Account account, CancellationToken ct = default)
        {
            if (_byName.ContainsKey(account.AccountName))
                return Task.FromResult(false);
            account.Id = _nextId++;
            _byName[account.AccountName] = account;
            return Task.FromResult(true);
        }

        public Task UpdateAsync(Account account, CancellationToken ct = default)
        {
            _byName[account.AccountName] = account;
            return Task.CompletedTask;
        }
    }

    /// <summary>測試用記憶體角色庫。</summary>
    private sealed class FakeCharacterRepository : ICharacterRepository
    {
        private readonly Dictionary<int, Character> _byId = new();
        private readonly Dictionary<string, Character> _byName = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 1;

        public Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default)
        {
            IReadOnlyList<Character> list = _byId.Values.Where(c => c.AccountId == accountId).ToList();
            return Task.FromResult(list);
        }

        public Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default)
            => Task.FromResult(_byId.TryGetValue(characterId, out var c) ? c : null);

        public Task<Character?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_byName.TryGetValue(name, out var c) ? c : null);

        public Task AddAsync(Character character, CancellationToken ct = default)
        {
            character.Id = _nextId++;
            _byId[character.Id] = character;
            _byName[character.Name] = character;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Character character, CancellationToken ct = default)
        {
            _byId[character.Id] = character;
            _byName[character.Name] = character;
            return Task.CompletedTask;
        }
    }

    private static V113LoginConnectionHandler BuildHandler(
        AuthService auth,
        CharacterService? charService = null,
        V113LoginOptions? opts = null)
    {
        charService ??= new CharacterService(new FakeCharacterRepository());
        opts ??= new V113LoginOptions(AutoRegister: true);
        return new V113LoginConnectionHandler(
            NullLogger<V113LoginConnectionHandler>.Instance, auth, charService, opts);
    }

    [Fact]
    public async Task Loopback_Handshake_Login_AutoRegister_ReturnsAuthSuccess()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var auth = new AuthService(new FakeAccountRepository(), new BcryptPasswordHasher());
        var handler = BuildHandler(auth);

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

        // 1) 讀 getHello（未加密 [len][payload]）→ 取 recvIv/sendIv
        var lenBuf = new byte[2];
        await stream.ReadExactlyAsync(lenBuf, cts.Token);
        int payloadLen = lenBuf[0] | (lenBuf[1] << 8);
        var payload = new byte[payloadLen];
        await stream.ReadExactlyAsync(payload, cts.Token);

        int pos = 0;
        short version = (short)(payload[pos] | (payload[pos + 1] << 8)); pos += 2;
        int patchLen = payload[pos] | (payload[pos + 1] << 8); pos += 2 + patchLen;
        var recvIv = payload.AsSpan(pos, 4).ToArray(); pos += 4;
        var sendIv = payload.AsSpan(pos, 4).ToArray(); pos += 4;
        Assert.Equal((short)113, version);

        // 2) 鏡像 cipher
        var clientSend = new MapleAesOfb(recvIv, 113);
        var clientRecv = new MapleAesOfb(sendIv, unchecked((short)(0xFFFF - 113)));

        // 3) 送 LOGIN_PASSWORD：[short 0x01][maple 帳號][maple 密碼]
        var body = new PacketWriter(32)
            .WriteShort(V113RecvOp.LoginPassword)
            .WriteMapleString("testuser")
            .WriteMapleString("testpass")
            .ToArray();
        var frame = new byte[body.Length + 4];
        clientSend.WriteHeader(frame.AsSpan(0, 4), body.Length);
        clientSend.Crypt(body);
        body.CopyTo(frame.AsSpan(4));
        await stream.WriteAsync(frame, cts.Token);
        await stream.FlushAsync(cts.Token);

        // 4) 讀回應並解密：應為 getAuthSuccess（LOGIN_STATUS=0x00, type=0, 後接 accId 等）
        var rh = new byte[4];
        await stream.ReadExactlyAsync(rh, cts.Token);
        Assert.True(clientRecv.Check(rh), "回應封包頭驗證失敗");
        int rlen = clientRecv.ReadLength(rh);
        var rbody = new byte[rlen];
        await stream.ReadExactlyAsync(rbody, cts.Token);
        clientRecv.Crypt(rbody);

        short respOp = (short)(rbody[0] | (rbody[1] << 8));
        Assert.Equal((short)0x00, respOp);   // LOGIN_STATUS
        Assert.Equal(0, rbody[2]);           // type = 0（成功；登入失敗則為 reason 3/4/5）
        Assert.True(rbody.Length > 6, "auth success 封包應比 login-failed 長（含 accId/帳號）");

        client.Close();
        await serverTask;
    }

    [Fact]
    public async Task Loopback_AfterLogin_ServerlistRequest_ReturnsServerlistAndEnd()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var auth = new AuthService(new FakeAccountRepository(), new BcryptPasswordHasher());
        var handler = BuildHandler(auth, opts: new V113LoginOptions(
            AutoRegister: true, WorldName: "TestWorld", ChannelCount: 2));

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

        // 1) 讀 getHello，取 IV
        var lenBuf = new byte[2];
        await stream.ReadExactlyAsync(lenBuf, cts.Token);
        var payload = new byte[lenBuf[0] | (lenBuf[1] << 8)];
        await stream.ReadExactlyAsync(payload, cts.Token);

        int pos = 2; // skip version
        int patchLen = payload[pos] | (payload[pos + 1] << 8); pos += 2 + patchLen;
        var recvIv = payload.AsSpan(pos, 4).ToArray(); pos += 4;
        var sendIv = payload.AsSpan(pos, 4).ToArray();

        var clientSend = new MapleAesOfb(recvIv, 113);
        var clientRecv = new MapleAesOfb(sendIv, unchecked((short)(0xFFFF - 113)));

        // 2) 送 LOGIN_PASSWORD（autoRegister 建帳）
        await SendEncryptedAsync(stream, clientSend,
            new PacketWriter(32)
                .WriteShort(V113RecvOp.LoginPassword)
                .WriteMapleString("worlduser")
                .WriteMapleString("worldpass")
                .ToArray(), cts.Token);

        // 3) 讀 AuthSuccess，丟棄
        await ReadDecryptedAsync(stream, clientRecv, cts.Token);

        // 4) 送 SERVERLIST_REQUEST
        await SendEncryptedAsync(stream, clientSend,
            new PacketWriter(2).WriteShort(V113RecvOp.ServerlistRequest).ToArray(), cts.Token);

        // 5) 讀回 SERVERLIST（world entry）
        var slPkt = await ReadDecryptedAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)V113SendOp.Serverlist, (short)(slPkt[0] | (slPkt[1] << 8)));
        Assert.Equal(0, slPkt[2]); // world id 0
        Assert.NotEqual(0xFF, slPkt[2]); // 不是結束標記

        // 6) 讀回 EndOfServerList（0xFF 結束）
        var eolPkt = await ReadDecryptedAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)V113SendOp.Serverlist, (short)(eolPkt[0] | (eolPkt[1] << 8)));
        Assert.Equal(0xFF, eolPkt[2]); // end marker

        // 7) 送 CHARLIST_REQUEST：[opcode][byte unknown][byte world][byte channel]
        await SendEncryptedAsync(stream, clientSend,
            new PacketWriter(5)
                .WriteShort(V113RecvOp.CharlistRequest)
                .WriteByte(0)   // unknown
                .WriteByte(0)   // world id
                .WriteByte(0)   // channel id (0→ selectedChannel=1)
                .ToArray(), cts.Token);

        // 8) 讀回 CHARLIST
        var clPkt = await ReadDecryptedAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)V113SendOp.Charlist, (short)(clPkt[0] | (clPkt[1] << 8)));
        Assert.Equal(0, clPkt[7]); // character count = 0

        client.Close();
        await serverTask;
    }

    // ── 測試輔助 ──────────────────────────────────────────────────────────────

    private static async Task SendEncryptedAsync(
        NetworkStream stream, MapleAesOfb cipher, byte[] body, CancellationToken ct)
    {
        var frame = new byte[body.Length + 4];
        cipher.WriteHeader(frame.AsSpan(0, 4), body.Length);
        cipher.Crypt(body);
        body.CopyTo(frame.AsSpan(4));
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<byte[]> ReadDecryptedAsync(
        NetworkStream stream, MapleAesOfb cipher, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        int len = cipher.ReadLength(header);
        var body = new byte[len];
        await stream.ReadExactlyAsync(body, ct);
        cipher.Crypt(body);
        return body;
    }

    [Fact]
    public async Task Loopback_CreateChar_CharNameCheck_CreateSuccess_CharlistShowsChar()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var charRepo = new FakeCharacterRepository();
        var auth = new AuthService(new FakeAccountRepository(), new BcryptPasswordHasher());
        var charService = new CharacterService(charRepo);
        var handler = BuildHandler(auth, charService);

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

        // 1) 握手 + 取 IV
        var lenBuf = new byte[2];
        await stream.ReadExactlyAsync(lenBuf, cts.Token);
        var hpayload = new byte[lenBuf[0] | (lenBuf[1] << 8)];
        await stream.ReadExactlyAsync(hpayload, cts.Token);
        int hpos = 2;
        int plen = hpayload[hpos] | (hpayload[hpos + 1] << 8); hpos += 2 + plen;
        var recvIv = hpayload.AsSpan(hpos, 4).ToArray(); hpos += 4;
        var sendIv = hpayload.AsSpan(hpos, 4).ToArray();
        var clientSend = new MapleAesOfb(recvIv, 113);
        var clientRecv = new MapleAesOfb(sendIv, unchecked((short)(0xFFFF - 113)));

        // 2) LOGIN_PASSWORD（autoRegister）
        await SendEncryptedAsync(stream, clientSend,
            new PacketWriter(32).WriteShort(0x01).WriteMapleString("charuser").WriteMapleString("charpass").ToArray(), cts.Token);
        var authPkt = await ReadDecryptedAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)0x00, (short)(authPkt[0] | (authPkt[1] << 8)));
        Assert.Equal(0, authPkt[2]); // success

        // 3) CHECK_CHAR_NAME（名稱可用）
        await SendEncryptedAsync(stream, clientSend,
            new PacketWriter(16).WriteShort(V113RecvOp.CheckCharName).WriteMapleString("TestChar").ToArray(), cts.Token);
        var namePkt = await ReadDecryptedAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)V113SendOp.CharNameResponse, (short)(namePkt[0] | (namePkt[1] << 8)));
        Assert.Equal(0, namePkt[namePkt.Length - 1]); // 0=available

        // 4) CREATE_CHAR（Explorer job=1）
        await SendEncryptedAsync(stream, clientSend,
            new PacketWriter(64)
                .WriteShort(V113RecvOp.CreateChar)
                .WriteMapleString("TestChar")
                .WriteInt(1)       // Explorer
                .WriteInt(20100)   // face
                .WriteInt(30030)   // hair
                .WriteInt(1040002) // top
                .WriteInt(1060002) // bottom
                .WriteInt(1072001) // shoes
                .WriteInt(1302000) // weapon
                .ToArray(), cts.Token);
        var newCharPkt = await ReadDecryptedAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)V113SendOp.AddNewCharEntry, (short)(newCharPkt[0] | (newCharPkt[1] << 8)));
        Assert.Equal(0, newCharPkt[2]); // 0=success

        // 5) SERVERLIST_REQUEST → 世界選單
        await SendEncryptedAsync(stream, clientSend,
            new PacketWriter(2).WriteShort(V113RecvOp.ServerlistRequest).ToArray(), cts.Token);
        await ReadDecryptedAsync(stream, clientRecv, cts.Token); // SERVERLIST
        await ReadDecryptedAsync(stream, clientRecv, cts.Token); // EndOfServerList

        // 6) CHARLIST_REQUEST → 角色列表（應有 1 個角色）
        await SendEncryptedAsync(stream, clientSend,
            new PacketWriter(5).WriteShort(V113RecvOp.CharlistRequest).WriteByte(0).WriteByte(0).WriteByte(0).ToArray(), cts.Token);
        var charlistPkt = await ReadDecryptedAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)V113SendOp.Charlist, (short)(charlistPkt[0] | (charlistPkt[1] << 8)));
        Assert.Equal(1, charlistPkt[7]); // 1 個角色

        client.Close();
        await serverTask;
    }

    [Fact]
    public void V113ReceiveVersion_IsCorrect()
    {
        // 防呆：確保 send/recv 版本常數沒被改動
        Assert.Equal((short)0x01, V113RecvOp.LoginPassword);
        Assert.Equal((short)0x00, V113SendOp.LoginStatus);
    }
}
