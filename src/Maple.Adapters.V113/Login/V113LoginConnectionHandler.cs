using Maple.Adapters.V113.Crypto;
using Maple.Application.Accounts;
using Maple.Application.Characters;
using Maple.Core.Accounts;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Net;
using Maple.Versioning;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Maple.Adapters.V113.Login;

/// <summary>v113 登入連線的選項（由 Host 從實例設定投影）。</summary>
public sealed record V113LoginOptions(
    bool AutoRegister,
    string WorldName = "Scania",
    int ChannelCount = 1,
    int CharSlots = 3,
    byte[] ChannelIp = null!,
    int ChannelPort = 8585);

/// <summary>
/// v113 登入連線處理：握手 → 帳密驗證 → 世界/頻道列表 → 角色列表 → 建角/選角。
/// </summary>
public sealed class V113LoginConnectionHandler : IConnectionHandler
{
    private readonly IVersionCipherFactory _ciphers = new V113CipherFactory();
    private readonly ILogger<V113LoginConnectionHandler> _log;
    private readonly AuthService _auth;
    private readonly CharacterService _charService;
    private readonly V113LoginOptions _options;

    public V113LoginConnectionHandler(
        ILogger<V113LoginConnectionHandler> log,
        AuthService auth,
        CharacterService charService,
        V113LoginOptions options)
    {
        _log = log;
        _auth = auth;
        _charService = charService;
        _options = options;
    }

    public async Task HandleConnectionAsync(MapleSession session, CancellationToken ct)
    {
        byte[] recvIv = { 0x46, 0x72, 0x7A, (byte)Random.Shared.Next(256) };
        byte[] sendIv = { 0x52, 0x30, 0x78, (byte)Random.Shared.Next(256) };

        await session.SendRawAsync(V113LoginPackets.Hello(recvIv, sendIv), ct);

        var (recv, send) = _ciphers.CreateSessionPair(recvIv, sendIv);
        session.SetCiphers(recv, send);
        session.EnableCapture(recvIv, sendIv);   // 封包擷取(env MAPLEFORGE_CAPTURE=1 才生效；診斷用)
        _log.LogInformation("[v113] 握手送出，cipher 啟用 {Remote}", session.Remote);

        // 每個連線獨立的登入狀態
        var ctx = new LoginContext();
        await session.RunAsync((body, s, token) => OnPacketAsync(body, s, ctx, token), ct);
    }

    // ── 主路由 ────────────────────────────────────────────────────────────────

    private async Task OnPacketAsync(byte[] body, MapleSession session, LoginContext ctx, CancellationToken ct)
    {
        if (body.Length < 2) return;

        var reader = new PacketReader(body);
        short opcode = reader.ReadShort();

        switch (opcode)
        {
            case V113RecvOp.LoginPassword:
                await HandleLoginAsync(reader, session, ctx, ct);
                break;

            case V113RecvOp.ServerlistRequest:
                await HandleServerlistRequestAsync(session, ctx, ct);
                break;

            case V113RecvOp.ServerStatusRequest:
                await session.SendAsync(V113LoginPackets.ServerStatus(0), ct);
                break;

            case V113RecvOp.CharlistRequest:
                await HandleCharlistRequestAsync(reader, session, ctx, ct);
                break;

            case V113RecvOp.CheckCharName:
                await HandleCheckCharNameAsync(reader, session, ct);
                break;

            case V113RecvOp.CreateChar:
                await HandleCreateCharAsync(reader, session, ctx, ct);
                break;

            case V113RecvOp.CharSelect:
                await HandleCharSelectAsync(reader, session, ctx, ct);
                break;

            case V113RecvOp.SetGender:
                await HandleSetGenderAsync(reader, session, ctx, ct);
                break;

            case V113RecvOp.DeleteChar:
                await HandleDeleteCharAsync(reader, session, ctx, ct);
                break;

            case V113RecvOp.ClientError:
            {
                var data = reader.ReadBytes(reader.Remaining);
                _log.LogWarning("[v113] CLIENT_ERROR len={Len} data='{Data}' hex={Hex} {Remote}",
                    data.Length, Encoding.ASCII.GetString(data), Convert.ToHexString(data), session.Remote);
                break;
            }

            case V113RecvOp.ClientFeedback:
            {
                var data = reader.ReadBytes(reader.Remaining);
                _log.LogInformation("[v113] CLIENT_FEEDBACK len={Len} hex={Hex} {Remote}",
                    data.Length, Convert.ToHexString(data), session.Remote);
                break;
            }

            case V113RecvOp.ClientLogout:
                _log.LogDebug("[v113] CLIENT_LOGOUT len={Len} {Remote}", reader.Remaining, session.Remote);
                break;

            case V113RecvOp.Pong:
                break;

            default:
                _log.LogInformation("[v113] 收到 opcode=0x{Op:X2} len={Len}（尚未處理）{Remote}",
                    opcode, body.Length, session.Remote);
                break;
        }
    }

