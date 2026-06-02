using Maple.Adapters.V113.Crypto;
using Maple.Application.Characters;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;
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

        // Per-connection context（player 持有執行期位置，見 Core/World）
        Character? chr = null;
        Player? player = null;

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
                            // 執行期玩家（持有位置；spawn 暫定 0,0，之後接 portal/SpawnPoint）
                            player = new Player(chr, new Position(0, 0, 0, 0));
                            var pos = player.Position;

                            _mapRegistry.Register(chr.MapId, chr.Id, chr, (pkt, tkn) => s.SendAsync(pkt, tkn));

                            // Notify existing players of new arrival（並讓新玩家看到現有玩家）
                            var others = _mapRegistry.GetOthers(chr.MapId, chr.Id);
                            foreach (var other in others)
                            {
                                var spawnForOther = V113MapPackets.SpawnPlayer(chr, pos.X, pos.Y, pos.Stance, pos.Foothold);
                                await other.SendPacket(spawnForOther, token);

                                var spawnForNew = V113MapPackets.SpawnPlayer(other.Character, 0, 0, 0, 0);
                                await s.SendAsync(spawnForNew, token);
                            }

                            _log.LogInformation("[Channel] 角色 {Name} 已進入地圖 {Map}，同地圖 {Count} 人", chr.Name, chr.MapId, others.Count);
                        }
                        break;

                    case V113ChannelRecvOp.MovePlayer:
                        if (player is null) break;
                        TryUpdateMovement(player, body);                              // 解析→更新 server 權威位置(Core)
                        await BroadcastToOthersAsync(player.Character, body, token);  // 原始 blob 轉發(動畫擬真)
                        break;

                    case V113ChannelRecvOp.GeneralChat:
                        if (player is null) break;
                        await HandleGeneralChatAsync(reader, player, s, token);
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

    /// <summary>
    /// 解析客戶端 MOVE_PLAYER 的移動串，更新 server 端權威位置（Core <see cref="Player"/>）。
    /// best-effort：解析失敗只記 log、不中斷連線（廣播仍走原始 blob）。
    /// c2s 格式：[opcode 2][header 33][movement list(numCommands…)]。
    /// </summary>
    private void TryUpdateMovement(Player player, byte[] body)
    {
        const int HeaderSkip = 2 + 33;
        if (body.Length <= HeaderSkip) return;
        try
        {
            var result = V113MovementParser.Parse(new PacketReader(body, HeaderSkip));
            player.MoveTo(new Position(result.X, result.Y, result.Stance, result.Foothold));
        }
        catch (InvalidDataException ex)
        {
            _log.LogDebug("[Channel] 移動解析失敗(忽略,仍廣播) {Msg}", ex.Message);
        }
    }

    /// <summary>
    /// 一般地圖聊天（對照 Java ChatHandler.GeneralChat 核心）。
    /// c2s：[maple string text][byte show]；自己看到 + 廣播同地圖其他玩家 CHATTEXT。
    /// </summary>
    private async Task HandleGeneralChatAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        string text;
        byte show;
        try
        {
            text = reader.ReadMapleString();
            show = reader.ReadByte();
        }
        catch (InvalidDataException) { return; }

        if (text.Length == 0 || text.Length >= 80) return;   // Java：非 GM >=80 擋

        var packet = V113MapPackets.ChatText(player.Character.Id, text, show);
        await session.SendAsync(packet, ct);                  // 自己看到泡泡

        var others = _mapRegistry.GetOthers(player.Character.MapId, player.Character.Id);
        foreach (var other in others)
        {
            try { await other.SendPacket(packet, ct); } catch { /* session 可能正在關 */ }
        }
        _log.LogInformation("[Channel] 角色 {Name} 地圖聊天「{Text}」", player.Character.Name, text);
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
        w.WriteByte(6);   // locale = 6 (TMS)，Login Hello 也是 6
        return w.ToArray();
    }
}
