using System.Collections.Concurrent;
using Maple.Adapters.V113.Crypto;
using Maple.Application.Buddies;
using Maple.Application.Characters;
using Maple.Application.Combat;
using Maple.Application.Items;
using Maple.Application.Duey;
using Maple.Application.Drops;
using Maple.Application.Fame;
using Maple.Application.Guilds;
using Maple.Application.Maps;
using Maple.Application.Npcs;
using Maple.Application.OnlinePlayers;
using Maple.Application.Parties;
using Maple.Application.Pets;
using Maple.Application.Quests;
using Maple.Application.Reactors;
using Maple.Application.Shops;
using Maple.Application.Skills;
using Maple.Application.Social;
using Maple.Application.Stats;
using Maple.Application.Storage;
using Maple.Application.Trades;
using Maple.Core.Accounts;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Skills;
using Maple.Core.World;
using Maple.Net;
using Maple.Versioning;
using Microsoft.Extensions.Logging;

namespace Maple.Adapters.V113.Channel;

/// <summary>v113 Channel 連線選項（由 Host 從實例設定投影）。</summary>
public sealed record V113ChannelOptions(int ChannelIndex = 0, byte[]? ChannelIp = null, int ChannelPort = 8585);

internal sealed record CashShopTransitionData(
    int CharacterId,
    int PreviousMapId,
    int PreviousChannel,
    DateTimeOffset RegisteredAt);

/// <summary>
/// v113 Channel 連線處理：握手 → PLAYER_LOGGEDIN → 載入角色 → SET_FIELD（進地圖）→ 廣播移動。
/// </summary>
public sealed class V113ChannelConnectionHandler : IChannelConnectionHandler
{
    private readonly IVersionCipherFactory _ciphers = new V113CipherFactory();
    private readonly ConcurrentDictionary<int, CashShopTransitionData> _pendingCashShopTransitions = new();
    private readonly ILogger<V113ChannelConnectionHandler> _log;
    private readonly CharacterService _charService;
    private readonly IAccountRepository _accounts;
    private readonly IOnlinePlayerRegistry _onlinePlayers;
    private readonly IMapSessionRegistry _mapRegistry;
    private readonly IFieldInstanceRegistry _fieldRegistry;
    private readonly MapService _mapService;
    private readonly INpcScriptFactory _npcScripts;
    private readonly ShopService _shopService;
    private readonly StorageService _storageService;
    private readonly CombatService _combatService;
    private readonly SkillService _skillService;
    private readonly ISkillBookCatalog _skillBookCatalog;
    private readonly DropService _dropService;
    private readonly FameService _fameService;
    private readonly GuildService _guildService;
    private readonly RangedMagicCombatService _rangedMagicCombatService;
    private readonly ReactorService _reactorService;
    private readonly TradeService _tradeService;
    private readonly FollowService _followService;
    private readonly V113BuddyHandler _buddyHandler;
    private readonly V113PartyOperationHandler _partyOperationHandler;
    private readonly V113PartySearchHandler _partySearchHandler;
    private readonly V113GuildOperationHandler _guildOperationHandler;
    private readonly V113CashShopOperationHandler _cashShopOperationHandler;
    private readonly V113ChatHandler _chatHandler;
    private readonly V113PlayerInteractionRouter _playerInteractionRouter;
    private readonly V113HiredMerchantHandler _hiredMerchantHandler;
    private readonly V113DueyHandler _dueyHandler;
    private readonly V113BbsHandler _bbsHandler;
    private readonly V113RingHandler _ringHandler;
    private readonly V113OwlHandler _owlHandler;
    private readonly V113BuffItemHandler _buffItemHandler;
    private readonly V113ItemUseHandler _itemUseHandler;
    private readonly V113ScrollHandler _scrollHandler;
    private readonly V113UseConsumableHandler _useConsumableHandler;
    private readonly V113UseCashItemHandler _useCashItemHandler;
    private readonly PetService _petService;
    private readonly ItemUseService _itemUseService;
    private readonly ItemMakerService _itemMakerService;
    private readonly RandomRewardsCatalog _randomRewardsCatalog;
    private readonly QuestService _questService;
    private readonly StatsService _statsService;
    private readonly V113AllianceHandler _allianceHandler;
    private readonly V113MessengerHandler _messengerHandler;
    private readonly V113DoorHandler _doorHandler;
    private readonly V113NoteHandler _noteHandler;
    private readonly V113FamilyHandler _familyHandler;
    private readonly V113EventMiniGameHandler _eventMiniGameHandler;
    private readonly V113ChannelOptions _options;

    public V113ChannelConnectionHandler(
        ILogger<V113ChannelConnectionHandler> log,
        CharacterService charService,
        IAccountRepository accounts,
        IOnlinePlayerRegistry onlinePlayers,
        IMapSessionRegistry mapRegistry,
        IFieldInstanceRegistry fieldRegistry,
        MapService mapService,
        INpcScriptFactory npcScripts,
        ShopService shopService,
        StorageService storageService,
        CombatService combatService,
        SkillService skillService,
        ISkillBookCatalog skillBookCatalog,
        DropService dropService,
        FameService fameService,
        GuildService guildService,
        RangedMagicCombatService rangedMagicCombatService,
        ReactorService reactorService,
        TradeService tradeService,
        FollowService followService,
        V113BuddyHandler buddyHandler,
        V113PartyOperationHandler partyOperationHandler,
        V113PartySearchHandler partySearchHandler,
        V113GuildOperationHandler guildOperationHandler,
        V113CashShopOperationHandler cashShopOperationHandler,
        V113ChatHandler chatHandler,
        V113PlayerInteractionRouter playerInteractionRouter,
        V113HiredMerchantHandler hiredMerchantHandler,
        V113DueyHandler dueyHandler,
        V113BbsHandler bbsHandler,
        V113RingHandler ringHandler,
        V113OwlHandler owlHandler,
        V113BuffItemHandler buffItemHandler,
        V113ItemUseHandler itemUseHandler,
        V113ScrollHandler scrollHandler,
        V113UseConsumableHandler useConsumableHandler,
        V113UseCashItemHandler useCashItemHandler,
        PetService petService,
        ItemUseService itemUseService,
        ItemMakerService itemMakerService,
        RandomRewardsCatalog randomRewardsCatalog,
        QuestService questService,
        StatsService statsService,
        V113AllianceHandler allianceHandler,
        V113MessengerHandler messengerHandler,
        V113DoorHandler doorHandler,
        V113NoteHandler noteHandler,
        V113FamilyHandler familyHandler,
        V113EventMiniGameHandler eventMiniGameHandler,
        V113ChannelOptions options)
    {
        _log = log;
        _charService = charService;
        _accounts = accounts;
        _onlinePlayers = onlinePlayers;
        _mapRegistry = mapRegistry;
        _fieldRegistry = fieldRegistry;
        _mapService = mapService;
        _npcScripts = npcScripts;
        _shopService = shopService;
        _storageService = storageService;
        _combatService = combatService;
        _skillService = skillService;
        _skillBookCatalog = skillBookCatalog;
        _dropService = dropService;
        _fameService = fameService;
        _guildService = guildService;
        _rangedMagicCombatService = rangedMagicCombatService;
        _reactorService = reactorService;
        _tradeService = tradeService;
        _followService = followService;
        _buddyHandler = buddyHandler;
        _partyOperationHandler = partyOperationHandler;
        _partySearchHandler = partySearchHandler;
        _guildOperationHandler = guildOperationHandler;
        _cashShopOperationHandler = cashShopOperationHandler;
        _chatHandler = chatHandler;
        _playerInteractionRouter = playerInteractionRouter;
        _hiredMerchantHandler = hiredMerchantHandler;
        _dueyHandler = dueyHandler;
        _bbsHandler = bbsHandler;
        _ringHandler = ringHandler;
        _owlHandler = owlHandler;
        _buffItemHandler = buffItemHandler;
        _itemUseHandler = itemUseHandler;
        _scrollHandler = scrollHandler;
        _useConsumableHandler = useConsumableHandler;
        _useCashItemHandler = useCashItemHandler;
        _petService = petService;
        _itemUseService = itemUseService;
        _itemMakerService = itemMakerService;
        _randomRewardsCatalog = randomRewardsCatalog;
        _questService = questService;
        _statsService = statsService;
        _allianceHandler = allianceHandler;
        _messengerHandler = messengerHandler;
        _doorHandler = doorHandler;
        _noteHandler = noteHandler;
        _familyHandler = familyHandler;
        _eventMiniGameHandler = eventMiniGameHandler;
        _options = options;
    }

    internal static bool CanEnterCashShopFromMap(int mapId)
        => !IsMapleLand(mapId) && mapId != 220080001;

    internal static CashShopTransitionData RegisterCashShopTransition(
        ConcurrentDictionary<int, CashShopTransitionData> pendingTransitions,
        int characterId,
        int previousMapId,
        int previousChannel)
    {
        var data = new CashShopTransitionData(
            characterId,
            previousMapId,
            previousChannel,
            DateTimeOffset.UtcNow);
        pendingTransitions[characterId] = data;
        return data;
    }

