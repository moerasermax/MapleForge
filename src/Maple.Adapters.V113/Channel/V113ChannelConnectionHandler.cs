using Maple.Adapters.V113.Crypto;
using Maple.Application.Characters;
using Maple.Core.IO;
using Maple.Net;
using Maple.Versioning;
using Microsoft.Extensions.Logging;

namespace Maple.Adapters.V113.Channel;

/// <summary>v113 Channel 連線選項（由 Host 從實例設定投影）。</summary>
public sealed record V113ChannelOptions(int ChannelIndex = 0);

/// <summary>
/// v113 Channel 連線處理：握手 → PLAYER_LOGGEDIN → 載入角色 → SET_FIELD（進地圖）。
/// 接收模式採 RunAsync 迴圈（對齊 Login 模式）。
/// </summary>
public sealed class V113ChannelConnectionHandler : IChannelConnectionHandler
{
    private readonly IVersionCipherFactory _ciphers = new V113CipherFactory();
    private readonly ILogger<V113ChannelConnectionHandler> _log;
    private readonly CharacterService _charService;
    private readonly V113ChannelOptions _options;

    public V113ChannelConnectionHandler(
        ILogger<V113ChannelConnectionHandler> log,
        CharacterService charService,
        V113ChannelOptions options)
    {
        _log = log;
        _charService = charService;
        _options = options;
    }

    public async Task HandleChannelConnectionAsync(MapleSession session, CancellationToken ct)
    {
        byte[] recvIv = { 0x46, 0x72, 0x7A, (byte)Random.Shared.Next(256) };
        byte[] sendIv = { 0x52, 0x30, 0x78, (byte)Random.Shared.Next(256) };

        await session.SendRawAsync(BuildHello(recvIv, sendIv), ct);

        var (recv, send) = _ciphers.CreateSessionPair(recvIv, sendIv);
        session.SetCiphers(recv, send);
        _log.LogInformation("[Channel] 握手送出，cipher 啟用 {Remote}", session.Remote);

        await session.RunAsync(OnPacketAsync, ct);
    }

    private async Task OnPacketAsync(byte[] body, MapleSession session, CancellationToken ct)
    {
        if (body.Length < 2) return;

        var reader = new PacketReader(body);
        var opcode = reader.ReadShort();

        switch (opcode)
        {
            case V113ChannelRecvOp.PlayerLoggedIn:
                await HandlePlayerLoggedInAsync(reader, session, ct);
                break;

            case V113ChannelRecvOp.Pong:
                break;

            default:
                _log.LogDebug("[Channel] opcode=0x{Op:X2} len={L} (未處理)", opcode, body.Length);
                break;
        }
    }

    private async Task HandlePlayerLoggedInAsync(PacketReader reader, MapleSession session, CancellationToken ct)
    {
        var charId = reader.ReadInt();
        _log.LogInformation("[Channel] PLAYER_LOGGEDIN charId={Id}", charId);

        var chr = await _charService.GetByIdAsync(charId, ct);
        if (chr is null)
        {
            _log.LogWarning("[Channel] 找不到角色 id={Id}，斷線", charId);
            return;
        }

        var setField = V113ChannelPackets.SetField(chr, _options.ChannelIndex);
        await session.SendAsync(setField, ct);
        _log.LogInformation("[Channel] 角色 {Name} 進入地圖 {Map}", chr.Name, chr.MapId);
    }

    private static ReadOnlyMemory<byte> BuildHello(byte[] recvIv, byte[] sendIv)
    {
        var w = new PacketWriter();
        const short version = 113;
        var patchBytes = System.Text.Encoding.ASCII.GetBytes("1");
        var payloadLen = (short)(2 + 2 + patchBytes.Length + 4 + 4 + 1);
        w.WriteShort(payloadLen);
        w.WriteShort(version);
        w.WriteShort(patchBytes.Length);
        w.WriteBytes(patchBytes);
        w.WriteBytes(recvIv);
        w.WriteBytes(sendIv);
        w.WriteByte(8);   // locale = 8 (TMS)
        return w.ToArray();
    }
}