    // ── 個別 handler ─────────────────────────────────────────────────────────

    private async Task HandleLoginAsync(PacketReader reader, MapleSession session, LoginContext ctx, CancellationToken ct)
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
                var acc = result.Account!;
                // v113 authentic gate：性別未設(10) 或 第二密碼未設(null) → 先送 CHOOSE_GENDER
                if (acc.Gender == 10 || acc.SecondPassword == null)
                {
                    ctx.PendingAccount = acc;
                    _log.LogInformation("[v113] → CHOOSE_GENDER account='{Account}' {Remote}",
                        acc.AccountName, session.Remote);
                    await session.SendAsync(V113LoginPackets.ChooseGender(acc.AccountName), ct);
                }
                else
                {
                    ctx.AccountId   = acc.Id;
                    ctx.AccountName = acc.AccountName;
                    ctx.Gender      = acc.Gender;
                    _log.LogInformation("[v113] ✓ 登入成功 account='{Account}' (id={Id}) {Remote}",
                        acc.AccountName, acc.Id, session.Remote);
                    await session.SendAsync(
                        V113LoginPackets.AuthSuccess(ctx.AccountId, ctx.AccountName, ctx.Gender), ct);
                    // v113 客戶端登入成功後「不主動請求」世界列表，server 須緊接著主動連送
                    // ServerList + EndOfServerList(對照 Java LoginWorker.java:75-77)。
                    // 漏送會讓真客戶端停在登入頁互等(blocker #2 根因)。
                    await session.SendAsync(
                        V113LoginPackets.ServerList(_options.WorldName, _options.ChannelCount), ct);
                    await session.SendAsync(V113LoginPackets.EndOfServerList(), ct);
                }
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

    private async Task HandleSetGenderAsync(PacketReader reader, MapleSession session, LoginContext ctx, CancellationToken ct)
    {
        string username, secondPassword;
        byte gender;
        try
        {
            username       = reader.ReadMapleString();
            secondPassword = reader.ReadMapleString();
            gender         = reader.ReadByte();
        }
        catch (InvalidDataException)
        {
            _log.LogWarning("[v113] SET_GENDER 格式異常 {Remote}", session.Remote);
            return;
        }

        // 只接受合法性別值（對照 Java：> 1 或 < 0 直接 close session）
        if (gender > 1)
        {
            _log.LogWarning("[v113] SET_GENDER 無效性別 gender={G}，忽略 {Remote}", gender, session.Remote);
            return;
        }

        var pending = ctx.PendingAccount;
        var normalizedUsername = username.Trim().ToLowerInvariant();
        if (pending == null || !pending.AccountName.Equals(normalizedUsername, StringComparison.Ordinal))
        {
            _log.LogWarning("[v113] SET_GENDER 無待設定帳號或帳號不符 {Remote}", session.Remote);
            return;
        }

        await _auth.SetGenderAndPinAsync(pending, gender, secondPassword, ct);
        ctx.PendingAccount = null;

        _log.LogInformation("[v113] SET_GENDER 完成 account='{Account}' gender={G} {Remote}",
            pending.AccountName, gender, session.Remote);
        await session.SendAsync(V113LoginPackets.GenderSet(pending.AccountName), ct);
        // 對照 Java LOGIN_NOTLOGGEDIN：客戶端收到 GENDER_SET 後重新送 LOGIN_PASSWORD；
        // 此時 account.Gender ≠ 10 且 SecondPassword ≠ null → gate 通過 → AuthSuccess。
    }

    private async Task HandleServerlistRequestAsync(MapleSession session, LoginContext ctx, CancellationToken ct)
    {
        if (!ctx.IsLoggedIn) return;
        await session.SendAsync(
            V113LoginPackets.ServerList(_options.WorldName, _options.ChannelCount), ct);
        await session.SendAsync(V113LoginPackets.EndOfServerList(), ct);
        _log.LogInformation("[v113] ← SERVERLIST world={World} ch={Ch} {Remote}",
            _options.WorldName, _options.ChannelCount, session.Remote);
    }

    private async Task HandleCharlistRequestAsync(PacketReader reader, MapleSession session, LoginContext ctx, CancellationToken ct)
    {
        if (!ctx.IsLoggedIn) return;
        try
        {
            reader.ReadByte();                      // unknown
            reader.ReadByte();                      // world id
            ctx.SelectedChannel = reader.ReadByte() + 1;
        }
        catch (InvalidDataException) { /* 不完整封包，沿用預設 channel */ }

        var chars = await _charService.GetCharactersAsync(ctx.AccountId, ct);
        await session.SendAsync(V113LoginPackets.CharList(chars, _options.CharSlots), ct);
        _log.LogInformation("[v113] ← CHARLIST {Count} 角色 slot={Slots} {Remote}",
            chars.Count, _options.CharSlots, session.Remote);
    }

