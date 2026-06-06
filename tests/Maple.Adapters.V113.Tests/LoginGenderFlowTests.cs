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
/// 性別選擇 + 第二密碼(PIN) 登入流程測試。
/// 對照 Java CharLoginHandler.handleLogin gate（gender==10 || secondPassword==null → CHOOSE_GENDER）
/// 與 SetGenderRequest handler（SET_GENDER 0x19 → GENDER_SET 0x15 → 客戶端重新登入 → AuthSuccess）。
/// </summary>
public class LoginGenderFlowTests
{
    // ── 封包位元組布局單元測試（對照 Java LoginPacket）────────────────────────

    [Fact]
    public void ChooseGender_ByteLayout_MatchesJava()
    {
        // Java getGenderNeeded: writeShort(CHOOSE_GENDER=0x14) + writeMapleAsciiString(accountName)
        var pkt = V113LoginPackets.ChooseGender("testuser");
        var r = new PacketReader(pkt);
        Assert.Equal((short)0x14, r.ReadShort());           // CHOOSE_GENDER opcode
        Assert.Equal("testuser", r.ReadMapleString());
    }

    [Fact]
    public void GenderSet_ByteLayout_MatchesJava()
    {
        // Java getGenderChanged: writeShort(GENDER_SET=0x15) + writeMapleAsciiString(accountName)
        var pkt = V113LoginPackets.GenderSet("testuser");
        var r = new PacketReader(pkt);
        Assert.Equal((short)0x15, r.ReadShort());           // GENDER_SET opcode
        Assert.Equal("testuser", r.ReadMapleString());
    }

    [Fact]
    public void AuthSuccess_GenderByte_AtCorrectPosition()
    {
        // Java getAuthSuccessRequest: opcode(2) + type(1) + accId(4) + gender(1) + ...
        var pkt = V113LoginPackets.AuthSuccess(42, "alice", gender: 1);
        var r = new PacketReader(pkt);
        Assert.Equal((short)0x00, r.ReadShort());   // LOGIN_STATUS
        Assert.Equal(0, r.ReadByte());              // type=0 success
        Assert.Equal(42, r.ReadInt());              // accountId
        Assert.Equal(1, r.ReadByte());              // gender=1（女）
    }

    // ── E2E loopback 整合測試：完整 gender 設定流程 ───────────────────────────

    /// <summary>新帳號 autoRegister：Gender=10 → CHOOSE_GENDER → SET_GENDER → GENDER_SET → re-login → AuthSuccess。</summary>
    [Fact]
    public async Task NewAccount_GenderFlow_EndToEnd_ReturnsAuthSuccess()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // 使用「不 auto-set gender」的 repo（新帳號 Gender=10，會觸發 CHOOSE_GENDER）
        var repo = new NaiveAccountRepository();
        var auth = new AuthService(repo, new BcryptPasswordHasher());
        var charService = new CharacterService(new FakeCharRepo());
        var handler = new V113LoginConnectionHandler(
            NullLogger<V113LoginConnectionHandler>.Instance, auth, charService,
            new V113LoginOptions(AutoRegister: true));

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

        // 1) 握手，取 IV
        var lenBuf = new byte[2];
        await stream.ReadExactlyAsync(lenBuf, cts.Token);
        var payload = new byte[lenBuf[0] | (lenBuf[1] << 8)];
        await stream.ReadExactlyAsync(payload, cts.Token);
        int hpos = 2;
        int plen = payload[hpos] | (payload[hpos + 1] << 8); hpos += 2 + plen;
        var recvIv = payload.AsSpan(hpos, 4).ToArray(); hpos += 4;
        var sendIv = payload.AsSpan(hpos, 4).ToArray();
        var clientSend = new MapleAesOfb(recvIv, 113);
        var clientRecv = new MapleAesOfb(sendIv, unchecked((short)(0xFFFF - 113)));

        // 2) 第一次 LOGIN_PASSWORD（autoRegister，建新帳號 Gender=10）
        await SendAsync(stream, clientSend,
            new PacketWriter(32).WriteShort(V113RecvOp.LoginPassword)
                .WriteMapleString("genderuser").WriteMapleString("pass1").ToArray(), cts.Token);

