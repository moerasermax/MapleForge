using Maple.Adapters.V113.Crypto;
using Maple.Application.Accounts;
using Maple.Core.IO;
using Maple.Net;
using Maple.Versioning;
using Microsoft.Extensions.Logging;

namespace Maple.Adapters.V113.Login;

/// <summary>v113 登入連線的選項（由 Host 從實例設定投影）。</summary>
public sealed record V113LoginOptions(
    bool AutoRegister,
    string WorldName = "Scania",
    int ChannelCount = 1,
    int CharSlots = 3);

/// <summary>
/// M2 的 v113 登入連線處理：送握手 → 啟用 cipher → 收 LOGIN_PASSWORD → 驗證帳密 → 回登入成功/失敗。
/// </summary>
public sealed class V113LoginConnectionHandler : IConnectionHandler
{
    private readonly IVersionCipherFactory _ciphers = new V113CipherFactory();
    private readonly ILogger<V113LoginConnectionHandler> _log;
    private readonly AuthService _auth;
    private readonly V113LoginOptions _options;

    public V113LoginConnectionHandler(
        ILogger<V113LoginConnectionHandler> log,
        AuthService auth,
        V113LoginOptions options)
    {
        _log = log;
        _auth = auth;
        _options = options;
    }

    public async Task HandleConnectionAsync(MapleSession session, CancellationToken ct)
    {
        byte[] recvIv = { 0x46, 0x72, 0x7A, (byte)Random.Shared.Next(256) };
        byte[] sendIv = { 0x52, 0x30, 0x78, (byte)Random.Shared.Next(256) };

        var hello = V113LoginPackets.Hello(recvIv, sendIv);
        await session.SendRawAsync(hello, ct);

        var (recv, send) = _ciphers.CreateSessionPair(recvIv, sendIv);
        session.SetCiphers(recv, send);
        _log.LogInformation("[v113] 握手送出，cipher 啟用 {Remote}", session.Remote);

        await session.RunAsync(OnPacketAsync, ct);
    }

    private async Task OnPacketAsync(byte[] body, MapleSession session, CancellationToken ct)
    {
        if (body.Length < 2) return;

        var reader = new PacketReader(body);
        short opcode = reader.ReadShort();

        switch (opcode)
        {
            case V113RecvOp.LoginPassword:
                await HandleLoginAsync(reader, session, ct);
                break;

            case V113RecvOp.ServerlistRequest:
                await HandleServerlistRequestAsync(session, ct);
                break;

            case V113RecvOp.ServerStatusRequest:
                await session.SendAsync(V113LoginPackets.ServerStatus(0), ct);
                break;

            case V113RecvOp.CharlistRequest:
                await HandleCharlistRequestAsync(session, ct);
                break;

            case V113RecvOp.Pong:
                break;

            default:
                _log.LogInformation("[v113] 收到 opcode=0x{Op:X2} len={Len}（尚未處理）{Remote}",
                    opcode, body.Length, session.Remote);
                break;
        }
    }

    private async Task HandleServerlistRequestAsync(MapleSession session, CancellationToken ct)
    {
        _log.LogInformation("[v113] → SERVERLIST_REQUEST {Remote}", session.Remote);
        await session.SendAsync(
            V113LoginPackets.ServerList(_options.WorldName, _options.ChannelCount), ct);
        await session.SendAsync(V113LoginPackets.EndOfServerList(), ct);
        _log.LogInformation("[v113] ← SERVERLIST 送出（world={World} ch={Ch}）{Remote}",
            _options.WorldName, _options.ChannelCount, session.Remote);
    }

    private async Task HandleCharlistRequestAsync(MapleSession session, CancellationToken ct)
    {
        _log.LogInformation("[v113] → CHARLIST_REQUEST {Remote}", session.Remote);
        await session.SendAsync(V113LoginPackets.CharList(_options.CharSlots), ct);
        _log.LogInformation("[v113] ← CHARLIST 送出（slots={Slots}）{Remote}",
            _options.CharSlots, session.Remote);
    }

    private async Task HandleLoginAsync(PacketReader reader, MapleSession session, CancellationToken ct)
    {
        string account, password;
        try
        {
            account = reader.ReadMapleString();
            password = reader.ReadMapleString();
        }
        catch (InvalidDataException)
        {
            _log.LogWarning("[v113] LOGIN_PASSWORD 格式異常 {Remote}", session.Remote);
            return;
        }

        var result = await _auth.AuthenticateAsync(account, password, _options.AutoRegister, ct);

        switch (result.Status)
        {
            case AuthStatus.Success:
                _log.LogInformation("[v113] ✓ 登入成功 account='{Account}' (id={Id}) {Remote}",
                    account, result.Account!.Id, session.Remote);
                await session.SendAsync(
                    V113LoginPackets.AuthSuccess(result.Account.Id, result.Account.AccountName), ct);
                break;

            case AuthStatus.AccountBanned:
                _log.LogInformation("[v113] 帳號封鎖 '{Account}' {Remote}", account, session.Remote);
                await session.SendAsync(V113LoginPackets.LoginFailed(3), ct);
                break;

            case AuthStatus.InvalidPassword:
            default:
                _log.LogInformation("[v113] 帳密錯誤 '{Account}' {Remote}", account, session.Remote);
                await session.SendAsync(V113LoginPackets.LoginFailed(4), ct);
                break;
        }
    }
}