    private async Task HandleCheckCharNameAsync(PacketReader reader, MapleSession session, CancellationToken ct)
    {
        string name;
        try { name = reader.ReadMapleString(); }
        catch (InvalidDataException) { return; }

        bool available = await _charService.IsNameAvailableAsync(name, ct);
        await session.SendAsync(
            V113CharacterPackets.CharNameResponse(name, nameUsed: !available), ct);
        _log.LogInformation("[v113] ← CHAR_NAME_RESPONSE name='{Name}' available={A} {Remote}",
            name, available, session.Remote);
    }

    private async Task HandleCreateCharAsync(PacketReader reader, MapleSession session, LoginContext ctx, CancellationToken ct)
    {
        if (!ctx.IsLoggedIn) return;
        string name;
        int jobType, face, hair, top, bottom, shoes, weapon;
        try
        {
            name    = reader.ReadMapleString();
            jobType = reader.ReadInt();
            face    = reader.ReadInt();
            hair    = reader.ReadInt();
            top     = reader.ReadInt();
            bottom  = reader.ReadInt();
            shoes   = reader.ReadInt();
            weapon  = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            _log.LogWarning("[v113] CREATE_CHAR 封包格式異常 {Remote}", session.Remote);
            return;
        }

        // 初始裝備（位置對照舊 Java：-5=Top, -6=Bottom, -7=Shoes, -11=Weapon）
        var equips = new List<EquipEntry>();
        if (top     > 0) equips.Add(new EquipEntry { Position = -5,  ItemId = top });
        if (bottom  > 0) equips.Add(new EquipEntry { Position = -6,  ItemId = bottom });
        if (shoes   > 0) equips.Add(new EquipEntry { Position = -7,  ItemId = shoes });
        if (weapon  > 0) equips.Add(new EquipEntry { Position = -11, ItemId = weapon });

        var chr = await _charService.CreateCharacterAsync(
            ctx.AccountId, ctx.Gender, name, jobType, face, hair, equips, ct);

        bool success = chr is not null;
        var fallback = chr ?? new Character
        {
            Id = 0, AccountId = ctx.AccountId, Name = name,
            Gender = ctx.Gender, Face = face, Hair = hair,
            Equips = equips,
        };

        await session.SendAsync(V113CharacterPackets.AddNewCharEntry(fallback, success), ct);
        _log.LogInformation("[v113] ← ADD_NEW_CHAR_ENTRY name='{Name}' success={S} {Remote}",
            name, success, session.Remote);
    }

    private async Task HandleCharSelectAsync(PacketReader reader, MapleSession session, LoginContext ctx, CancellationToken ct)
    {
        if (!ctx.IsLoggedIn) return;
        int charId;
        try { charId = reader.ReadInt(); }
        catch (InvalidDataException) { return; }

        // 驗證角色屬於此帳號
        var chr = await _charService.GetByIdAsync(charId, ct);
        if (chr is null || chr.AccountId != ctx.AccountId)
        {
            _log.LogWarning("[v113] CHAR_SELECT 非法角色 id={Id} account={Acc} {Remote}",
                charId, ctx.AccountId, session.Remote);
            return;
        }

        var ip   = _options.ChannelIp ?? new byte[] { 127, 0, 0, 1 };
        var port = _options.ChannelPort;
        await session.SendAsync(V113LoginPackets.ServerIp(ip, port, charId), ct);
        _log.LogInformation("[v113] ← SERVER_IP charId={Id} {Ip}:{Port} {Remote}",
            charId, string.Join(".", ip), port, session.Remote);
    }

    // ── 連線狀態（per-connection）───────────────────────────────────────────

    private sealed class LoginContext
    {
        public int AccountId { get; set; }
        public string AccountName { get; set; } = "";
        public byte Gender { get; set; }
        public int SelectedChannel { get; set; } = 1;

        /// <summary>
        /// 送出 CHOOSE_GENDER 後暫存帳號物件，等待 SET_GENDER 完成。
        /// 非 null 表示帳號驗證通過但正在等待性別/PIN 設定。
        /// </summary>
        public Account? PendingAccount { get; set; }

        public bool IsLoggedIn => AccountId > 0;
    }

    // ── DELETE_CHAR ──────────────────────────────────────────────────────────

    private async Task HandleDeleteCharAsync(PacketReader reader, MapleSession session, LoginContext ctx, CancellationToken ct)
    {
        if (!ctx.IsLoggedIn) return;

        reader.ReadByte(); // skip
        var secondPassword = reader.ReadMapleString();
        var characterId = reader.ReadInt();

        byte state = 0;

        var chr = await _charService.GetByIdAsync(characterId, ct);
        if (chr is null || chr.AccountId != ctx.AccountId)
        {
            state = 1;
        }
        else
        {
            var deleted = await _charService.DeleteAsync(characterId, ct);
            if (!deleted) state = 1;
        }

        var w = new PacketWriter();
        w.WriteShort(V113SendOp.DeleteCharResponse);
        w.WriteInt(characterId);
        w.WriteByte(state);
        await session.SendAsync(w.ToArray(), ct);
    }
}