        // 3) 期望收到 CHOOSE_GENDER (0x14)，不是 AuthSuccess (0x00)
        var pkt1 = await RecvAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)0x14, (short)(pkt1[0] | (pkt1[1] << 8)));
        // 封包含帳號名稱
        var r1 = new PacketReader(pkt1[2..]);
        Assert.Equal("genderuser", r1.ReadMapleString());

        // 4) 送 SET_GENDER (0x19)：帳號名 + PIN + gender(0=男)
        await SendAsync(stream, clientSend,
            new PacketWriter(32).WriteShort(V113RecvOp.SetGender)
                .WriteMapleString("genderuser").WriteMapleString("mypin").WriteByte(0).ToArray(), cts.Token);

        // 5) 期望收到 GENDER_SET (0x15)
        var pkt2 = await RecvAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)0x15, (short)(pkt2[0] | (pkt2[1] << 8)));

        // 6) 重新送 LOGIN_PASSWORD（同帳密；此時 Gender=0 且 SecondPassword 已設）
        await SendAsync(stream, clientSend,
            new PacketWriter(32).WriteShort(V113RecvOp.LoginPassword)
                .WriteMapleString("genderuser").WriteMapleString("pass1").ToArray(), cts.Token);

        // 7) 期望收到 AuthSuccess (LOGIN_STATUS=0x00, type=0)
        var pkt3 = await RecvAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)0x00, (short)(pkt3[0] | (pkt3[1] << 8)));  // LOGIN_STATUS
        Assert.Equal(0, pkt3[2]);                                        // type=0 success
        Assert.True(pkt3.Length > 6, "AuthSuccess 應包含 accountId 等欄位");

        client.Close();
        await serverTask;
    }

    /// <summary>已設定 gender 的帳號，登入直接拿到 AuthSuccess，不經過 CHOOSE_GENDER。</summary>
    [Fact]
    public async Task ExistingAccount_WithGenderSet_SkipsGenderFlow()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var repo = new NaiveAccountRepository();
        // 預先植入已完成性別設定的帳號
        repo.Seed(new Account
        {
            AccountName  = "veteran",
            PasswordHash = new BcryptPasswordHasher().Hash("pw"),
            CreatedAt    = DateTime.UtcNow,
            Gender       = 1,       // 女，已設定
            SecondPassword = "pin",
        });

        var auth = new AuthService(repo, new BcryptPasswordHasher());
        var handler = new V113LoginConnectionHandler(
            NullLogger<V113LoginConnectionHandler>.Instance, auth,
            new CharacterService(new FakeCharRepo()),
            new V113LoginOptions(AutoRegister: false));

        var serverTask = Task.Run(async () =>
        {
            var sock = await listener.AcceptSocketAsync(cts.Token);
            var session = new MapleSession(sock, NullLogger<MapleSession>.Instance);
            await using (session) await handler.HandleConnectionAsync(session, cts.Token);
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        var stream = client.GetStream();

        var lenBuf = new byte[2];
        await stream.ReadExactlyAsync(lenBuf, cts.Token);
        var payload = new byte[lenBuf[0] | (lenBuf[1] << 8)];
        await stream.ReadExactlyAsync(payload, cts.Token);
        int hpos = 2;
        int plen = payload[hpos] | (payload[hpos + 1] << 8); hpos += 2 + plen;
        var recvIv = payload.AsSpan(hpos, 4).ToArray(); hpos += 4;
        var sendIv = payload.AsSpan(hpos, 4).ToArray();
        var clientSend = new MapleAesOfb(recvIv, 113);
        var clientRecv = new MapleAesOfb(sendIv, unchecked((short)(0xFFFF - 113)));

        await SendAsync(stream, clientSend,
            new PacketWriter(32).WriteShort(V113RecvOp.LoginPassword)
                .WriteMapleString("veteran").WriteMapleString("pw").ToArray(), cts.Token);

        var pkt = await RecvAsync(stream, clientRecv, cts.Token);
        Assert.Equal((short)0x00, (short)(pkt[0] | (pkt[1] << 8)));  // AuthSuccess, 非 CHOOSE_GENDER
        Assert.Equal(0, pkt[2]);     // type=0
        Assert.Equal(1, pkt[7]);     // gender=1（女）：opcode(2)+type(1)+accountId(4)=offset 7

        client.Close();
        await serverTask;
    }

    // ── 測試輔助 ─────────────────────────────────────────────────────────────

    /// <summary>不會 auto-set gender 的 repo，用於需要觸發 CHOOSE_GENDER gate 的測試。</summary>
    private sealed class NaiveAccountRepository : IAccountRepository
    {
        private readonly Dictionary<string, Account> _store = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 1;

        public void Seed(Account a) { a.Id = _nextId++; _store[a.AccountName] = a; }

        public Task<Account?> FindByIdAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult(_store.Values.FirstOrDefault(a => a.Id == accountId));

        public Task<Account?> FindByNameAsync(string accountName, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(accountName, out var a) ? a : (Account?)null);

        public Task AddAsync(Account a, CancellationToken ct = default)
        { a.Id = _nextId++; _store[a.AccountName] = a; return Task.CompletedTask; }

        public Task<bool> TryAddAsync(Account a, CancellationToken ct = default)
        {
            if (_store.ContainsKey(a.AccountName)) return Task.FromResult(false);
            a.Id = _nextId++; _store[a.AccountName] = a; return Task.FromResult(true);
        }

        public Task UpdateAsync(Account a, CancellationToken ct = default)
        { _store[a.AccountName] = a; return Task.CompletedTask; }
    }

    private sealed class FakeCharRepo : ICharacterRepository
    {
        public Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Character>>(Array.Empty<Character>());
        public Task<Character?> FindByIdAsync(int id, CancellationToken ct = default) => Task.FromResult<Character?>(null);
        public Task<Character?> FindByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<Character?>(null);
        public Task AddAsync(Character c, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Character c, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static async Task SendAsync(NetworkStream s, MapleAesOfb cipher, byte[] body, CancellationToken ct)
    {
        var frame = new byte[body.Length + 4];
        cipher.WriteHeader(frame.AsSpan(0, 4), body.Length);
        cipher.Crypt(body);
        body.CopyTo(frame.AsSpan(4));
        await s.WriteAsync(frame, ct);
        await s.FlushAsync(ct);
    }

    private static async Task<byte[]> RecvAsync(NetworkStream s, MapleAesOfb cipher, CancellationToken ct)
    {
        var hdr = new byte[4];
        await s.ReadExactlyAsync(hdr, ct);
        int len = cipher.ReadLength(hdr);
        var body = new byte[len];
        await s.ReadExactlyAsync(body, ct);
        cipher.Crypt(body);
        return body;
    }
}
