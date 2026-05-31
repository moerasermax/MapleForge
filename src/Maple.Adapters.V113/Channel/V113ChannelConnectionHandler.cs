using Maple.Adapters.V113.Crypto;
using Maple.Application.Characters;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Net;
using Maple.Versioning;
using Microsoft.Extensions.Logging;

namespace Maple.Adapters.V113.Channel;

/// <summary>v113 Channel 連線選項（由 Host 從實例設定投影）。</summary>
public sealed record V113ChannelOptions(int ChannelIndex = 0);

/// <summary>
/// v113 Channel 連線處理：握手 → PLAYER_LOGGEDIN → 載入角色 → SET_FIELD（進地圖）→ 廣播移動。
/// </summary>
public sealed class V113ChannelConnectionHandler : IChannelConnectionHandler
{
    private readonly IVersionCipherFactory _ciphers = new V113CipherFactory();
    private readonly ILogger<V113ChannelConnectionHandler> _log;
    private readonly CharacterService _charService;
    private readonly IMapSessionRegistry _mapRegistry;
    private readonly V113ChannelOptions _options;

    public V113ChannelConnectionHandler(
        ILogger<V113ChannelConnectionHandler> log,
        CharacterService charService,
        IMapSessionRegistry mapRegistry,
        V113ChannelOptions options)
    {
        _log = log;
        _charService = charService;
        _mapRegistry = mapRegistry;
        _options = options;
    }

    public async Task HandleChannelConnectionAsync(MapleSession session, CancellationToken ct)
    {
        byte[] recvIv = { 0x46, 0x72, 0x7A, (byte)Random.Shared.Next(256) };
        byte[] sendIv = { 0x52, 0x30, 0x78, (byte)Random.Shared.Next(256) };

        await session.SendRawAsync(BuildHello(recvIv, sendIv), ct);

        var (recv, send) = _ciphers.CreateSessionPair(recvIv, sendIv);
        session.SetCiphers(recv, send);
        _log.LogInformation("[Channel] 握手送出 {Remote}", session.Remote);

        // Per-connection context
        Character? chr = null;
        short x = 0, y = 0;
        byte stance = 0;
        short foothold = 0;

        try
        {
            await session.RunAsync(async (body, s, token) =>
            {
                if (body.Length < 2) return;
                var reader = new PacketReader(body);
                var opcode = reader.ReadShort();

                switch (opcode)
                {
                    case V113ChannelRecvOp.PlayerLoggedIn:
                        chr = await HandlePlayerLoggedInAsync(reader, s, token);
                        if (chr is not null)
                        {
                            // Determine spawn position
                            var spawnPortal = 0; // TODO: use chr.SpawnPoint
                            x = 0; y = 0; stance = 0; foothold = 0;

                            // Register in map registry
                            _mapRegistry.Register(chr.MapId, chr.Id, chr, (pkt, tkn) => s.SendAsync(pkt, tkn));

                            // Notify existing players of new arrival
                            var others = _mapRegistry.GetOthers(chr.MapId, chr.Id);
                            foreach (var other in others)
                            {
                                // Spawn new player for each existing player
                                var spawnForOther = V113MapPackets.SpawnPlayer(chr, x, y, stance, foothold);
                                await other.SendPacket(spawnForOther, token);

                                // Spawn existing player for new arrival
                                var spawnForNew = V113MapPackets.SpawnPlayer(other.Character, x, y, stance, foothold);
                                await s.SendAsync(spawnForNew, token);
                            }

                            _log.LogInformation("[Channel] 角色 {Name} 已進入地圖 {Map}，同地圖 {Count} 人", chr.Name, chr.MapId, others.Count);
                        }
                        break;

                    case V113ChannelRecvOp.MovePlayer:
                        if (chr is null) break;
                        HandleMovePlayer(body, chr, ref x, ref y, ref stance, ref foothold);
                        await BroadcastToOthersAsync(chr, body, token);
                        break;

                    case V113ChannelRecvOp.Pong:
                        break;

                    default:
                        _log.LogDebug("[Channel] opcode=0x{Op:X2} len={L}", opcode, body.Length);
                        break;
                }
            }, ct);
        }
        finally
        {
            // Cleanup: remove from map, notify others
            if (chr is not null)
            {
                _mapRegistry.Deregister(chr.MapId, chr.Id);
                var removePacket = V113MapPackets.RemovePlayer(chr.Id);
                var remainingOthers = _mapRegistry.GetOthers(chr.MapId, chr.Id);
                foreach (var other in remainingOthers)
                {
                    try
                    {
                        await other.SendPacket(removePacket, CancellationToken.None);
                    }
                    catch { /* session might be closing */ }
                }
                _log.LogInformation("[Channel] 角色 {Name} 離開地圖 {Map}", chr.Name, chr.MapId);
            }
        }
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task<Character?> HandlePlayerLoggedInAsync(PacketReader reader, MapleSession session, CancellationToken ct)
    {
        var charId = reader.ReadInt();
        _log.LogInformation("[Channel] PLAYER_LOGGEDIN charId={Id}", charId);

        var chr = await _charService.GetByIdAsync(charId, ct);
        if (chr is null)
        {
            _log.LogWarning("[Channel] 找不到角色 id={Id}", charId);
            return null;
        }

        var setField = V113ChannelPackets.SetField(chr, _options.ChannelIndex);
        await session.SendAsync(setField, ct);
        _log.LogInformation("[Channel] 角色 {Name} SET_FIELD 送出 → 地圖 {Map}", chr.Name, chr.MapId);
        return chr;
    }

    private static void HandleMovePlayer(byte[] body, Character chr, ref short x, ref short y, ref byte stance, ref short foothold)
    {
        // Client MOVE_PLAYER format: [opcode 2][unknown 33][movement data]
        // We update server-side position minimally (just track it)
        // Full MovementParse is in M3-7 scope; for now we do basic relay
        const int HeaderSkip = 2 + 33;
        if (body.Length <= HeaderSkip) return;
        // Movement data is forwarded as-is; position extraction is optional for M3-7
    }

    private async Task BroadcastToOthersAsync(Character chr, byte[] body, CancellationToken ct)
    {
        const int HeaderSkip = 2 + 33;
        if (body.Length <= HeaderSkip) return;

        var rawMovement = body.AsSpan(HeaderSkip);
        var broadcast = V113MapPackets.MovePlayerBroadcast(chr.Id, rawMovement);

        var others = _mapRegistry.GetOthers(chr.MapId, chr.Id);
        foreach (var other in others)
        {
            try
            {
                await other.SendPacket(broadcast, ct);
            }
            catch { /* ignore failed broadcasts */ }
        }
    }

    // ── Hello ──────────────────────────────────────────────────────────────────

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
        w.WriteByte(8);
        return w.ToArray();
    }
}
