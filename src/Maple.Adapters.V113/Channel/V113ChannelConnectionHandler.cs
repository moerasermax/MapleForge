using Maple.Adapters.V113.Crypto;
using Maple.Application.Characters;
using Maple.Application.Maps;
using Maple.Application.Npcs;
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
    private readonly MapService _mapService;
    private readonly INpcScriptFactory _npcScripts;
    private readonly V113ChannelOptions _options;

    public V113ChannelConnectionHandler(
        ILogger<V113ChannelConnectionHandler> log,
        CharacterService charService,
        IMapSessionRegistry mapRegistry,
        MapService mapService,
        INpcScriptFactory npcScripts,
        V113ChannelOptions options)
    {
        _log = log;
        _charService = charService;
        _mapRegistry = mapRegistry;
        _mapService = mapService;
        _npcScripts = npcScripts;
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

        // Per-connection context（handler 是 singleton！這些狀態必須是連線區域變數）
        Character? chr = null;
        Player? player = null;
        var npcOidToId = new Dictionary<int, int>();   // 地圖 NPC objectId → npcId（SpawnMapNpcs 時建）
        NpcConversation? conversation = null;           // 當前對話（session-local，不進 registry）

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

                            // 地圖物件同步：把該地圖的 NPC spawn 給剛進場的玩家（同時建 oid→npcId）
                            await SpawnMapNpcsAsync(chr.MapId, s, npcOidToId, token);

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

                    case V113ChannelRecvOp.NpcTalk:
                        if (player is null) break;
                        conversation = await StartNpcConversationAsync(reader, player, npcOidToId, s, token);
                        break;

                    case V113ChannelRecvOp.NpcTalkMore:
                        if (conversation is null) break;
                        await ContinueNpcConversationAsync(reader, conversation, token);
                        if (!conversation.Active) conversation = null;
                        break;

                    case V113ChannelRecvOp.ItemMove:
                        if (player is null) break;
                        await HandleItemMoveAsync(reader, player, s, token);
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

    /// <summary>
    /// 進場時把該地圖的 NPC spawn 給玩家（對照 Java MapleNPC.sendSpawnData：spawnNPC + spawnNPCRequestController）。
    /// objectId 暫用每連線計數器（base <see cref="NpcObjectIdBase"/>，避開玩家 charId 小號）；
    /// proper 每-Field 配發器待 IFieldRegistry 重構（見架構文件風險#5）。
    /// 跳過隱藏 NPC 與 PlayerNPC（id ≥ 9901000，對照 Java sendSpawnData 條件）。
    /// </summary>
    private async Task SpawnMapNpcsAsync(int mapId, MapleSession session, Dictionary<int, int> oidToNpcId, CancellationToken ct)
    {
        var map = _mapService.LoadMap(mapId);
        var objectId = NpcObjectIdBase;
        var spawned = 0;

        oidToNpcId.Clear();
        foreach (var def in map.Npcs)
        {
            if (def.Hide || def.NpcId >= 9901000) continue;

            var oid = objectId++;
            var npc = new Npc(def, oid);
            oidToNpcId[oid] = def.NpcId;   // 供 c2s NPC_TALK 的 oid 反查 npcId
            await session.SendAsync(V113MapPackets.SpawnNpc(npc), ct);
            await session.SendAsync(V113MapPackets.SpawnNpcRequestController(npc), ct);
            spawned++;
        }

        _log.LogInformation("[Channel] 地圖 {Map} 送出 {Count} 個 NPC spawn", mapId, spawned);
    }

    /// <summary>NPC 地圖物件 id 起始值（避開玩家以 charId 充當的小號 objectId）。</summary>
    private const int NpcObjectIdBase = 1000;

    // ── NPC 對話（路線圖②）─────────────────────────────────────────────────────

    /// <summary>
    /// c2s NPC_TALK：[int oid] → 反查 npcId → 建腳本對話、跑 start()、flush 第一則對話。
    /// sink/warp 為語意化委派（編碼鎖本層；warp 重用進場序列）。回傳仍 active 的對話、否則 null。
    /// </summary>
    private async Task<NpcConversation?> StartNpcConversationAsync(
        PacketReader reader, Player player, Dictionary<int, int> oidToNpcId, MapleSession session, CancellationToken ct)
    {
        var oid = reader.ReadInt();
        if (!oidToNpcId.TryGetValue(oid, out var npcId))
        {
            _log.LogDebug("[Channel] NPC_TALK 未知 oid={Oid}", oid);
            return null;
        }

        var ctx = new NpcContext(npcId, player);
        var script = _npcScripts.TryCreate(npcId, ctx);
        if (script is null)
        {
            _log.LogDebug("[Channel] NPC {Npc} 無對應腳本，略過", npcId);
            return null;
        }

        var convo = new NpcConversation(
            npcId, script, ctx,
            sendDialog: (dlg, c) => session.SendAsync(V113NpcDialogEncoder.Encode(dlg), c),
            warp: (mapId, c) => WarpAsync(player.Character, oidToNpcId, session, mapId, c));

        await convo.StartAsync(ct);
        _log.LogInformation("[Channel] NPC {Npc} 對話開始", npcId);
        return convo.Active ? convo : null;
    }

    /// <summary>
    /// c2s NPC_TALK_MORE：[byte lastMsg][byte action(mode)][selection]。
    /// 對照 Java NPCMoreTalk：lastMsg==2(getText) 帶字串、否則 selection = 剩餘≥4 readInt / &gt;0 readByte / else -1。
    /// </summary>
    private static async Task ContinueNpcConversationAsync(PacketReader reader, NpcConversation convo, CancellationToken ct)
    {
        var lastMsg = reader.ReadByte();
        int mode = (sbyte)reader.ReadByte();   // 1=下一步/是, 0=上一步/否, -1=ESC
        int selection = -1;

        if (lastMsg != 2)   // getText 的輸入字串 MVP 不消費（selection 維持 -1）
        {
            if (reader.Remaining >= 4) selection = reader.ReadInt();
            else if (reader.Remaining > 0) selection = reader.ReadByte();
        }

        await convo.ContinueAsync(mode, lastMsg, selection, ct);
    }

    /// <summary>
    /// cm.warp 的落地：換地圖（MVP 重用進場序列——deregister 舊圖 → 設 MapId → SET_FIELD → register 新圖 → spawn NPC）。
    /// proper 的輕量 WarpToMap 封包 + login/warp 共用 IMapTransition 用例待後續重構。
    /// </summary>
    private async Task WarpAsync(Character chr, Dictionary<int, int> oidToNpcId, MapleSession session, int mapId, CancellationToken ct)
    {
        _mapRegistry.Deregister(chr.MapId, chr.Id);
        chr.MapId = mapId;

        var setField = V113ChannelPackets.SetField(chr, _options.ChannelIndex);
        await session.SendAsync(setField, ct);

        _mapRegistry.Register(mapId, chr.Id, chr, (pkt, tkn) => session.SendAsync(pkt, tkn));
        await SpawnMapNpcsAsync(mapId, session, oidToNpcId, ct);
        _log.LogInformation("[Channel] 角色 {Name} warp → 地圖 {Map}", chr.Name, mapId);
    }

    // ── 背包（路線圖②後半）─────────────────────────────────────────────────────

    /// <summary>
    /// c2s ITEM_MOVE：MVP-0 僅處理「格內移動」(src&gt;0,dst&gt;0)→Player 變動→flush→ModifyInventory(mode 2) 回包。
    /// 穿脫裝(一端負)/丟棄(dst=0) defer（不回包，客戶端自行回滾，見設計 doc）。
    /// </summary>
    private async Task HandleItemMoveAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        var req = V113InventoryPackets.ParseItemMove(reader);
        if (!req.IsValidBagType) return;

        if (!req.IsWithinBagMove)
        {
            _log.LogDebug("[Channel] ITEM_MOVE 非格內移動(穿脫/丟棄) MVP-0 略過 type={T} src={S} dst={D}", req.RawType, req.Src, req.Dst);
            return;
        }

        if (player.MoveItem(req.Type, req.Src, req.Dst))
        {
            player.FlushInventory();
            await session.SendAsync(V113InventoryPackets.ModifyMove(req.Type, req.Src, req.Dst), ct);
            _log.LogDebug("[Channel] ITEM_MOVE {T} {S}→{D}", req.Type, req.Src, req.Dst);
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