    internal static bool TryConsumeCashShopTransition(
        ConcurrentDictionary<int, CashShopTransitionData> pendingTransitions,
        int characterId,
        out CashShopTransitionData transition)
    {
        if (pendingTransitions.TryRemove(characterId, out transition!))
        {
            if (DateTimeOffset.UtcNow - transition.RegisteredAt < TimeSpan.FromSeconds(30))
            {
                return true;
            }
        }

        transition = default!;
        return false;
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
        var sessionToken = new object();
        Character? chr = null;
        Account? account = null;
        Player? player = null;
        FieldInstance? currentField = null;
        int? storageNpcId = null;
        var npcOidToId = new Dictionary<int, int>();   // 地圖 NPC objectId → npcId（SpawnMapNpcs 時建）
        NpcConversation? conversation = null;           // 當前對話（session-local，不進 registry）
        var cashShopMode = false;
        CashShopTransitionData? cashShopTransition = null;

        async Task OpenShopFromNpcAsync(int shopOrNpcId, CancellationToken token)
        {
            if (player is null) return;

            var shop = _shopService.OpenShop(player, shopOrNpcId);
            if (shop is null)
            {
                _log.LogDebug("[Channel] NPC shop not found shopOrNpcId={Id}", shopOrNpcId);
                return;
            }

            await session.SendAsync(V113ShopPackets.OpenNpcShop(shop), token);
        }

        async Task OpenStorageFromNpcAsync(int npcId, CancellationToken token)
        {
            if (player is null) return;

            storageNpcId = npcId;
            var result = _storageService.Open(player);
            var packet = V113StoragePackets.EncodeResult(result, npcId, player.Storage);
            if (packet is not null)
            {
                await session.SendAsync(packet, token);
            }
        }

        async Task WarpFromNpcAsync(int mapId, CancellationToken token)
        {
            if (player is null) return;
            currentField = await WarpAsync(player, currentField, npcOidToId, session, mapId, sessionToken, token);
        }

        // 腳走地圖傳送點（CHANGE_MAP 0x1E）。對照 Java PlayerHandler.ChangeMap 正常 portal 分支。
        async Task HandleChangeMapAsync(PacketReader r, CancellationToken token)
        {
            if (player is null || chr is null) return;
            var req = V113MapPackets.ParseChangeMap(r);

            if (req.TargetId == -1)
            {
                // 一般腳走傳送點：查當前圖 portal(by name) → 取目標圖(tm)+目標 portal(tn) → 換圖落地
                var map = _mapService.LoadMap(chr.MapId);
                var portal = map.Portals.FirstOrDefault(p => p.Name == req.PortalName);
                if (portal is null || portal.TargetMapId == NoTargetMapId || !string.IsNullOrEmpty(portal.Script))
                {
                    // 查不到 / 無目標 / script portal(MVP 未實作 PortalScript) → 放行不卡（不改角色狀態）
                    if (portal is not null && !string.IsNullOrEmpty(portal.Script))
                        _log.LogInformation("[Channel] script portal '{Portal}'(地圖 {Map}) 尚未實作，放行", req.PortalName, chr.MapId);
                    else
                        _log.LogDebug("[Channel] portal '{Portal}'(地圖 {Map}) 無目標/不存在，放行", req.PortalName, chr.MapId);
                    await session.SendAsync(V113StatsPackets.EnableActions(), token);
                    return;
                }

                // 目標 portal id → 客戶端據 SET_FIELD 的 SpawnPoint byte 落地（tn 找不到 fallback 0）
                var targetMap = _mapService.LoadMap(portal.TargetMapId);
                var targetPortal = targetMap.Portals.FirstOrDefault(p => p.Name == portal.TargetPortalName);
                int spawnPortalId = targetPortal?.Id ?? 0;

                currentField = await WarpAsync(player, currentField, npcOidToId, session, portal.TargetMapId, sessionToken, token, spawnPortalId);
                _log.LogInformation("[Channel] {Name} 走 portal '{Portal}' → 地圖 {Map}（落地 portal {Pid}）", chr.Name, req.PortalName, portal.TargetMapId, spawnPortalId);
            }
            else
            {
                // 死亡/特殊 targetid：MVP 回當前圖的 ReturnMap portal 0（不卡死）；無效則放行
                var map = _mapService.LoadMap(chr.MapId);
                if (map.ReturnMapId is not (0 or NoTargetMapId) && map.ReturnMapId != chr.MapId)
                {
                    currentField = await WarpAsync(player, currentField, npcOidToId, session, map.ReturnMapId, sessionToken, token, 0);
                    _log.LogInformation("[Channel] {Name} 死亡/特殊換圖 → ReturnMap {Map}", chr.Name, map.ReturnMapId);
                }
                else
                {
                    await session.SendAsync(V113StatsPackets.EnableActions(), token);
                }
            }
        }

        async Task SendExpiredBuffCancelsAsync(MapleSession target, CancellationToken token)
        {
            if (player is null) return;

            var packets = V113SkillMoveHandler.CancelExpiredBuffs(player, _skillService, DateTimeOffset.UtcNow);
            foreach (var packet in packets)
            {
                await target.SendAsync(packet, token);
            }
        }

        try
        {
            await session.RunAsync(async (body, s, token) =>
            {
                if (body.Length < 2) return;
                var reader = new PacketReader(body);
                var opcode = reader.ReadShort();

                await SendExpiredBuffCancelsAsync(s, token);

                if (cashShopMode)
                {
                    await HandleCashShopModePacketAsync(opcode, reader, player, account, s, token);
                    return;
                }

                switch (opcode)
                {
                    case V113ChannelRecvOp.PlayerLoggedIn:
                    {
                        var charId = reader.ReadInt();
                        _log.LogInformation("[Channel] PLAYER_LOGGEDIN charId={Id}", charId);
                        var enteringCashShop = TryConsumeCashShopTransition(
                            _pendingCashShopTransitions,
                            charId,
                            out var transition);

                        chr = await _charService.GetByIdAsync(charId, token);
                        if (chr is not null)
                        {
                            if (enteringCashShop)
                            {
                                cashShopTransition = transition;
                                cashShopMode = true;
                                chr.MapId = transition.PreviousMapId;
                            }

                            account = await _accounts.FindByIdAsync(chr.AccountId, token);

                            // 執行期玩家（持有位置；spawn 暫定 0,0，之後接 portal/SpawnPoint）
                            player = new Player(chr, new Position(0, 0, 0, 0));
                            if (account is not null)
                            {
                                player.AttachStorage(account);
                            }
                            else
                            {
                                _log.LogWarning("[Channel] 角色 {Name} 找不到 AccountId={AccountId}，倉庫不會持久化", chr.Name, chr.AccountId);
                            }

                            if (cashShopMode)
                            {
                                await SendCashShopInitialPacketsAsync(player, account, s, token);
                                _log.LogInformation(
                                    "[Channel] 角色 {Name} 進入 CashShop mode，原地圖 {Map} channel={Channel}",
                                    chr.Name,
                                    cashShopTransition?.PreviousMapId,
                                    cashShopTransition?.PreviousChannel);
                                break;
                            }

                            await SendNormalChannelEntryPacketsAsync(chr, s, token);

                            var channel = _options.ChannelIndex + 1;
                            _onlinePlayers.Register(
                                player,
                                channel,
                                (pkt, tkn) => s.SendAsync(pkt, tkn),
                                sessionToken);
                            _tradeService.RegisterPlayer(
                                player,
                                channel,
                                (pkt, tkn) => s.SendAsync(pkt, tkn),
                                sessionToken);

                            await _buddyHandler.OnPlayerLoggedInAsync(
                                player,
                                s,
                                channel,
                                token);

                            await _guildOperationHandler.OnPlayerLoggedInAsync(
                                player,
                                channel,
                                (pkt, tkn) => s.SendAsync(pkt, tkn),
                                token);

                            await _familyHandler.NotifyLoginAsync(player, channel, token);

                            await _partyOperationHandler.NotifyLoginAsync(
                                player,
                                _options.ChannelIndex,
                                (pkt, tkn) => s.SendAsync(pkt, tkn),
                                token);

                            var pos = player.Position;
                            currentField = EnterField(chr.MapId, player);

                            _mapRegistry.Register(chr.MapId, chr.Id, chr, (pkt, tkn) => s.SendAsync(pkt, tkn), sessionToken);
                            await _partySearchHandler.NotifyMapEntryAsync(player, (pkt, tkn) => s.SendAsync(pkt, tkn), token);
                            await _partyOperationHandler.NotifyMapEntryAsync(player, _options.ChannelIndex, (pkt, tkn) => s.SendAsync(pkt, tkn), token);

                            // Notify existing players of new arrival（並讓新玩家看到現有玩家）
                            var others = _mapRegistry.GetOthers(chr.MapId, chr.Id);
                            foreach (var other in others)
                            {
                                var spawnForOther = await BuildSpawnPlayerPacketAsync(chr, pos.X, pos.Y, pos.Stance, pos.Foothold, token);
                                await other.SendPacket(spawnForOther, token);

                                var spawnForNew = await BuildSpawnPlayerPacketAsync(other.Character, 0, 0, 0, 0, token);
                                await s.SendAsync(spawnForNew, token);
                            }

                            // 地圖物件同步：把該地圖的 NPC / monster spawn 給剛進場的玩家。
                            await SpawnMapNpcsAsync(chr.MapId, s, npcOidToId, token);
                            await SendFieldHiredMerchantsAsync(chr.MapId, s, token);
                            await SendFieldMonstersAsync(currentField, s, token);
                            await SendFieldDropsAsync(currentField, s, token);
                            await V113ReactorHandler.SendFieldReactorsAsync(currentField, s, token);

                            _log.LogInformation("[Channel] 角色 {Name} 已進入地圖 {Map}，同地圖 {Count} 人", chr.Name, chr.MapId, others.Count);
                        }
                        else
                        {
                            _log.LogWarning("[Channel] 找不到角色 id={Id}", charId);
                        }
                        break;
                    }

                    case V113ChannelRecvOp.MovePlayer:
                        if (player is null) break;
                        TryUpdateMovement(player, body);                              // 解析→更新 server 權威位置(Core)
                        await BroadcastToOthersAsync(player.Character, body, token);  // 原始 blob 轉發(動畫擬真)
                        break;

                    case V113ChannelRecvOp.UseChair:
                        if (player is null) break;
                        await HandleUseChairAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.CancelChair:
                        if (player is null) break;
                        await HandleCancelChairAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.ShowExpChair:
                        if (player is null) break;
                        _ = V113RewardItemHandler.ParseShowExpChair(reader);
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.ChangeMap:
                        if (player is null) break;
                        await HandleChangeMapAsync(reader, token);                    // 腳走傳送點換圖
                        break;

                    case V113ChannelRecvOp.ChangeChannel:
                        if (player is null) break;
                        var targetChannel = V113ChannelChangePackets.ParseChangeChannel(reader);
                        player.FlushInventory();
                        await _charService.UpdateAsync(player.Character, token);
                        var channelIp = _options.ChannelIp ?? new byte[] { 127, 0, 0, 1 };
                        await s.SendAsync(V113ChannelChangePackets.ChangeChannel(channelIp, (short)_options.ChannelPort), token);
                        _log.LogInformation("[Channel] {Name} change channel target={Target} → {Ip}:{Port}",
                            player.Character.Name, targetChannel, string.Join(".", channelIp), _options.ChannelPort);
                        break;

                    case V113ChannelRecvOp.EnterCashShop:
                        if (player is null) break;
                        if (!CanEnterCashShopFromMap(player.Character.MapId))
                        {
                            await s.SendAsync(V113StatsPackets.EnableActions(), token);
                            break;
                        }

                        player.FlushInventory();
                        await _charService.UpdateAsync(player.Character, token);
                        await RemoveFromCurrentFieldForTransitionAsync(player, currentField, sessionToken, token);
                        currentField = null;

                        var cashShopTransitionData = RegisterCashShopTransition(
                            _pendingCashShopTransitions,
                            player.Character.Id,
                            player.Character.MapId,
                            _options.ChannelIndex + 1);
                        var cashShopIp = _options.ChannelIp ?? new byte[] { 127, 0, 0, 1 };
                        await s.SendAsync(V113ChannelChangePackets.ChangeChannel(cashShopIp, (short)_options.ChannelPort), token);
                        _log.LogInformation(
                            "[Channel] {Name} ENTER_CASH_SHOP pending transition map={Map} channel={Channel} → {Ip}:{Port}",
                            player.Character.Name,
                            cashShopTransitionData.PreviousMapId,
                            cashShopTransitionData.PreviousChannel,
                            string.Join(".", cashShopIp),
                            _options.ChannelPort);
                        throw new OperationCanceledException("Cash shop reconnect requested.");

                    case V113ChannelRecvOp.UseInnerPortal:
                        if (player is null) break;
                        await HandleUseInnerPortalAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.CloseRangeAttack:
                    case V113ChannelRecvOp.PassiveEnergy:
                        if (player is null || currentField is null) break;
                        await HandleCloseRangeAttackAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.RangedAttack:
                        if (player is null || currentField is null) break;
                        await HandleRangedAttackAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.MagicAttack:
                        if (player is null || currentField is null) break;
                        await HandleMagicAttackAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.TakeDamage:
                        if (player is null || currentField is null) break;
                        await HandleTakeDamageAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.DamageReactor:
                        if (player is null || currentField is null) break;
                        await V113ReactorHandler.HandleDamageReactorAsync(
                            reader,
                            player,
                            currentField,
                            _reactorService,
                            (packet, tkn) => BroadcastPacketToMapAsync(player.Character, s, packet, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.TouchReactor:
                    {
                        if (player is null || currentField is null) break;
                        // 對照 DamageReactor：觸碰觸發後把 reactor 狀態變化（trigger/destroy）廣播給同圖。
                        var touchResult = V113ReactorHandler.HandleTouchReactor(reader, player, currentField, _reactorService);
                        if (touchResult?.Hit is { } touchHit
                            && V113ReactorPackets.EncodeHitResult(touchHit) is { } touchPacket)
                        {
                            await BroadcastPacketToMapAsync(player.Character, s, touchPacket, token);
                        }
                        break;
                    }

                    case V113ChannelRecvOp.MoveLife:
                        if (player is null || currentField is null) break;
                        await HandleMoveLifeAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.AutoAggro:
                        if (player is null || currentField is null) break;
                        await HandleAutoAggroAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.FriendlyDamage:
                        if (player is null) break;
                        if (reader.Remaining < 8) break;
                        _ = reader.ReadInt();
                        _ = reader.ReadInt();
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.MonsterBomb:
                        if (player is null || currentField is null) break;
                        await HandleMonsterBombAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.HypnotizeDmg:
                        if (player is null) break;
                        if (reader.Remaining < 12) break;
                        _ = reader.ReadInt();
                        _ = reader.ReadInt();
                        _ = reader.ReadInt();
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.MobNode:
                        if (player is null || currentField is null) break;
                        await HandleMobNodeAsync(reader, player, currentField);
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.DisplayNode:
                        if (player is null) break;
                        if (reader.Remaining < 4) break;
                        _ = reader.ReadInt();
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.GeneralChat:
                        if (player is null) break;
                        await HandleGeneralChatAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.CloseChalkboard:
                        if (player is null) break;
                        await HandleCloseChalkboardAsync(player, s, token);
                        break;

                    case V113ChannelRecvOp.FaceExpression:
                        if (player is null) break;
                        await HandleFaceExpressionAsync(reader, player, token);
                        break;

                    case V113ChannelRecvOp.UseItemEffect:
                    case V113ChannelRecvOp.WheelOfFortune:
                        if (player is null) break;
                        await HandleUseItemEffectAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.DueyAction:
                        if (player is null) break;
                        await _dueyHandler.HandleActionAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.Owl:
                        if (player is null) break;
                        await HandleOwlResultAsync(_owlHandler.HandleOpen(player), player, currentField, npcOidToId, s, sessionToken, token);
                        break;

                    case V113ChannelRecvOp.OwlWarp:
                        if (player is null) break;
                        currentField = await HandleOwlResultAsync(_owlHandler.HandleWarp(reader, player), player, currentField, npcOidToId, s, sessionToken, token);
                        break;

                    case V113ChannelRecvOp.MonsterBookCover:
                        if (player is null) break;
                        await HandleMonsterBookCoverAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.HiredMerchantRemoteControl:
                        if (player is null) break;
                        await SendHiredMerchantResultAsync(
                            await _hiredMerchantHandler.HandleRemoteControlAsync(
                                reader,
                                player,
                                DateTimeOffset.UtcNow,
                                token),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.GiveFame:
                        if (player is null) break;
                        await HandleGiveFameAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.MesoDrop:
                        if (player is null || currentField is null) break;
                        await HandleMesoDropAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.CharInfoRequest:
                        if (player is null) break;
                        await HandleCharInfoRequestAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.TrockAddMap:
                        if (player is null) break;
                        await HandleTrockAddMapAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.AntiMacroItemUse:
                        if (player is null) break;
                        _ = reader.ReadInt();
                        _ = reader.ReadByte();
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.AntiMacroSkillUse:
                        if (player is null) break;
                        _ = reader.ReadInt();
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.OldAntiMacroQuestion:
                        if (player is null) break;
                        var antiMacroAnswer = reader.ReadMapleString();
                        _log.LogInformation(
                            "[Channel] OLD_ANTI_MACRO answer charId={CharId} inputCode={InputCode}",
                            player.Character.Id,
                            antiMacroAnswer);
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.DistributeAp:
                        if (player is null) break;
                        await HandleStatsMutationAsync(
                            V113StatsHandlers.HandleDistributeAp(reader, player, _statsService),
                            s,
                            sendSkill: false,
                            token);
                        break;

                    case V113ChannelRecvOp.HealOverTime:
                        if (player is null) break;
                        await HandleStatsMutationAsync(
                            V113StatsHandlers.HandleHealOverTime(reader, player, _statsService),
                            s,
                            sendSkill: false,
                            token);
                        break;

                    case V113ChannelRecvOp.DistributeSp:
                        if (player is null) break;
                        await HandleStatsMutationAsync(
                            V113StatsHandlers.HandleDistributeSp(reader, player, _statsService),
                            s,
                            sendSkill: true,
                            token);
                        break;

                    case V113ChannelRecvOp.SpecialMove:
                        if (player is null) break;
                        await HandleSpecialMoveAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.CancelBuff:
                        if (player is null) break;
                        await HandleCancelBuffAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.QuestAction:
                        if (player is null) break;
                        await HandleQuestActionAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.CalcDamageStatSetRequest:
                        break;

                    case V113ChannelRecvOp.ThrowGrenade:
                        if (player is null) break;
                        _ = V113RewardItemHandler.ParseThrowGrenade(reader);
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.SkillMacro:
                        if (player is null) break;
                        await HandleSkillMacroAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.RewardItem:
                        if (player is null) break;
                        await HandleRewardItemResultAsync(
                            V113RewardItemHandler.HandleRewardItem(reader, player),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.ItemMaker:
                        if (player is null) break;
                        await HandleItemMakerResultAsync(
                            V113ItemMakerHandler.Handle(reader, player, _itemMakerService),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.UseTreasureChest:
                        if (player is null) break;
                        await HandleRewardItemResultAsync(
                            V113RewardItemHandler.HandleTreasureChest(reader, player, _randomRewardsCatalog, _options.ChannelIndex + 1),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.PartyChat:
                        if (player is null) break;
                        await _chatHandler.HandleGroupChatAsync(reader, player, token);
                        break;

                    case V113ChannelRecvOp.Whisper:
                        if (player is null) break;
                        await _chatHandler.HandleWhisperFindAsync(
                            reader,
                            player,
                            _options.ChannelIndex + 1,
                            (pkt, tkn) => s.SendAsync(pkt, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.PlayerInteraction:
                        if (player is null) break;
                        var playerInteractionMutated = await _playerInteractionRouter.HandleAsync(
                            reader,
                            player,
                            (packet, tkn) => s.SendAsync(packet, tkn),
                            (packet, tkn) => BroadcastPacketToMapAsync(player.Character, s, packet, tkn),
                            (byte)(_options.ChannelIndex + 1),
                            DateTimeOffset.UtcNow,
                            token);
                        if (playerInteractionMutated)
                        {
                            await _charService.UpdateAsync(player.Character, token);
                        }
                        break;

                    case V113ChannelRecvOp.PartyOperation:
                        if (player is null) break;
                        await _partyOperationHandler.HandlePartyOperationAsync(
                            reader,
                            player,
                            _options.ChannelIndex,
                            (pkt, tkn) => s.SendAsync(pkt, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.GuildOperation:
                        if (player is null) break;
                        await _guildOperationHandler.HandleGuildOperationAsync(
                            reader,
                            player,
                            _options.ChannelIndex + 1,
                            (pkt, tkn) => s.SendAsync(pkt, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.DenyGuildRequest:
                        if (player is null) break;
                        await _guildOperationHandler.HandleDenyGuildRequestAsync(reader, player, token);
                        break;

                    case V113ChannelRecvOp.BbsOperation:
                        if (player is null) break;
                        await _bbsHandler.HandleBbsOperationAsync(
                            reader,
                            player,
                            (pkt, tkn) => s.SendAsync(pkt, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.BuddyListModify:
                        if (player is null) break;
                        await _buddyHandler.HandleModifyAsync(
                            reader,
                            player,
                            s,
                            _options.ChannelIndex + 1,
                            token);
                        break;

                    // FOLLOW_REQUEST / FOLLOW_REPLY are intentionally not dispatched:
                    // this v113 recv.properties disables them, and candidate FOLLOW_REPLY 0x7A conflicts with BuddyListModify.
                    case V113ChannelRecvOp.RpsGame:
                        if (player is null) break;
                        await HandleEventMiniGameResultAsync(
                            _eventMiniGameHandler.HandleRpsGame(reader, player),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.RingAction:
                        if (player is null) break;
                        await _ringHandler.HandleRingActionAsync(
                            reader,
                            player,
                            _mapRegistry,
                            (pkt, tkn) => s.SendAsync(pkt, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.CygnusSummon:
                        if (player is null) break;
                    {
                        var result = V113PlayerEventHandler.HandleCygnusSummon(player);
                        if (result.StartNpcId is { } npcId)
                        {
                            conversation = await StartNpcConversationByNpcIdAsync(
                                npcId,
                                player,
                                s,
                                OpenShopFromNpcAsync,
                                OpenStorageFromNpcAsync,
                                WarpFromNpcAsync,
                                token);
                            if (conversation is null)
                            {
                                await s.SendAsync(V113StatsPackets.EnableActions(), token);
                            }
                            break;
                        }

                        await SendPlayerEventResultAsync(result, s, token);
                        break;
                    }

                    case V113ChannelRecvOp.ItemUnlock:
                        if (player is null) break;
                        await HandleItemUnlockAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.ChangeKeymap:
                        if (player is null) break;
                        await HandleChangeKeymapAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.UpdateCharInfo:
                        if (player is null) break;
                        await HandleUpdateCharInfoAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.EnterMts:
                        if (player is null) break;
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.Solomon:
                        if (player is null) break;
                        await HandleBuffItemResultAsync(_buffItemHandler.HandleSolomon(reader, player), player, s, token);
                        break;

                    case V113ChannelRecvOp.GachExp:
                        if (player is null) break;
                        await HandleBuffItemResultAsync(_buffItemHandler.HandleGachExp(reader, player), player, s, token);
                        break;

                    case V113ChannelRecvOp.TransformPlayer:
                        if (player is null || currentField is null) break;
                        await HandleTransformPlayerAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.XmasSurprise:
                        if (player is null || account is null) break;
                        await HandleBuffItemResultAsync(_buffItemHandler.HandleXmasSurprise(reader, account, player), player, s, token);
                        break;

                    case V113ChannelRecvOp.GamePoll:
                        if (player is null) break;
                        await s.SendAsync(V113UserInterfaceHandler.HandleGamePoll(reader), token);
                        break;

                    case V113ChannelRecvOp.UpdateQuest:
                        if (player is null) break;
                        await HandleUpdateQuestAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.QuestItem:
                        break;

                    case V113ChannelRecvOp.UseItemQuest:
                        break;

                    case V113ChannelRecvOp.UseSummonBag:
                    {
                        if (player is null || currentField is null) break;
                        var ctx = new V113ItemUseContext { ReturnMapId = player.Character.MapId };
                        var result = _itemUseHandler.HandleUseSummonBag(reader, player, ctx);
                        currentField = await HandleItemUseResultAsync(result, player, currentField, npcOidToId, s, sessionToken, token);
                        break;
                    }

                    case V113ChannelRecvOp.UseMountFood:
                    {
                        if (player is null || currentField is null) break;
                        var result = _itemUseHandler.HandleUseMountFood(reader, player);
                        currentField = await HandleItemUseResultAsync(result, player, currentField, npcOidToId, s, sessionToken, token);
                        break;
                    }

                    case V113ChannelRecvOp.UseScriptedNpcItem:
                        if (player is null) break;
                        await HandleScriptedNpcItemAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.UseCatchItem:
                    {
                        if (player is null || currentField is null) break;
                        var field = currentField;
                        var result = _itemUseHandler.HandleUseCatchItem(reader, player, oid =>
                        {
                            lock (field) { return field.Get(oid) is Mob m ? V113ItemUseTargetMob.From(m) : null; }
                        });
                        currentField = await HandleItemUseResultAsync(result, player, currentField, npcOidToId, s, sessionToken, token);
                        break;
                    }

                    case V113ChannelRecvOp.UseTeleRock:
                        if (player is null || currentField is null) break;
                        currentField = await HandleUseTeleRockAsync(reader, player, currentField, npcOidToId, s, sessionToken, token);
                        break;

                    case V113ChannelRecvOp.UseReturnScroll:
                    {
                        if (player is null || currentField is null) break;
                        var ctx = new V113ItemUseContext { ReturnMapId = player.Character.MapId };
                        var result = _itemUseHandler.HandleUseReturnScroll(reader, player, ctx);
                        currentField = await HandleItemUseResultAsync(result, player, currentField, npcOidToId, s, sessionToken, token);
                        break;
                    }

                    case V113ChannelRecvOp.UseUpgradeScroll:
                    {
                        if (player is null) break;
                        await HandleScrollResultAsync(_scrollHandler.HandleUseUpgradeScroll(reader, player), player, s, token);
                        break;
                    }

                    case V113ChannelRecvOp.UseSkillBook:
                        if (player is null) break;
                        await HandleUseSkillBookAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.NpcTalk:
                        if (player is null) break;
                        conversation = await StartNpcConversationAsync(
                            reader,
                            player,
                            npcOidToId,
                            s,
                            OpenShopFromNpcAsync,
                            OpenStorageFromNpcAsync,
                            WarpFromNpcAsync,
                            token);
                        break;

                    case V113ChannelRecvOp.NpcTalkMore:
                        if (conversation is null) break;
                        await ContinueNpcConversationAsync(reader, conversation, token);
                        if (!conversation.Active) conversation = null;
                        break;

                    case V113ChannelRecvOp.NpcShop:
                        if (player is null) break;
                        await HandleNpcShopAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.Storage:
                        if (player is null) break;
                        if (await HandleStorageAsync(reader, player, storageNpcId ?? 0, account, s, token))
                        {
                            storageNpcId = null;
                        }
                        break;

                    case V113ChannelRecvOp.UseHiredMerchant:
                        if (player is null) break;
                        await SendHiredMerchantResultAsync(
                            await _hiredMerchantHandler.HandleUseHiredMerchantAsync(
                                reader,
                                player,
                                (byte)(_options.ChannelIndex + 1),
                                DateTimeOffset.UtcNow,
                                token),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.MerchItemStore:
                        if (player is null) break;
                        await SendHiredMerchantResultAsync(
                            await _hiredMerchantHandler.HandleMerchItemStoreAsync(reader, player, token),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.ItemMove:
                        if (player is null) break;
                        await HandleItemMoveAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.UseItem:
                        if (player is null) break;
                        var useItemResult = _useConsumableHandler.Handle(reader, player);
                        if (useItemResult.Handled)
                        {
                            foreach (var packet in useItemResult.Packets)
                            {
                                await s.SendAsync(packet, token);
                            }

                            if (useItemResult.CharacterMutated)
                            {
                                await _charService.UpdateAsync(player.Character, token);
                            }
                        }
                        break;

                    case V113ChannelRecvOp.ItemGather:
                        if (player is null) break;
                        await HandleItemArrangeAsync(reader, player, s, isSort: false, token);
                        break;

                    case V113ChannelRecvOp.ItemSort:
                        if (player is null) break;
                        await HandleItemArrangeAsync(reader, player, s, isSort: true, token);
                        break;

                    case V113ChannelRecvOp.CancelItemEffect:
                        if (player is null) break;
                        await HandleCancelItemEffectAsync(reader, player, token);
                        break;

                    case V113ChannelRecvOp.UseCashItem:
                        if (player is null) break;
                        var useCashResult = await _useCashItemHandler.HandleAsync(
                            reader,
                            player,
                            _options.ChannelIndex + 1,
                            token);
                        if (useCashResult.Handled)
                        {
                            foreach (var pkt in useCashResult.Packets)
                            {
                                await s.SendAsync(pkt, token);
                            }

                            foreach (var pkt in useCashResult.BroadcastPackets)
                            {
                                await BroadcastPacketToOthersAsync(player.Character, pkt, token);
                            }

                            foreach (var pkt in useCashResult.MapPackets)
                            {
                                await BroadcastPacketToMapAsync(player.Character, s, pkt, token);
                            }

                            foreach (var pkt in useCashResult.ChannelBroadcastPackets)
                            {
                                await BroadcastPacketToAllOnlineAsync(pkt, token);
                            }

                            if (useCashResult.CharacterMutated)
                            {
                                await _charService.UpdateAsync(player.Character, token);
                            }

                            if (useCashResult.WarpToMapId is { } cashWarpMapId)
                            {
                                _log.LogInformation(
                                    "[Channel] USE_CASH_ITEM warp charId={CharId} mapId={MapId}",
                                    player.Character.Id,
                                    cashWarpMapId);
                                currentField = await WarpAsync(
                                    player,
                                    currentField,
                                    npcOidToId,
                                    s,
                                    cashWarpMapId,
                                    sessionToken,
                                    token);
                            }
                        }
                        break;

                    case V113ChannelRecvOp.UseOwlMinerva:
                        if (player is null) break;
                        await HandleOwlResultAsync(_owlHandler.HandleMinerva(reader, player), player, currentField, npcOidToId, s, sessionToken, token);
                        break;

                    // REPAIR / REPAIR_ALL are intentionally not dispatched:
                    // this v113 recv.properties disables them, and commented 0x73/0x72 conflict with PlayerInteraction/Messenger.
                    case V113ChannelRecvOp.ItemPickup:
                        if (player is null || currentField is null) break;
                        await HandleItemPickupAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.Coconut:
                        if (player is null) break;
                        await HandleEventMiniGameResultAsync(
                            _eventMiniGameHandler.HandleCoconut(reader, player),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.MonsterCarnival:
                        if (player is null) break;
                        _ = reader.ReadByte();
                        _ = reader.ReadInt();
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.Snowball:
                        if (player is null) break;
                        await SendPlayerEventResultAsync(V113PlayerEventHandler.HandleSnowball(reader), s, token);
                        break;

                    case V113ChannelRecvOp.LeftKnockBack:
                        if (player is null) break;
                        await SendPlayerEventResultAsync(V113PlayerEventHandler.HandleLeftKnockBack(reader, player), s, token);
                        break;

                    case V113ChannelRecvOp.CsUpdate:
                        if (player is null || account is null) break;
                        await s.SendAsync(V113CashShopPackets.ShowCashBalances(account), token);
                        await s.SendAsync(
                            V113CashShopPackets.ShowCashInventory(
                                player.Inventory.By(Core.Inventory.InventoryType.Cash).Items,
                                account.Id,
                                account.Storage.Slots,
                                characterSlots: 3),
                            token);
                        break;

                    case V113ChannelRecvOp.CashShopOperation:
                        if (player is null || account is null) break;
                        var cashShopResult = _cashShopOperationHandler.Handle(reader, account, player);
                        if (!cashShopResult.Handled) break;

                        foreach (var packet in cashShopResult.Packets)
                        {
                            await s.SendAsync(packet, token);
                        }

                        if (cashShopResult.AccountMutated)
                        {
                            await _accounts.UpdateAsync(account, token);
                        }

                        if (cashShopResult.CharacterMutated)
                        {
                            await _charService.UpdateAsync(player.Character, token);
                        }
                        break;

                    case V113ChannelRecvOp.CouponCode:
                        if (player is null || account is null) break;
                        var couponResult = await _cashShopOperationHandler.HandleCouponCodeAsync(
                            reader,
                            account,
                            player,
                            DateTimeOffset.UtcNow,
                            token);
                        foreach (var packet in couponResult.Packets)
                        {
                            await s.SendAsync(packet, token);
                        }
                        if (couponResult.AccountMutated)
                        {
                            await _accounts.UpdateAsync(account, token);
                        }
                        if (couponResult.CharacterMutated)
                        {
                            await _charService.UpdateAsync(player.Character, token);
                        }
                        break;

                    case V113ChannelRecvOp.TouchingMts:
                        if (player is null) break;
                        _ = reader.ReadByte();
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.MtsTab:
                        if (player is null) break;
                        _ = reader.ReadInt();
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        break;

                    case V113ChannelRecvOp.SpawnPet:
                        if (player is null) break;
                        await V113PetHandler.HandleSpawnPetAsync(reader, player, s, _petService, (pkt, tkn) => BroadcastPacketToOthersAsync(player.Character, pkt, tkn), token);
                        break;

                    case V113ChannelRecvOp.MovePet:
                        if (player is null) break;
                        await V113PetHandler.HandleMovePetAsync(reader, player, s, _petService, (pkt, tkn) => BroadcastPacketToOthersAsync(player.Character, pkt, tkn), token);
                        break;

                    case V113ChannelRecvOp.PetFood:
                        if (player is null) break;
                        await V113PetHandler.HandlePetFoodAsync(reader, player, s, _petService, (pkt, tkn) => BroadcastPacketToOthersAsync(player.Character, pkt, tkn), token);
                        break;

                    case V113ChannelRecvOp.PetChat:
                        if (player is null) break;
                        await V113PetHandler.HandlePetChatAsync(reader, player, s, _petService, (pkt, tkn) => BroadcastPacketToOthersAsync(player.Character, pkt, tkn), token);
                        break;

                    case V113ChannelRecvOp.PetCommand:
                        if (player is null) break;
                        await V113PetHandler.HandlePetCommandAsync(reader, player, s, _petService, (pkt, tkn) => BroadcastPacketToOthersAsync(player.Character, pkt, tkn), token);
                        break;

                    case V113ChannelRecvOp.PetLoot:
                        if (player is null) break;
                        await V113PetHandler.HandlePetLootAsync(reader, player, _petService, (pkt, tkn) => BroadcastPacketToOthersAsync(player.Character, pkt, tkn), token);
                        break;

                    case V113ChannelRecvOp.PetAutoPot:
                        if (player is null) break;
                        await V113PetHandler.HandlePetAutoPotion(reader, player, s, _petService, token);
                        break;

                    case V113ChannelRecvOp.PetIgnore:
                        if (player is null) break;
                        await V113PetHandler.HandlePetIgnore(reader, player, s, _petService, token);
                        break;

                    case V113ChannelRecvOp.MoveSummon:
                        if (player is null || currentField is null) break;
                        await V113SummonHandler.HandleMoveSummonAsync(reader, player, currentField, _mapRegistry, token);
                        break;

                    case V113ChannelRecvOp.SummonAttack:
                        if (player is null || currentField is null) break;
                        await V113SummonHandler.HandleSummonAttackAsync(reader, player, currentField, _mapRegistry, token);
                        break;

                    case V113ChannelRecvOp.DamageSummon:
                        if (player is null || currentField is null) break;
                        await V113SummonHandler.HandleDamageSummonAsync(reader, player, currentField, s, _mapRegistry, token);
                        break;

                    case V113ChannelRecvOp.SubSummon:
                        if (player is null) break;
                        await V113SummonHandler.HandleSubSummonAsync(reader, s, token);
                        break;

                    case V113ChannelRecvOp.NpcAction:
                        if (player is null) break;
                        await HandleNpcActionAsync(reader, player, body.Length, npcOidToId, s, token);
                        break;

                    case V113ChannelRecvOp.ChangeMapSpecial:
                        if (player is null) break;
                        currentField = await HandleChangeMapSpecialAsync(reader, player, currentField, npcOidToId, s, sessionToken, token);
                        break;

                    case V113ChannelRecvOp.SkillEffect:
                        if (player is null) break;
                        await HandleSkillEffectAsync(reader, player, token);
                        break;

                    case V113ChannelRecvOp.StrangeData:
                    case V113ChannelRecvOp.CancelDebuff:
                        break;

                    case V113ChannelRecvOp.AutoAssignAp:
                        if (player is null) break;
                        await HandleStatsMutationAsync(
                            V113StatsHandlers.HandleAutoAssignAp(reader, player),
                            s,
                            sendSkill: false,
                            token);
                        break;

                    case V113ChannelRecvOp.DenyPartyRequest:
                        if (player is null) break;
                        await _partyOperationHandler.HandleDenyPartyRequestAsync(
                            reader,
                            player,
                            _options.ChannelIndex,
                            (pkt, tkn) => s.SendAsync(pkt, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.PartySearchStart:
                        if (player is null) break;
                        await _partySearchHandler.HandleStartAsync(
                            reader,
                            player,
                            (pkt, tkn) => s.SendAsync(pkt, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.PartySearchStop:
                        if (player is null) break;
                        _partySearchHandler.HandleStop(player);
                        break;

                    case V113ChannelRecvOp.MapleTV:
                        if (player is null) break;
                        await s.SendAsync(V113UserInterfaceHandler.HandleMapleTv(reader), token);
                        break;

                    case V113ChannelRecvOp.BeansUpdate:
                        if (player is null) break;
                        await HandleEventMiniGameResultAsync(
                            _eventMiniGameHandler.HandleBeansUpdate(reader, player),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.BeansGameAction:
                        if (player is null) break;
                        await HandleEventMiniGameResultAsync(
                            _eventMiniGameHandler.HandleBeansGameAction(reader, player),
                            player,
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.AranCombo:
                        if (player is null) break;
                        await SendPlayerEventResultAsync(
                            V113PlayerEventHandler.HandleAranCombo(reader, player, _skillService, DateTimeOffset.UtcNow),
                            s,
                            token);
                        break;

                    case V113ChannelRecvOp.Messenger:
                        if (player is null) break;
                        await _messengerHandler.HandleMessengerAsync(
                            reader,
                            player,
                            _options.ChannelIndex + 1,
                            (pkt, tkn) => s.SendAsync(pkt, tkn),
                            token);
                        break;

                    case V113ChannelRecvOp.AllianceOperation:
                        if (player is null) break;
                        {
                            var allianceResult = await _allianceHandler.HandleAllianceOperationAsync(reader, player, token);
                            foreach (var pkt in allianceResult.SelfPackets)
                                await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.DenyAllianceRequest:
                        if (player is null) break;
                        {
                            var denyResult = await _allianceHandler.HandleDenyAllianceRequestAsync(reader, player, token);
                            foreach (var pkt in denyResult.SelfPackets)
                                await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.NoteAction:
                        if (player is null) break;
                        await _noteHandler.HandleNoteActionAsync(reader, player, s, token);
                        break;

                    case V113ChannelRecvOp.UseDoor:
                        if (player is null || currentField is null) break;
                        {
                            var doorResult = await _doorHandler.HandleUseDoorAsync(reader, player, player.Character.MapId);
                            foreach (var pkt in doorResult.SelfPackets)
                                await s.SendAsync(pkt, token);
                            if (doorResult.Warp is { CanWarp: true } doorWarp)
                            {
                                currentField = await WarpAsync(
                                    player, currentField, npcOidToId, s,
                                    doorWarp.DestinationMapId, sessionToken, token);
                            }
                        }
                        break;

                    case V113ChannelRecvOp.RequestFamily:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleRequestFamilyAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.OpenFamily:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleOpenFamilyAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.FamilyOperation:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleFamilyOperationAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.DeleteJunior:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleDeleteJuniorAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.DeleteSenior:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleDeleteSeniorAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.AcceptFamily:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleAcceptFamilyAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.UseFamily:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleUseFamilyAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                            if (famResult.Warp is { } famWarp)
                            {
                                currentField = await WarpAsync(
                                    player, currentField, npcOidToId, s,
                                    famWarp.DestinationMapId, sessionToken, token);
                            }
                        }
                        break;

                    case V113ChannelRecvOp.FamilyPrecept:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleFamilyPreceptAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                        }
                        break;

                    case V113ChannelRecvOp.FamilySummon:
                        if (player is null) break;
                        {
                            var famResult = await _familyHandler.HandleFamilySummonAsync(reader, player, token);
                            foreach (var pkt in famResult.SelfPackets) await s.SendAsync(pkt, token);
                            if (famResult.Warp is { } summonWarp)
                            {
                                currentField = await WarpAsync(
                                    player, currentField, npcOidToId, s,
                                    summonWarp.DestinationMapId, sessionToken, token);
                            }
                        }
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
            if (player is not null)
            {
                var tradeResult = _tradeService.DeregisterPlayer(player, sessionToken);
                try
                {
                    // best-effort：通知交易對手取消。對「對手 session」送包可能拋（對手同時斷線），
                    // 但絕不可因此跳過下方自身的 deregister／持久化清理（否則背包/角色不落地＋registry 洩漏）。
                    await V113PlayerInteractionRouter.DispatchTradeNoticesAsync(
                        _tradeService,
                        tradeResult,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[Channel] 登出派送交易取消通知失敗 charId={CharId}", player.Character.Id);
                }

                _followService.CancelFollow(player);

                var deregisteredPlayer = _onlinePlayers.Deregister(player.Character.Id, sessionToken);
                if (deregisteredPlayer is not null)
                {
                    try
                    {
                        // best-effort：公會/好友登出通知會送往其他玩家 session，失敗不可阻斷後續持久化。
                        await _guildOperationHandler.OnPlayerLoggedOutAsync(player, CancellationToken.None);
                        await _buddyHandler.OnPlayerLoggedOutAsync(player, CancellationToken.None);
                        await _messengerHandler.NotifyDisconnectAsync(player, CancellationToken.None);
                        await _playerInteractionRouter.NotifyDisconnectAsync(player, CancellationToken.None);
                        await _familyHandler.NotifyDisconnectAsync(player, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "[Channel] 登出公會/好友通知失敗 charId={CharId}", player.Character.Id);
                    }
                }

                player.FlushInventory();

                if (account is not null)
                {
                    player.FlushStorage(account);
                    try
                    {
                        await _accounts.UpdateAsync(account, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "[Channel] Account {AccountId} storage flush failed", account.Id);
                    }
                }

                try
                {
                    await _charService.UpdateAsync(player.Character, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[Channel] Character {CharId} flush failed", player.Character.Id);
                }
            }

            if (currentField is not null && player is not null)
            {
                lock (currentField)
                {
                    currentField.Remove(player.ObjectId);
                }
            }

            // Cleanup: remove from map, notify others
            if (chr is not null)
            {
                _partySearchHandler.NotifyMapLeave(player!);
                await _partyOperationHandler.NotifyLogoutAsync(player!, CancellationToken.None);
                var removedFromMap = _mapRegistry.Deregister(chr.MapId, chr.Id, sessionToken);
                if (removedFromMap)
                {
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

    private FieldInstance EnterField(int mapId, Player player)
    {
        var field = _fieldRegistry.GetOrCreate(mapId, out var created);
        lock (field)
        {
            if (created)
            {
                var spawned = _combatService.SpawnMapMonsters(field, mapId);
                _log.LogInformation("[Channel] 地圖 {Map} 初始化 {Count} 隻怪物", mapId, spawned.Count);

                var reactors = _reactorService.SpawnMapReactors(field, mapId);
                _log.LogInformation("[Channel] 地圖 {Map} 初始化 {Count} 個 reactor", mapId, reactors.Count);
            }

            field.Add(player);
        }

        return field;
    }

    private async Task<byte[]> BuildSpawnPlayerPacketAsync(
        Character chr,
        short x,
        short y,
        byte stance,
        short foothold,
        CancellationToken ct)
    {
        V113SpawnGuildInfo? guildInfo = null;
        if (chr.GuildId > 0)
        {
            var guild = await _guildService.GetGuildAsync(chr.GuildId, ct);
            if (guild is not null)
            {
                guildInfo = new V113SpawnGuildInfo(
                    guild.Name,
                    guild.Emblem.LogoBackground,
                    guild.Emblem.LogoBackgroundColor,
                    guild.Emblem.Logo,
                    guild.Emblem.LogoColor);
            }
        }

        return V113MapPackets.SpawnPlayer(chr, x, y, stance, foothold, guildInfo);
    }

    private async Task SendFieldMonstersAsync(FieldInstance field, MapleSession session, CancellationToken ct)
    {
        List<Mob> mobs;
        lock (field)
        {
            mobs = field.Objects.OfType<Mob>().Where(static m => m.IsAlive).ToList();
        }

        foreach (var mob in mobs)
        {
            await session.SendAsync(V113CombatPackets.SpawnMonster(mob), ct);
            await session.SendAsync(V113CombatPackets.SpawnMonsterControl(mob, newSpawn: true, aggro: false), ct);
        }

        if (mobs.Count > 0)
        {
            _log.LogInformation("[Channel] 地圖 {Map} replay {Count} 隻怪物", field.MapId, mobs.Count);
        }
    }

    private async Task SendFieldDropsAsync(FieldInstance field, MapleSession session, CancellationToken ct)
    {
        List<MapDrop> drops;
        lock (field)
        {
            drops = field.Objects.OfType<MapDrop>().Where(static d => !d.IsPickedUp).ToList();
        }

        foreach (var drop in drops)
        {
            await session.SendAsync(V113DropPackets.DropItemFromMapObject(drop, mode: 2), ct);
        }

        if (drops.Count > 0)
        {
            _log.LogInformation("[Channel] 地圖 {Map} replay {Count} 個掉落物", field.MapId, drops.Count);
        }
    }

    private async Task SendFieldHiredMerchantsAsync(int mapId, MapleSession session, CancellationToken ct)
    {
        var packets = await _hiredMerchantHandler.SpawnOpenMerchantPacketsAsync(
            (byte)(_options.ChannelIndex + 1),
            mapId,
            new Position(0, 0, 0, 0),
            ct);

        foreach (var packet in packets)
        {
            await session.SendAsync(packet, ct);
        }

        if (packets.Count > 0)
        {
            _log.LogInformation("[Channel] 地圖 {Map} replay {Count} 個 hired merchant", mapId, packets.Count);
        }
    }

    /// <summary>NPC 地圖物件 id 起始值（避開玩家以 charId 充當的小號 objectId）。</summary>
    private const int NpcObjectIdBase = 1000;

    /// <summary>WZ portal 無目標地圖的哨兵值（對照 MapService.LoadPortals 的 tm 預設）。</summary>
    private const int NoTargetMapId = 999999999;

    private static bool IsMapleLand(int mapId) => mapId < 1010004;

    // ── NPC 對話（路線圖②）─────────────────────────────────────────────────────

    /// <summary>
    /// c2s NPC_TALK：[int oid] → 反查 npcId → 建腳本對話、跑 start()、flush 第一則對話。
    /// sink/warp 為語意化委派（編碼鎖本層；warp 重用進場序列）。回傳仍 active 的對話、否則 null。
    /// </summary>
    private async Task<NpcConversation?> StartNpcConversationAsync(
        PacketReader reader,
        Player player,
        Dictionary<int, int> oidToNpcId,
        MapleSession session,
        Func<int, CancellationToken, Task> openShop,
        Func<int, CancellationToken, Task> openStorage,
        Func<int, CancellationToken, Task> warp,
        CancellationToken ct)
    {
        var oid = reader.ReadInt();
        if (!oidToNpcId.TryGetValue(oid, out var npcId))
        {
            _log.LogDebug("[Channel] NPC_TALK 未知 oid={Oid}", oid);
            return null;
        }

        var ctx = new NpcContext(npcId, player, _questService);
        var script = _npcScripts.TryCreate(npcId, ctx);
        if (script is null)
        {
            _log.LogDebug("[Channel] NPC {Npc} 無對應腳本，略過", npcId);
            return null;
        }

        var convo = new NpcConversation(
            npcId, script, ctx,
            sendDialog: (dlg, c) => session.SendAsync(V113NpcDialogEncoder.Encode(dlg), c),
            warp: warp,
            openShop: openShop,
            openStorage: openStorage,
            sendQuestResult: (result, c) => SendQuestTransactionResultAsync(result, session, c),
            sendInfoQuestUpdate: (questId, data, c) => session.SendAsync(V113QuestPackets.UpdateInfoQuest(questId, data), c));

        await convo.StartAsync(ct);
        _log.LogInformation("[Channel] NPC {Npc} 對話開始", npcId);
        return convo.Active ? convo : null;
    }

    private async Task<NpcConversation?> StartNpcConversationByNpcIdAsync(
        int npcId,
        Player player,
        MapleSession session,
        Func<int, CancellationToken, Task> openShop,
        Func<int, CancellationToken, Task> openStorage,
        Func<int, CancellationToken, Task> warp,
        CancellationToken ct)
    {
        var ctx = new NpcContext(npcId, player, _questService);
        var script = _npcScripts.TryCreate(npcId, ctx);
        if (script is null)
        {
            _log.LogDebug("[Channel] NPC {Npc} 無對應腳本，略過", npcId);
            return null;
        }

        var convo = new NpcConversation(
            npcId, script, ctx,
            sendDialog: (dlg, c) => session.SendAsync(V113NpcDialogEncoder.Encode(dlg), c),
            warp: warp,
            openShop: openShop,
            openStorage: openStorage,
            sendQuestResult: (result, c) => SendQuestTransactionResultAsync(result, session, c),
            sendInfoQuestUpdate: (questId, data, c) => session.SendAsync(V113QuestPackets.UpdateInfoQuest(questId, data), c));

        await convo.StartAsync(ct);
        _log.LogInformation("[Channel] NPC {Npc} 對話開始", npcId);
        return convo.Active ? convo : null;
    }

    private static async Task SendPlayerEventResultAsync(
        V113PlayerEventResult result,
        MapleSession session,
        CancellationToken ct)
    {
        if (!result.Handled)
        {
            return;
        }

        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }
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
    /// cm.warp 的落地：換地圖（MVP 重用進場序列——deregister 舊圖 → 設 MapId → SET_FIELD → register 新圖 → spawn NPC/monster）。
    /// proper 的輕量 WarpToMap 封包 + login/warp 共用 IMapTransition 用例待後續重構。
    /// </summary>
    private async Task<FieldInstance> WarpAsync(
        Player player,
        FieldInstance? currentField,
        Dictionary<int, int> oidToNpcId,
        MapleSession session,
        int mapId,
        object sessionToken,
        CancellationToken ct,
        int spawnPortalId = 0)
    {
        var chr = player.Character;
        var oldMapId = chr.MapId;
        var removedFromOldMap = _mapRegistry.Deregister(oldMapId, chr.Id, sessionToken);
        _partySearchHandler.NotifyMapLeave(player);

        if (currentField is not null)
        {
            lock (currentField)
            {
                currentField.Remove(player.ObjectId);
            }
        }

        if (removedFromOldMap)
        {
            var removePacket = V113MapPackets.RemovePlayer(chr.Id);
            var oldOthers = _mapRegistry.GetOthers(oldMapId, chr.Id);
            foreach (var other in oldOthers)
            {
                try { await other.SendPacket(removePacket, ct); } catch { /* session 可能正在關 */ }
            }
        }

        chr.MapId = mapId;
        chr.SpawnPoint = (byte)spawnPortalId;   // 客戶端據 SET_FIELD 此 byte 把玩家放到目標 portal

        var setField = V113ChannelPackets.SetField(chr, _options.ChannelIndex);
        await session.SendAsync(setField, ct);

        var field = EnterField(mapId, player);
        _mapRegistry.Register(mapId, chr.Id, chr, (pkt, tkn) => session.SendAsync(pkt, tkn), sessionToken);
        await _partySearchHandler.NotifyMapEntryAsync(player, (pkt, tkn) => session.SendAsync(pkt, tkn), ct);
        await _partyOperationHandler.NotifyMapEntryAsync(player, _options.ChannelIndex, (pkt, tkn) => session.SendAsync(pkt, tkn), ct);
        await SpawnMapNpcsAsync(mapId, session, oidToNpcId, ct);
        await SendFieldHiredMerchantsAsync(mapId, session, ct);
        await SendFieldMonstersAsync(field, session, ct);
        await SendFieldDropsAsync(field, session, ct);
        await V113ReactorHandler.SendFieldReactorsAsync(field, session, ct);
        _log.LogInformation("[Channel] 角色 {Name} warp → 地圖 {Map}", chr.Name, mapId);
        return field;
    }

    // ── Quest / Stats / 背包 / 商店 / 倉庫 / 戰鬥 ─────────────────────────────

    private static async Task HandleStatsMutationAsync(
        PlayerStatsMutation mutation,
        MapleSession session,
        bool sendSkill,
        CancellationToken ct)
    {
        if (V113StatsHandlers.EncodeUpdateStats(mutation) is { } statsPacket)
        {
            await session.SendAsync(statsPacket, ct);
        }

        if (sendSkill && V113StatsHandlers.EncodeUpdateSkill(mutation) is { } skillPacket)
        {
            await session.SendAsync(skillPacket, ct);
        }
    }

    private async Task<FieldInstance?> HandleItemUseResultAsync(
        V113ItemUseResult result,
        Player player,
        FieldInstance? currentField,
        Dictionary<int, int> npcOidToId,
        MapleSession session,
        object sessionToken,
        CancellationToken ct)
    {
        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }

        foreach (var packet in result.BroadcastPackets)
        {
            await BroadcastPacketToOthersAsync(player.Character, packet, ct);
        }

        if (!result.Applied)
        {
            return currentField;
        }

        if (currentField is not null && result.SpawnMonsterIds.Count > 0)
        {
            List<Mob> spawned;
            lock (currentField)
            {
                spawned = _itemUseService.SpawnSummonBagMonsters(currentField, player, result.SpawnMonsterIds).ToList();
            }

            foreach (var mob in spawned)
            {
                await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.SpawnMonster(mob), ct);
                await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.SpawnMonsterControl(mob, newSpawn: true, aggro: false), ct);
            }
        }

        if (currentField is not null && result.RemoveMonsterObjectId is { } removeOid)
        {
            bool removed;
            lock (currentField)
            {
                removed = _itemUseService.RemoveCaughtMob(currentField, removeOid);
            }

            if (removed)
            {
                await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.KillMonster(removeOid), ct);
            }
        }

        if (result.InventoryMutations.Count > 0 || result.GainItems.Count > 0)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }

        if (result.WarpMapId is { } warpMapId)
        {
            return await WarpAsync(player, currentField, npcOidToId, session, warpMapId, sessionToken, ct);
        }

        return currentField;
    }

    private async Task HandleScrollResultAsync(
        V113ScrollHandleResult result,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        if (!result.Handled)
        {
            return;
        }

        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }

        if (result.BroadcastPacket is not null)
        {
            await BroadcastPacketToMapAsync(player.Character, session, result.BroadcastPacket, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }
    }

    private async Task HandleBuffItemResultAsync(
        V113BuffItemHandleResult result,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        if (!result.Handled)
        {
            return;
        }

        foreach (var packet in result.Packets)
        {
            await session.SendAsync(packet, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }
    }

    private async Task SendHiredMerchantResultAsync(
        V113HiredMerchantHandleResult result,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        if (!result.Handled)
        {
            return;
        }

        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }

        foreach (var packet in result.MapPackets)
        {
            await BroadcastPacketToMapAsync(player.Character, session, packet, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }
    }

    private async Task HandleRewardItemResultAsync(
        V113RewardItemResult result,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        if (!result.Handled)
        {
            return;
        }

        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }

        foreach (var packet in result.BroadcastPackets)
        {
            await BroadcastPacketToOthersAsync(player.Character, packet, ct);
        }

        foreach (var packet in result.ChannelBroadcastPackets)
        {
            await BroadcastPacketToAllOnlineAsync(packet, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
            _log.LogInformation(
                "[Channel] REWARD_ITEM/TREASURE_CHEST charId={CharId} slot={Slot} itemId={ItemId}",
                player.Character.Id,
                result.Request.Slot,
                result.Request.ItemId);
        }
    }

    private async Task HandleTransformPlayerAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        List<Player> mapPlayers;
        lock (field)
        {
            mapPlayers = field.Players.ToList();
        }

        var result = _buffItemHandler.HandleTransformPlayer(reader, player, mapPlayers);
        if (!result.Handled)
        {
            return;
        }

        foreach (var packet in result.SourcePackets)
        {
            await session.SendAsync(packet, ct);
        }

        if (result.Target is not null)
        {
            foreach (var packet in result.TargetPackets)
            {
                await SendPacketToRuntimePlayerAsync(player, session, result.Target, packet, ct);
            }

            foreach (var packet in result.BroadcastPackets)
            {
                await BroadcastPacketToOthersAsync(result.Target.Character, packet, ct);
            }
        }

        if (result.SourceCharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }
    }

    private async Task SendPacketToRuntimePlayerAsync(
        Player currentPlayer,
        MapleSession currentSession,
        Player target,
        byte[] packet,
        CancellationToken ct)
    {
        if (target.Character.Id == currentPlayer.Character.Id)
        {
            await currentSession.SendAsync(packet, ct);
            return;
        }

        var targetEntry = _mapRegistry
            .GetOthers(target.Character.MapId, currentPlayer.Character.Id)
            .FirstOrDefault(e => e.CharId == target.Character.Id);
        if (targetEntry is not null)
        {
            await targetEntry.SendPacket(packet, ct);
        }
    }

    private async Task<FieldInstance?> HandleOwlResultAsync(
        V113OwlHandleResult result,
        Player player,
        FieldInstance? currentField,
        Dictionary<int, int> oidToNpcId,
        MapleSession session,
        object sessionToken,
        CancellationToken ct)
    {
        foreach (var packet in result.Packets)
        {
            await session.SendAsync(packet, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }

        return result.WarpMapId is { } mapId
            ? await WarpAsync(player, currentField, oidToNpcId, session, mapId, sessionToken, ct)
            : currentField;
    }

    private async Task HandleGiveFameAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        V113GiveFameRequest request;
        try
        {
            request = V113FamePackets.ParseGiveFame(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113FamePackets.GiveFameError(FameResultStatus.InvalidMode), ct);
            return;
        }

        var result = _fameService.GiveFame(
            player,
            request.TargetCharacterId,
            request.Mode,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (result.Status != FameResultStatus.Success || result.Target is null)
        {
            await session.SendAsync(V113FamePackets.GiveFameError(result.Status), ct);
            return;
        }

        await session.SendAsync(
            V113FamePackets.GiveFameResponse(request.Mode, result.Target.Name, result.NewFame),
            ct);
        await result.Target.SendPacket(
            V113StatsPackets.UpdateStats(new[]
            {
                new PlayerStatUpdate(PlayerStatKind.Fame, result.NewFame),
            }),
            ct);
        await result.Target.SendPacket(V113FamePackets.ReceiveFame(request.Mode, player.Character.Name), ct);
    }

    private async Task HandleCharInfoRequestAsync(PacketReader reader, Player requester, MapleSession session, CancellationToken ct)
    {
        int targetCharacterId;
        try
        {
            targetCharacterId = V113CharacterInfoPackets.ParseCharInfoRequest(reader);
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "[Channel] CHAR_INFO_REQUEST packet invalid charId={CharId}", requester.Character.Id);
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        await session.SendAsync(V113StatsPackets.EnableActions(), ct);

        var target = FindCharacterInSameMap(requester, targetCharacterId);
        if (target is null)
        {
            return;
        }

        var social = await BuildCharacterInfoSocialAsync(target, ct);
        await session.SendAsync(V113CharacterInfoPackets.CharInfo(target, social), ct);
    }

    private async Task HandleUpdateCharInfoAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        V113CharacterInfoUpdate request;
        try
        {
            request = V113CharacterInfoPackets.ParseUpdateCharInfo(reader);
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "[Channel] UPDATE_CHAR_INFO packet invalid charId={CharId}", player.Character.Id);
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        switch (request.Kind)
        {
            case V113CharacterInfoUpdateKind.None:
                await session.SendAsync(V113StatsPackets.EnableActions(), ct);
                return;
            case V113CharacterInfoUpdateKind.CharacterMessage:
                player.UpdateCharacterMessage(request.Message);
                break;
            case V113CharacterInfoUpdateKind.Expression:
                player.UpdateProfileExpression(request.Expression);
                break;
            case V113CharacterInfoUpdateKind.Birthday:
                player.UpdateProfileBirthday(request.Blood, request.BirthMonth, request.BirthDay, request.Constellation);
                break;
            default:
                _log.LogDebug("[Channel] UPDATE_CHAR_INFO ignored unknown type={Type} charId={CharId}", request.RawKind, player.Character.Id);
                await session.SendAsync(V113StatsPackets.EnableActions(), ct);
                return;
        }

        await _charService.UpdateAsync(player.Character, ct);
    }

    private async Task HandleTrockAddMapAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        V113TrockAddMapRequest request;
        try
        {
            request = V113TrockPackets.ParseAddMap(reader);
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "[Channel] TROCK_ADD_MAP packet invalid charId={CharId}", player.Character.Id);
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var mapId = player.Character.MapId;
        var changed = false;
        if (request.IsVip)
        {
            if (request.IsDelete)
            {
                changed = player.RemoveVipRock(request.MapId);
            }
            else if (request.IsAdd && mapId != 180000000)
            {
                changed = player.AddVipRock(mapId);
            }
        }
        else if (request.IsDelete)
        {
            changed = player.RemoveRegularRock(request.MapId);
        }
        else if (request.IsAdd && mapId <= 197010000 && mapId != 180000000)
        {
            changed = player.AddRegularRock(mapId);
        }

        if (changed)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }

        await session.SendAsync(V113TrockPackets.MapTransferResult(player.Character, request.Vip, request.IsDelete), ct);
    }

    private async Task<FieldInstance> HandleUseTeleRockAsync(
        PacketReader reader,
        Player player,
        FieldInstance currentField,
        Dictionary<int, int> npcOidToId,
        MapleSession session,
        object sessionToken,
        CancellationToken ct)
    {
        var result = V113TeleRockHandler.HandleUseTeleRock(reader, _mapService);
        foreach (var packet in result.Packets)
        {
            await session.SendAsync(packet, ct);
        }

        if (result.Success && result.WarpMapId is { } warpMapId)
        {
            _log.LogInformation(
                "[Channel] USE_TELE_ROCK charId={CharId} rockType={RockType} mapId={MapId}",
                player.Character.Id,
                result.Request.RockType,
                warpMapId);
            return await WarpAsync(player, currentField, npcOidToId, session, warpMapId, sessionToken, ct);
        }

        _log.LogDebug(
            "[Channel] USE_TELE_ROCK rejected charId={CharId} rockType={RockType} mode={Mode} mapId={MapId} name={Name}",
            player.Character.Id,
            result.Request.RockType,
            result.Request.Mode,
            result.Request.MapId,
            result.Request.CharacterName);
        return currentField;
    }

    private async Task HandleItemUnlockAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        var result = V113ItemUnlockHandler.Handle(reader, player);
        foreach (var packet in result.Packets)
        {
            await session.SendAsync(packet, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
            _log.LogInformation(
                "[Channel] ITEM_UNLOCK charId={CharId} slot={Slot}",
                player.Character.Id,
                result.Request.Slot);
        }
    }

    private async Task HandleScriptedNpcItemAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        var result = V113ScriptedNpcItemHandler.Handle(reader, player);
        foreach (var packet in result.Packets)
        {
            await session.SendAsync(packet, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
            _log.LogInformation(
                "[Channel] USE_SCRIPTED_NPC_ITEM consumed charId={CharId} slot={Slot} itemId={ItemId}",
                player.Character.Id,
                result.Request.Slot,
                result.Request.ItemId);
        }
    }

    private Task HandleMobNodeAsync(
        PacketReader reader,
        Player player,
        FieldInstance field)
    {
        try
        {
            var result = V113MobNodeHandler.Handle(reader, field);
            if (result.MobFound)
            {
                _log.LogInformation(
                    "[Channel] MOB_NODE charId={CharId} mobOid={MobOid} nodeIndex={NodeIndex}",
                    player.Character.Id,
                    result.Request.MobObjectId,
                    result.Request.NodeIndex);
            }
            else
            {
                _log.LogDebug(
                    "[Channel] MOB_NODE ignored missing mob charId={CharId} mobOid={MobOid} nodeIndex={NodeIndex}",
                    player.Character.Id,
                    result.Request.MobObjectId,
                    result.Request.NodeIndex);
            }
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "[Channel] MOB_NODE packet invalid charId={CharId}", player.Character.Id);
        }

        return Task.CompletedTask;
    }

    private async Task HandleMonsterBookCoverAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        int coverItemId;
        try
        {
            coverItemId = V113MonsterBookPackets.ParseChangeCover(reader);
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "[Channel] MONSTER_BOOK_COVER packet invalid charId={CharId}", player.Character.Id);
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (!V113MonsterBookPackets.IsMonsterCardOrClear(coverItemId))
        {
            return;
        }

        player.ChangeMonsterBookCover(coverItemId);
        await _charService.UpdateAsync(player.Character, ct);
        await session.SendAsync(V113MonsterBookPackets.ChangeCover(coverItemId), ct);
    }

    private async Task HandleChangeKeymapAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        V113ChangeKeymapRequest request;
        try
        {
            request = V113KeymapPackets.ParseChangeKeymap(reader);
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "[Channel] CHANGE_KEYMAP packet invalid charId={CharId}", player.Character.Id);
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (request.IsPetAutoPot)
        {
            if (player.UpdatePetAutoPot(request.PetAutoPotType, request.PetAutoPotItemId))
            {
                await _charService.UpdateAsync(player.Character, ct);
            }

            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        foreach (var change in request.Changes)
        {
            player.ChangeKeyBinding(change.Key, change.Type, change.Action);
        }

        await _charService.UpdateAsync(player.Character, ct);
        _log.LogDebug("[Channel] CHANGE_KEYMAP charId={CharId} tick={Tick} changes={Count}",
            player.Character.Id,
            request.Tick,
            request.Changes.Count);
    }

    private async Task HandleSkillMacroAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        IReadOnlyList<V113SkillMacroChange> changes;
        try
        {
            changes = V113SkillMacroPackets.ParseChangeSkillMacro(reader);
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "[Channel] SKILL_MACRO packet invalid charId={CharId}", player.Character.Id);
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        foreach (var change in changes)
        {
            player.UpdateSkillMacro(
                change.Position,
                change.Name,
                change.Shout,
                change.Skill1,
                change.Skill2,
                change.Skill3);
        }

        await _charService.UpdateAsync(player.Character, ct);
        _log.LogDebug("[Channel] SKILL_MACRO charId={CharId} changes={Count}",
            player.Character.Id,
            changes.Count);
    }

    private Character? FindCharacterInSameMap(Player requester, int targetCharacterId)
    {
        if (requester.Character.Id == targetCharacterId)
        {
            return requester.Character;
        }

        return _mapRegistry
            .GetOthers(requester.Character.MapId, requester.Character.Id)
            .FirstOrDefault(e => e.CharId == targetCharacterId)
            ?.Character;
    }

    private async Task<V113CharacterInfoSocial> BuildCharacterInfoSocialAsync(Character character, CancellationToken ct)
    {
        if (character.GuildId <= 0)
        {
            return V113CharacterInfoSocial.Empty;
        }

        var guild = await _guildService.GetGuildAsync(character.GuildId, ct);
        return guild is null
            ? V113CharacterInfoSocial.Empty
            : new V113CharacterInfoSocial(guild.Name, string.Empty);
    }

    private async Task HandleQuestActionAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        QuestClientAction action;
        try
        {
            action = V113QuestPackets.ParseQuestAction(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = _questService.HandleClientAction(player, action);
        await SendQuestTransactionResultAsync(result, session, ct);
    }

    private async Task HandleUpdateQuestAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        int questId;
        try
        {
            questId = V113QuestPackets.ParseUpdateQuest(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var result = _questService.UpdateQuest(player, questId);
        await SendQuestTransactionResultAsync(result, session, ct);
    }

    private static async Task SendQuestTransactionResultAsync(
        QuestTransactionResult result,
        MapleSession session,
        CancellationToken ct)
    {
        if (result.Quest is { } quest)
        {
            await session.SendAsync(V113QuestPackets.UpdateQuest(quest), ct);
        }

        foreach (var item in result.GainedItems)
        {
            await session.SendAsync(
                V113QuestPackets.ModifyInventoryAdd(Player.InventoryTypeOf(item.ItemId), item),
                ct);
        }

        foreach (var mutation in result.InventoryMutations)
        {
            await session.SendAsync(V113QuestPackets.ModifyInventoryQuantity(mutation), ct);
        }

        if (result.MesoChanged)
        {
            await session.SendAsync(V113QuestPackets.UpdateMeso(result.Meso), ct);
        }

        if (result.ShowQuestCompletionId is { } completedQuestId)
        {
            await session.SendAsync(V113QuestPackets.ShowQuestCompletion(completedQuestId), ct);
        }

        if (result is { Quest: { } completed, NextQuestId: { } nextQuestId })
        {
            await session.SendAsync(
                V113QuestPackets.UpdateQuestFinish(completed.QuestId, completed.Npc, nextQuestId),
                ct);
        }
    }

    private async Task HandleItemMoveAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        var result = V113InventoryMoveHandler.ApplyItemMove(reader, player);
        if (result.Packet is not null)
        {
            await session.SendAsync(result.Packet, ct);
        }

        if (result.Success)
        {
            _log.LogDebug("[Channel] ITEM_MOVE {Operation} type={Type} src={Src} dst={Dst}",
                result.Operation,
                result.Request.Type,
                result.Request.Src,
                result.Request.Dst);
        }
    }

    private async Task HandleItemArrangeAsync(
        PacketReader reader,
        Player player,
        MapleSession session,
        bool isSort,
        CancellationToken ct)
    {
        V113InventoryArrangeRequest request;
        try
        {
            request = V113InventoryPackets.ParseArrange(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (!request.IsValidBagType)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var changed = isSort
            ? player.SortInventory(request.Type)
            : player.GatherInventory(request.Type);
        if (changed)
        {
            player.FlushInventory();
            await _charService.UpdateAsync(player.Character, ct);
        }

        var packet = isSort
            ? V113InventoryPackets.FinishedSort(request.RawType)
            : V113InventoryPackets.FinishedGather(request.RawType);
        await session.SendAsync(packet, ct);
        await session.SendAsync(V113StatsPackets.EnableActions(), ct);
    }

    private async Task HandleSpecialMoveAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        V113SkillHandleResult handled;
        try
        {
            handled = V113SkillMoveHandler.HandleSpecialMove(reader, player, _skillService, DateTimeOffset.UtcNow);
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (handled.Packet is not null)
        {
            await session.SendAsync(handled.Packet, ct);
        }

        if (handled.Cast is { Status: not SkillCastStatus.Success } cast)
        {
            _log.LogDebug("[Channel] SPECIAL_MOVE ignored skill={SkillId} status={Status}", cast.SkillId, cast.Status);
        }
    }

    private async Task HandleCancelBuffAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        V113SkillHandleResult handled;
        try
        {
            handled = V113SkillMoveHandler.HandleCancelBuff(reader, player, _skillService);
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (handled.Packet is not null)
        {
            await session.SendAsync(handled.Packet, ct);
        }

        if (handled.Cancel?.Status == CancelBuffStatus.ChargeSkill)
        {
            _log.LogDebug("[Channel] CANCEL_BUFF charge skill cancel broadcast not wired source={SourceId}", handled.SourceId);
        }
    }

    private async Task HandleItemPickupAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        V113ItemPickupRequest req;
        try
        {
            req = V113DropPackets.ParseItemPickup(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        DropPickupResult result;
        lock (field)
        {
            result = _dropService.TryPickup(field, player, req.ObjectId);
        }

        if (!result.Success || result.Drop is null)
        {
            return;
        }

        await BroadcastPacketToMapAsync(
            player.Character,
            session,
            V113DropPackets.RemoveItemFromMap(result.Drop.ObjectId, animation: 2, characterId: player.Character.Id),
            ct);

        if (result.GainedItem is not null && result.InventoryType is not null)
        {
            await session.SendAsync(V113DropPackets.ModifyInventoryAdd(result.InventoryType.Value, result.GainedItem), ct);
            await session.SendAsync(V113DropPackets.ShowItemGain(result.GainedItem.ItemId, result.GainedItem.Quantity), ct);
        }
        else if (result.GainedMeso > 0)
        {
            await session.SendAsync(V113DropPackets.UpdateMeso(player.Character.Meso), ct);
            await session.SendAsync(V113DropPackets.ShowMesoGain(result.GainedMeso), ct);
        }
    }

    private async Task HandleMesoDropAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        V113MesoDropRequest request;
        try
        {
            request = V113DropPackets.ParseMesoDrop(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        MesoDropResult result;
        lock (field)
        {
            result = _dropService.TryDropMeso(field, player, request.Meso);
        }

        if (!result.Success || result.Drop is null)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        await session.SendAsync(V113DropPackets.UpdateMeso(player.Character.Meso), ct);
        await BroadcastPacketToMapAsync(
            player.Character,
            session,
            V113DropPackets.DropItemFromMapObject(result.Drop),
            ct);
    }

    private async Task HandleNpcShopAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        var req = V113ShopPackets.ParseNpcShop(reader);
        switch (req.Action)
        {
            case V113NpcShopAction.Buy:
            {
                var result = _shopService.Buy(player, req.ItemId, req.Quantity);
                if (result.Status == ShopTransactionStatus.Success && result.GainedItem is not null)
                {
                    await session.SendAsync(V113ShopPackets.ModifyInventoryAdd(Player.InventoryTypeOf(req.ItemId), result.GainedItem), ct);
                    await session.SendAsync(V113ShopPackets.UpdateMeso(result.Meso), ct);
                    await session.SendAsync(V113ShopPackets.ConfirmShopTransaction(V113ShopPackets.ConfirmBuy), ct);
                }
                else
                {
                    await session.SendAsync(V113ShopPackets.ConfirmShopTransaction(V113ShopPackets.ConfirmError), ct);
                }
                break;
            }

            case V113NpcShopAction.Sell:
            {
                var result = _shopService.Sell(player, req.Slot, req.ItemId, req.Quantity);
                if (result.Status == ShopTransactionStatus.Success && result.Mutation is not null)
                {
                    await session.SendAsync(V113ShopPackets.ModifyInventoryQuantity(result.Mutation), ct);
                    await session.SendAsync(V113ShopPackets.UpdateMeso(result.Meso), ct);
                    await session.SendAsync(V113ShopPackets.ConfirmShopTransaction(V113ShopPackets.ConfirmSell), ct);
                }
                else
                {
                    await session.SendAsync(V113ShopPackets.ConfirmShopTransaction(V113ShopPackets.ConfirmError), ct);
                }
                break;
            }

            case V113NpcShopAction.Recharge:
            default:
                await session.SendAsync(V113ShopPackets.ConfirmShopTransaction(V113ShopPackets.ConfirmError), ct);
                break;
        }
    }

    private async Task<bool> HandleStorageAsync(
        PacketReader reader,
        Player player,
        int npcId,
        Account? account,
        MapleSession session,
        CancellationToken ct)
    {
        var req = V113StoragePackets.Parse(reader);
        if (!req.HasValidType)
        {
            return false;
        }

        var result = req.Mode switch
        {
            StorageClientMode.TakeOut => _storageService.TakeOut(player, req.Type, req.StorageSlot),
            StorageClientMode.Store => _storageService.Store(player, req.Type, req.InventorySlot, req.Quantity, req.ItemId),
            StorageClientMode.Arrange => _storageService.Arrange(player),
            StorageClientMode.Meso => _storageService.MoveMeso(player, req.Meso),
            StorageClientMode.Close => _storageService.Close(player),
            _ => StorageResult.None,
        };

        if (result.Kind == StorageResultKind.Closed && account is not null)
        {
            player.FlushStorage(account);
            await _accounts.UpdateAsync(account, ct);
        }

        var packet = V113StoragePackets.EncodeResult(result, npcId, player.Storage);
        if (packet is not null)
        {
            await session.SendAsync(packet, ct);
        }

        return result.Kind == StorageResultKind.Closed;
    }

    private async Task HandleCloseRangeAttackAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        V113CloseRangeAttack attack;
        try
        {
            attack = V113CombatPackets.ParseCloseRangeAttack(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        var attackBroadcast = V113CombatPackets.CloseRangeAttackBroadcast(player.Character.Id, attack, player.Character.Level);
        await BroadcastPacketToOthersAsync(player.Character, attackBroadcast, ct);

        CombatAttackResult result;
        lock (field)
        {
            result = _combatService.ApplyAttack(field, player, attack.ToCombatAttack());
        }

        await SendCombatHitsAsync(result.Hits, player, session, ct);
    }

    private async Task HandleRangedAttackAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        V113RangedAttack attack;
        try
        {
            attack = V113RangedMagicAttackPackets.ParseRangedAttack(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        RangedAttackApplyResult result;
        lock (field)
        {
            result = _rangedMagicCombatService.ApplyRangedAttack(
                field,
                player,
                attack.ToCombatRangedAttack(),
                GetRangedConsumables(player));
        }

        if (!result.Applied)
        {
            _log.LogDebug("[Channel] RANGED_ATTACK ignored status={Status}", result.Status);
            return;
        }

        foreach (var mutation in result.InventoryMutations)
        {
            await session.SendAsync(V113RangedMagicAttackPackets.ModifyInventoryQuantity(mutation), ct);
        }

        var skillLevel = GetSkillLevelByte(player, attack.SkillId);
        var attackBroadcast = V113RangedMagicAttackPackets.RangedAttackBroadcast(
            player.Character.Id,
            attack,
            player.Character.Level,
            result.VisualProjectileItemId,
            skillLevel);
        await BroadcastPacketToOthersAsync(player.Character, attackBroadcast, ct);

        await SendCombatHitsAsync(result.Combat.Hits, player, session, ct);
    }

    private async Task HandleMagicAttackAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        if (!player.IsAlive)
        {
            return;
        }

        V113MagicAttack attack;
        try
        {
            attack = V113RangedMagicAttackPackets.ParseMagicAttack(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        CombatAttackResult result;
        lock (field)
        {
            result = _rangedMagicCombatService.ApplyMagicAttack(field, player, attack.ToCombatAttack());
        }

        var attackBroadcast = V113RangedMagicAttackPackets.MagicAttackBroadcast(
            player.Character.Id,
            attack,
            player.Character.Level,
            GetSkillLevelByte(player, attack.SkillId));
        await BroadcastPacketToOthersAsync(player.Character, attackBroadcast, ct);

        await SendCombatHitsAsync(result.Hits, player, session, ct);
    }

    private async Task HandleTakeDamageAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        V113TakeDamageRequest request;
        try
        {
            request = V113PlayerDamagePackets.ParseTakeDamage(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (request.Damage < -1 || request.Damage > 60_000)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (!request.IsMapDamage)
        {
            lock (field)
            {
                if (field.Get(request.ObjectId) is not Mob)
                {
                    return;
                }
            }
        }

        var applied = request.Damage > 0 ? player.TakeDamage(request.Damage) : (short)0;
        if (applied > 0)
        {
            await session.SendAsync(
                V113StatsPackets.UpdateStats(new[]
                {
                    new PlayerStatUpdate(PlayerStatKind.Hp, player.Hp),
                }),
                ct);
        }

        await BroadcastPacketToOthersAsync(
            player.Character,
            V113PlayerDamagePackets.DamagePlayer(
                player.Character.Id,
                request.Type,
                request.Damage,
                request.MonsterIdFrom,
                request.Direction),
            ct);
    }

    private async Task SendCombatHitsAsync(
        IReadOnlyList<CombatMobHit> hits,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        foreach (var hit in hits)
        {
            if (hit.AppliedDamage > 0)
            {
                await BroadcastPacketToMapAsync(
                    player.Character,
                    session,
                    V113CombatPackets.DamageMonster(hit.ObjectId, hit.AppliedDamage),
                    ct);
            }

            if (hit.Killed)
            {
                await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.KillMonster(hit.ObjectId), ct);

                if (hit.Rewards is { } rewards)
                {
                    await SendMobKillRewardsAsync(player, session, rewards, ct);
                }
            }
        }
    }

    private async Task SendMobKillRewardsAsync(
        Player player,
        MapleSession session,
        MobKillRewards rewards,
        CancellationToken ct)
    {
        if (rewards.StatsMutation is { } mutation)
        {
            if (V113StatsHandlers.EncodeUpdateStats(mutation) is { } statsPacket)
            {
                await session.SendAsync(statsPacket, ct);
            }
        }
        else if (rewards.ExpGained > 0)
        {
            await session.SendAsync(V113DropPackets.UpdateExp(player.Character.Exp), ct);
        }

        if (rewards.ExpGained > 0)
        {
            await session.SendAsync(V113DropPackets.ShowExpGainMonster(rewards.ExpGained), ct);
        }

        foreach (var drop in rewards.SpawnedDrops)
        {
            await BroadcastPacketToMapAsync(
                player.Character,
                session,
                V113DropPackets.DropItemFromMapObject(drop, mode: 1),
                ct);
        }
    }

    private static byte GetSkillLevelByte(Player player, int skillId)
        => skillId > 0 ? (byte)Math.Clamp(player.GetSkillLevel(skillId), byte.MinValue, byte.MaxValue) : (byte)0;

    private static RangedAttackConsumableOptions GetRangedConsumables(Player player)
    {
        var active = player.ActiveBuffs.Select(static b => b.Stat).ToHashSet();
        return new RangedAttackConsumableOptions(
            HasShadowPartner: active.Contains(MapleBuffStat.SHADOWPARTNER),
            HasSoulArrow: active.Contains(MapleBuffStat.SOULARROW),
            HasSpiritClaw: active.Contains(MapleBuffStat.SPIRIT_CLAW));
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private async Task SendNormalChannelEntryPacketsAsync(Character chr, MapleSession session, CancellationToken ct)
    {
        var setField = V113ChannelPackets.SetField(chr, _options.ChannelIndex);
        await session.SendAsync(setField, ct);
        if (V113SkillMacroPackets.SkillMacros(chr) is { } macros)
        {
            await session.SendAsync(macros, ct);
        }
        await session.SendAsync(V113KeymapPackets.Keymap(chr), ct);
        _log.LogInformation("[Channel] 角色 {Name} SET_FIELD 送出 → 地圖 {Map}", chr.Name, chr.MapId);
    }

    private static async Task SendCashShopInitialPacketsAsync(Player player, Account? account, MapleSession session, CancellationToken ct)
    {
        if (account is null)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        foreach (var packet in V113CashShopPackets.InitialCashShopPackets(
            player.Character,
            account,
            player.Inventory.By(Core.Inventory.InventoryType.Cash).Items,
            account.Storage.Slots,
            characterSlots: 3))
        {
            await session.SendAsync(packet, ct);
        }
    }

    private async Task HandleCashShopModePacketAsync(
        short opcode,
        PacketReader reader,
        Player? player,
        Account? account,
        MapleSession session,
        CancellationToken ct)
    {
        if (player is null)
        {
            return;
        }

        switch (opcode)
        {
            case V113ChannelRecvOp.ChangeMap:
                await LeaveCashShopAsync(player, account, session, ct);
                throw new OperationCanceledException("Leave cash shop reconnect requested.");

            case V113ChannelRecvOp.CsUpdate:
                await SendCashShopInitialPacketsAsync(player, account, session, ct);
                break;

            case V113ChannelRecvOp.CashShopOperation:
                if (account is null) break;
                var cashShopResult = _cashShopOperationHandler.Handle(reader, account, player);
                if (!cashShopResult.Handled) break;

                foreach (var packet in cashShopResult.Packets)
                {
                    await session.SendAsync(packet, ct);
                }

                if (cashShopResult.AccountMutated)
                {
                    await _accounts.UpdateAsync(account, ct);
                }

                if (cashShopResult.CharacterMutated)
                {
                    await _charService.UpdateAsync(player.Character, ct);
                }
                break;

            case V113ChannelRecvOp.CouponCode:
                if (account is null) break;
                var couponResult = await _cashShopOperationHandler.HandleCouponCodeAsync(
                    reader,
                    account,
                    player,
                    DateTimeOffset.UtcNow,
                    ct);
                foreach (var packet in couponResult.Packets)
                {
                    await session.SendAsync(packet, ct);
                }
                if (couponResult.AccountMutated)
                {
                    await _accounts.UpdateAsync(account, ct);
                }
                if (couponResult.CharacterMutated)
                {
                    await _charService.UpdateAsync(player.Character, ct);
                }
                break;

            case V113ChannelRecvOp.Pong:
                break;

            default:
                _log.LogDebug("[Channel] CashShop mode ignored opcode=0x{Op:X2}", opcode);
                await session.SendAsync(V113StatsPackets.EnableActions(), ct);
                break;
        }
    }

    private async Task LeaveCashShopAsync(Player player, Account? account, MapleSession session, CancellationToken ct)
    {
        player.FlushInventory();
        if (account is not null)
        {
            player.FlushStorage(account);
            await _accounts.UpdateAsync(account, ct);
        }

        await _charService.UpdateAsync(player.Character, ct);

        var channelIp = _options.ChannelIp ?? new byte[] { 127, 0, 0, 1 };
        await session.SendAsync(V113ChannelChangePackets.ChangeChannel(channelIp, (short)_options.ChannelPort), ct);
        _log.LogInformation(
            "[Channel] {Name} leave CashShop → {Ip}:{Port}",
            player.Character.Name,
            string.Join(".", channelIp),
            _options.ChannelPort);
    }

    private async Task RemoveFromCurrentFieldForTransitionAsync(
        Player player,
        FieldInstance? currentField,
        object sessionToken,
        CancellationToken ct)
    {
        if (currentField is not null)
        {
            lock (currentField)
            {
                currentField.Remove(player.ObjectId);
            }
        }

        _partySearchHandler.NotifyMapLeave(player);

        var mapId = player.Character.MapId;
        if (!_mapRegistry.Deregister(mapId, player.Character.Id, sessionToken))
        {
            return;
        }

        var removePacket = V113MapPackets.RemovePlayer(player.Character.Id);
        var others = _mapRegistry.GetOthers(mapId, player.Character.Id);
        foreach (var other in others)
        {
            try
            {
                await other.SendPacket(removePacket, ct);
            }
            catch
            {
                // Peer sessions may be closing; transition cleanup must continue.
            }
        }
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
    /// c2s MOVE_LIFE (0xB6)：怪物移動。解析 → 更新 mob 位置 → 回 response 給控制者 → 廣播給其他人。
    /// 對照 Java MobHandler.MoveMonster。
    /// </summary>
    private async Task HandleMoveLifeAsync(PacketReader reader, Player player, FieldInstance field, MapleSession session, CancellationToken ct)
    {
        V113MoveLifeData data;
        try
        {
            data = V113MobMovementPackets.ParseMoveLife(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        Mob? mob;
        lock (field)
        {
            mob = field.GetMob(data.ObjectId);
        }

        if (mob is null || !mob.IsAlive) return;

        // 嘗試從 raw movement 解析最終位置來更新 mob；失敗則 fallback 到 startPos
        try
        {
            if (data.RawMovement.Length > 0)
            {
                var moveResult = V113MovementParser.Parse(new PacketReader(data.RawMovement));
                if (moveResult.Commands > 0)
                {
                    mob.MoveTo(new Position(moveResult.X, moveResult.Y, moveResult.Stance, moveResult.Foothold));
                }
                else
                {
                    mob.MoveTo(new Position(data.StartX, data.StartY, mob.Position.Stance, mob.Position.Foothold));
                }
            }
            else
            {
                mob.MoveTo(new Position(data.StartX, data.StartY, mob.Position.Stance, mob.Position.Foothold));
            }
        }
        catch (InvalidDataException)
        {
            mob.MoveTo(new Position(data.StartX, data.StartY, mob.Position.Stance, mob.Position.Foothold));
        }

        // Response 給控制者
        await session.SendAsync(
            V113MobMovementPackets.MoveMonsterResponse(data.ObjectId, data.MoveId, mob.Mp, aggro: false),
            ct);

        // 廣播移動給同圖其他玩家
        var broadcast = V113MobMovementPackets.BroadcastMoveMonster(
            data.ObjectId, data.UseSkill, data.SkillIndex, data.SkillData,
            data.StartX, data.StartY, data.RawMovement);
        await BroadcastPacketToOthersAsync(player.Character, broadcast, ct);
    }

    /// <summary>
    /// c2s AUTO_AGGRO (0xB7)：怪物自動仇恨。設定控制者並送 SpawnMonsterControl。
    /// 對照 Java MobHandler.AutoAggro。
    /// </summary>
    private async Task HandleAutoAggroAsync(PacketReader reader, Player player, FieldInstance field, MapleSession session, CancellationToken ct)
    {
        int objectId;
        try
        {
            objectId = reader.ReadInt();
        }
        catch (InvalidDataException)
        {
            return;
        }

        Mob? mob;
        lock (field)
        {
            mob = field.GetMob(objectId);
        }

        if (mob is null || !mob.IsAlive) return;

        mob.ControllerId = player.Character.Id;

        await session.SendAsync(V113CombatPackets.SpawnMonsterControl(mob, newSpawn: false, aggro: true), ct);
    }

    private async Task HandleUseInnerPortalAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        V113InnerPortalRequest request;
        try
        {
            request = V113InnerPortalPackets.ParseUseInnerPortal(reader);
        }
        catch (InvalidDataException)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        var map = _mapService.LoadMap(player.Character.MapId);
        var portal = map.Portals.FirstOrDefault(p => p.Name == request.PortalName);
        if (portal is null)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        player.MoveTo(new Position(request.X, request.Y, player.Position.Stance, player.Position.Foothold));
        await session.SendAsync(V113InnerPortalPackets.CurrentMapWarp((byte)portal.Id), ct);
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
        await BroadcastPacketToOthersAsync(player.Character, packet, ct);
        _log.LogInformation("[Channel] 角色 {Name} 地圖聊天「{Text}」", player.Character.Name, text);
    }

    private async Task HandleCloseChalkboardAsync(Player player, MapleSession session, CancellationToken ct)
    {
        player.ClearChalkboard();
        await BroadcastPacketToMapAsync(
            player.Character,
            session,
            V113ChalkboardPackets.Chalkboard(player.Character.Id, null),
            ct);
        _log.LogDebug("[Channel] 角色 {Name} 關閉黑板", player.Character.Name);
    }

    /// <summary>
    /// 玩家表情（對照 Java PlayerHandler.ChangeEmotion）。
    /// c2s：[int emote]；s2c 廣播同地圖其他玩家 FACIAL_EXPRESSION。
    /// </summary>
    private async Task HandleFaceExpressionAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        int emote;
        try
        {
            emote = reader.ReadInt();
        }
        catch (InvalidDataException) { return; }

        if (emote <= 0) return;

        await BroadcastPacketToOthersAsync(
            player.Character,
            V113MapPackets.FacialExpression(player.Character.Id, emote),
            ct);
        _log.LogDebug("[Channel] 角色 {Name} 表情 {Emote}", player.Character.Name, emote);
    }

    /// <summary>
    /// 使用道具效果（對照 Java PlayerHandler.UseItemEffect 主幹）。
    /// c2s：[int itemId]；同地圖其他玩家收到 SHOW_ITEM_EFFECT。
    /// </summary>
    private async Task HandleUseItemEffectAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        int itemId;
        try
        {
            itemId = reader.ReadInt();
        }
        catch (InvalidDataException) { return; }

        if (itemId <= 0 || !player.HasItem(Player.InventoryTypeOf(itemId), itemId))
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (itemId != 5510000)
        {
            player.UseItemEffect(itemId);
        }

        await BroadcastPacketToOthersAsync(
            player.Character,
            V113MapPackets.ItemEffect(player.Character.Id, itemId),
            ct);
        _log.LogDebug("[Channel] 角色 {Name} 使用道具效果 {ItemId}", player.Character.Name, itemId);
    }

    /// <summary>
    /// 取消道具效果。Java 經 cancelEffect(getItemEffect(-id)) 間接取消；此處用 itemId=0 清除地圖外觀。
    /// </summary>
    private async Task HandleCancelItemEffectAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        try
        {
            reader.ReadInt();
        }
        catch (InvalidDataException) { return; }

        player.CancelItemEffect();
        await BroadcastPacketToOthersAsync(
            player.Character,
            V113MapPackets.ItemEffect(player.Character.Id, itemId: 0),
            ct);
        _log.LogDebug("[Channel] 角色 {Name} 取消道具效果", player.Character.Name);
    }

    /// <summary>
    /// 使用背包椅子（對照 Java PlayerHandler.UseChair 主幹）。
    /// c2s：[int itemId]；同地圖其他玩家收到 SHOW_CHAIR。
    /// </summary>
    private async Task HandleUseChairAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        int itemId;
        try
        {
            itemId = reader.ReadInt();
        }
        catch (InvalidDataException) { return; }

        if (itemId <= 0 || !player.HasItem(Player.InventoryTypeOf(itemId), itemId))
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        player.UseChair(itemId);
        await BroadcastPacketToOthersAsync(
            player.Character,
            V113MapPackets.ShowChair(player.Character.Id, itemId),
            ct);
        await session.SendAsync(V113StatsPackets.EnableActions(), ct);
        _log.LogDebug("[Channel] 角色 {Name} 使用椅子 {ItemId}", player.Character.Name, itemId);
    }

    /// <summary>
    /// 取消椅子（對照 Java PlayerHandler.CancelChair 主幹）。
    /// c2s：[short id]；id=-1 取消道具椅，否則使用地圖內建椅。
    /// </summary>
    private async Task HandleCancelChairAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        short id;
        try
        {
            id = reader.ReadShort();
        }
        catch (InvalidDataException) { return; }

        if (id == -1)
        {
            player.CancelChair();
            await session.SendAsync(V113MapPackets.CancelChair(-1), ct);
            await BroadcastPacketToOthersAsync(
                player.Character,
                V113MapPackets.ShowChair(player.Character.Id, itemId: 0),
                ct);
        }
        else
        {
            player.UseMapChair(id);
            await session.SendAsync(V113MapPackets.CancelChair(id), ct);
        }

        _log.LogDebug("[Channel] 角色 {Name} 取消/切換椅子 {ChairId}", player.Character.Name, id);
    }

    private async Task BroadcastToOthersAsync(Character chr, byte[] body, CancellationToken ct)
    {
        const int HeaderSkip = 2 + 33;
        if (body.Length <= HeaderSkip) return;

        var rawMovement = body.AsSpan(HeaderSkip);
        var broadcast = V113MapPackets.MovePlayerBroadcast(chr.Id, rawMovement);
        await BroadcastPacketToOthersAsync(chr, broadcast, ct);
    }

    private async Task BroadcastPacketToOthersAsync(Character chr, byte[] packet, CancellationToken ct)
    {
        var others = _mapRegistry.GetOthers(chr.MapId, chr.Id);
        foreach (var other in others)
        {
            try
            {
                await other.SendPacket(packet, ct);
            }
            catch { /* ignore failed broadcasts */ }
        }
    }

    private async Task BroadcastPacketToMapAsync(Character chr, MapleSession session, byte[] packet, CancellationToken ct)
    {
        await session.SendAsync(packet, ct);
        await BroadcastPacketToOthersAsync(chr, packet, ct);
    }

    /// <summary>
    /// 對照 Java <c>ChannelServer.broadcastSmegaPacket</c>（對 <c>PlayerStorage</c> 全體廣播，
    /// 含發送者本人，不限地圖）。MapleForge 現行單實例單頻道架構下，這等於「這個 process 目前所有
    /// 在線玩家」；多實例啟用後，`IOnlinePlayerRegistry` 需要依 channel 分桶才能精確對應 Java 的
    /// 頻道 vs 全服差異（見任務歷程 `2026-09-06_08`）。
    /// </summary>
    private async Task BroadcastPacketToAllOnlineAsync(byte[] packet, CancellationToken ct)
    {
        foreach (var online in _onlinePlayers.GetAll())
        {
            try
            {
                await online.SendPacket(packet, ct);
            }
            catch { /* ignore failed broadcasts */ }
        }
    }

    private async Task HandleMonsterBombAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        var result = V113MonsterBombHandler.Handle(reader, player, field, _combatService);
        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }

        foreach (var packet in result.MapPackets)
        {
            await BroadcastPacketToMapAsync(player.Character, session, packet, ct);
        }
    }

    private async Task HandleEventMiniGameResultAsync(
        V113EventMiniGameHandleResult result,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }

        foreach (var packet in result.MapPackets)
        {
            await BroadcastPacketToMapAsync(player.Character, session, packet, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }
    }

    private async Task HandleItemMakerResultAsync(
        V113ItemMakerHandleResult result,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }

        foreach (var packet in result.BroadcastPackets)
        {
            await BroadcastPacketToOthersAsync(player.Character, packet, ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
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

    // ── D-2: USE_SKILL_BOOK (0x4C) ──────────────────────────────────────────────

    private async Task HandleUseSkillBookAsync(PacketReader reader, Player player, MapleSession session, CancellationToken ct)
    {
        V113SkillBookHandleResult result;
        try
        {
            result = V113SkillBookHandler.HandleUseSkillBook(reader, player, _skillBookCatalog);
        }
        catch (InvalidDataException ex)
        {
            _log.LogWarning(ex, "[Channel] USE_SKILL_BOOK packet invalid charId={CharId}", player.Character.Id);
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return;
        }

        if (!result.Handled)
        {
            return;
        }

        foreach (var packet in result.SelfPackets)
        {
            await session.SendAsync(packet, ct);
        }

        if (result.BroadcastPacket is not null)
        {
            await BroadcastPacketToMapAsync(player.Character, session, result.BroadcastPacket, ct);
        }

        if (result.SendEnableActions)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
        }

        if (result.CharacterMutated)
        {
            await _charService.UpdateAsync(player.Character, ct);
        }
    }

    // ── C-1: NPC_ACTION relay ──────────────────────────────────────────────────

    private async Task HandleNpcActionAsync(
        PacketReader reader, Player player, int bodyLength,
        Dictionary<int, int> npcOidToId, MapleSession session, CancellationToken ct)
    {
        if (bodyLength < 6) return; // opcode(2) + oid(4) minimum
        var oid = reader.ReadInt();
        if (!npcOidToId.ContainsKey(oid)) return;

        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.NpcAction);
        w.WriteInt(oid);

        int remaining = bodyLength - 2 - 4; // body already excludes framing; subtract opcode(2)+oid(4)
        if (remaining > 0)
        {
            var rest = reader.ReadBytes(remaining);
            w.WriteBytes(rest);
        }

        await BroadcastPacketToMapAsync(player.Character, session, w.ToArray(), ct);
    }

    // ── C-2: CHANGE_MAP_SPECIAL (script portal 0x5E) ──────────────────────────

    private async Task<FieldInstance?> HandleChangeMapSpecialAsync(
        PacketReader reader, Player player, FieldInstance? currentField,
        Dictionary<int, int> npcOidToId, MapleSession session,
        object sessionToken, CancellationToken ct)
    {
        var chr = player.Character;
        reader.ReadByte(); // skip 1
        var portalName = reader.ReadMapleString();

        var map = _mapService.LoadMap(chr.MapId);
        var portal = map.Portals.FirstOrDefault(p => p.Name == portalName);
        if (portal is null || portal.TargetMapId is 0 or 999999999)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
            return currentField;
        }

        var targetMap = _mapService.LoadMap(portal.TargetMapId);
        var targetPortal = targetMap.Portals.FirstOrDefault(p => p.Name == portal.TargetPortalName);
        int spawnPortalId = targetPortal?.Id ?? 0;

        var newField = await WarpAsync(player, currentField, npcOidToId, session, portal.TargetMapId, sessionToken, ct, spawnPortalId);
        _log.LogInformation("[Channel] {Name} script portal '{Portal}' → 地圖 {Map}", chr.Name, portalName, portal.TargetMapId);
        return newField;
    }

    // ── C-3: SKILL_EFFECT (charge skill visual broadcast 0x57) ────────────────

    private async Task HandleSkillEffectAsync(PacketReader reader, Player player, CancellationToken ct)
    {
        var skillId = reader.ReadInt();
        var level = reader.ReadByte();
        var flags = reader.ReadByte();
        var speed = reader.ReadByte();
        var unk = reader.ReadByte();

        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.SkillEffect);
        w.WriteInt(player.Character.Id);
        w.WriteInt(skillId);
        w.WriteByte(level);
        w.WriteByte(flags);
        w.WriteByte(speed);
        w.WriteByte(unk);

        await BroadcastPacketToOthersAsync(player.Character, w.ToArray(), ct);
    }
}
