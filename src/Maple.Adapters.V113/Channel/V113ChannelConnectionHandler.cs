using System.Collections.Concurrent;
using Maple.Adapters.V113.Crypto;
using Maple.Application.Alliances;
using Maple.Application.Buddies;
using Maple.Application.Characters;
using Maple.Application.Combat;
using Maple.Application.Items;
using Maple.Application.Duey;
using Maple.Application.Drops;
using Maple.Application.Fame;
using Maple.Application.Guilds;
using Maple.Application.Maps;
using Maple.Application.Mobs;
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
using Maple.Core.Guilds;
using Maple.Core.Guilds.Bbs;
using Maple.Core.IO;
using Maple.Core.Items;
using Maple.Core.Maps;
using Maple.Core.Quests;
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
    private readonly IEquipTemplateProvider? _equipTemplates;
    private readonly MobSkillService? _mobSkills;
    private readonly PlayerMistService? _playerMists;
    private readonly PartyBuffService? _partyBuffs;
    private readonly AbilityPointService _abilityPoints;
    private readonly JobAdvancementService _jobAdvancement;
    private readonly IPortalScriptFactory? _portalScripts;
    private readonly V113SessionCommandRegistry? _sessionCommands;
    private readonly Maple.Application.Events.TransportService? _transport;
    private readonly IQuestScriptFactory? _questScripts;
    private readonly Maple.Application.Events.ITransportEffects? _transportEffects;
    private readonly IPartyRegistry? _partyRegistry;
    private readonly Maple.Application.Events.PortalScriptOverrideRegistry _portalOverrides;
    private readonly Maple.Application.Events.IEventScriptHost? _eventScripts;
    private readonly IItemUseCatalog? _itemUseCatalog;
    private readonly Maple.Application.Events.IEventWorld? _eventWorld;
    private readonly Maple.Application.Squads.SquadRegistry? _squads;
    private readonly V113SquadNotifier? _squadNotifier;
    private readonly Maple.Application.Text.WzStringCatalog? _strings;
    private readonly DropServiceOptions? _dropOptions;

    /// <summary>P176：線上角色 → (Player, session)，供跨玩家腳本效果在對方序列內使用（只在對方 Post 的工作裡讀取）。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (Player Player, MapleSession Session)> _remotePlayers = new();

    internal const string FishMerchantClosedMessage = "精靈小釣手尚未開放。";
    private readonly INpcScriptFactory _npcScripts;
    private readonly ShopService _shopService;
    private readonly StorageService _storageService;
    private readonly CombatService _combatService;
    private readonly SkillService _skillService;
    private readonly PlayerDeathService _playerDeaths;
    private readonly SummonService _summonService;
    private readonly DoorService _doorService;
    private readonly V113BuffCancellationEffects _buffEffects;
    private readonly V113PartyHpSync _partyHpSync;
    private readonly ISkillBookCatalog _skillBookCatalog;
    private readonly DropService _dropService;
    private readonly FameService _fameService;
    private readonly GuildService _guildService;
    private readonly AllianceService _allianceService;
    private readonly IGuildBbsRepository _guildBbs;
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
        AllianceService allianceService,
        IGuildBbsRepository guildBbs,
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
        V113ChannelOptions options,
        PlayerDeathService playerDeaths,
        SummonService summonService,
        DoorService doorService,
        V113BuffCancellationEffects buffEffects,
        V113PartyHpSync partyHpSync,
        IEquipTemplateProvider? equipTemplates = null,
        MobSkillService? mobSkills = null,
        PlayerMistService? playerMists = null,
        PartyBuffService? partyBuffs = null,
        AbilityPointService? abilityPoints = null,
        JobAdvancementService? jobAdvancement = null,
        IPortalScriptFactory? portalScripts = null,
        V113SessionCommandRegistry? sessionCommands = null,
        Maple.Application.Events.TransportService? transport = null,
        IQuestScriptFactory? questScripts = null,
        Maple.Application.Events.ITransportEffects? transportEffects = null,
        IPartyRegistry? partyRegistry = null,
        DropServiceOptions? dropOptions = null,
        Maple.Application.Events.PortalScriptOverrideRegistry? portalOverrides = null,
        Maple.Application.Events.IEventScriptHost? eventScripts = null,
        IItemUseCatalog? itemUseCatalog = null,
        Maple.Application.Events.IEventWorld? eventWorld = null,
        Maple.Application.Squads.SquadRegistry? squads = null,
        V113SquadNotifier? squadNotifier = null,
        Maple.Application.Text.WzStringCatalog? strings = null)
    {
        _partyRegistry = partyRegistry;
        _portalOverrides = portalOverrides ?? new Maple.Application.Events.PortalScriptOverrideRegistry();
        _eventScripts = eventScripts;
        _itemUseCatalog = itemUseCatalog;
        _eventWorld = eventWorld;
        _squads = squads;
        _squadNotifier = squadNotifier;
        _strings = strings;
        _dropOptions = dropOptions;
        _transportEffects = transportEffects;
        _questScripts = questScripts ?? npcScripts as IQuestScriptFactory;
        _transport = transport;
        _sessionCommands = sessionCommands;
        _portalScripts = portalScripts ?? npcScripts as IPortalScriptFactory;
        _jobAdvancement = jobAdvancement ?? new JobAdvancementService();
        _log = log;
        _playerMists = playerMists;
        _partyBuffs = partyBuffs;
        _abilityPoints = abilityPoints ?? new AbilityPointService();
        _equipTemplates = equipTemplates;
        _mobSkills = mobSkills;
        _partyHpSync = partyHpSync;
        _buffEffects = buffEffects;
        _doorService = doorService;
        _playerDeaths = playerDeaths;
        _summonService = summonService;
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
        _allianceService = allianceService;
        _guildBbs = guildBbs;
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

        // P166：cm.warp(map, portal)——Java changeMap(mapz, mapz.getPortal(portal))。
        async Task WarpFromNpcToPortalAsync(int mapId, int portal, CancellationToken token)
        {
            if (player is null) return;
            currentField = await WarpAsync(player, currentField, npcOidToId, session, mapId, sessionToken, token, portal);
        }

        // P174：QUEST_ACTION：腳本任務（4 開始／5 完成）走 scripts/quest/<id>.js；其他動作照舊。
        async Task HandleQuestActionLocalAsync(PacketReader r, CancellationToken token)
        {
            if (player is null) return;
            QuestClientAction action;
            try
            {
                action = V113QuestPackets.ParseQuestAction(r);
            }
            catch (InvalidDataException)
            {
                return;
            }

            if (action.Kind is QuestClientActionKind.ScriptedStart or QuestClientActionKind.ScriptedComplete)
            {
                await StartQuestScriptAsync(action, token);
                return;
            }

            await SendQuestTransactionResultAsync(_questService.HandleClientAction(player, action), player, session, token);
        }

        // P174：Java NPCScriptManager.startQuest／endQuest：可開始/可完成才跑腳本；已在對話中則忽略；
        // 完成動作（case 5）不論結果都送 showSpecialEffect(9) 給自己與他人。
        async Task StartQuestScriptAsync(QuestClientAction action, CancellationToken token)
        {
            if (player is null) return;
            var end = action.Kind == QuestClientActionKind.ScriptedComplete;
            var allowed = end ? _questService.CanCompleteQuest(player, action.QuestId) : _questService.CanStartQuest(player, action.QuestId);
            if (allowed && conversation is not { Active: true } && _questScripts is not null)
            {
                var ctx = NewNpcContext(action.NpcId, player, questId: action.QuestId);
                if (_questScripts.TryCreateQuest(action.QuestId, end, ctx) is { } script)
                {
                    var convo = CreateConversation(action.NpcId, script, ctx, player, session, OpenShopFromNpcAsync, OpenStorageFromNpcAsync, WarpFromNpcAsync, WarpFromNpcToPortalAsync);
                    await convo.StartAsync(token);
                    conversation = convo.Active ? convo : null;
                }
                else if (!end)
                {
                    await session.SendAsync(V113BroadcastPackets.PopupMessage($"此任務尚未建置，請通知管理員。\r\n任務編號: {action.QuestId}"), token);
                }
            }

            if (end)
            {
                await session.SendAsync(V113StatsPackets.ShowSpecialEffect(9), token);
                await BroadcastPacketToOthersAsync(player.Character, V113StatsPackets.ShowForeignEffect(player.Character.Id, 9), token);
            }
        }

        // P170：Java MapleGenericPortal.enterPortal 腳本分支；pi.openNpc 開啟 NPC 對話；仍在原地圖才 enableActions。
        async Task EnterScriptPortalAsync(MapPortal portal, CancellationToken token)
        {
            if (player is null) return;
            var startMap = player.Character.MapId;
            var (_, openNpcId) = await RunPortalScriptAsync(
                portal, player, session, OpenShopFromNpcAsync, OpenStorageFromNpcAsync, WarpFromNpcAsync, WarpFromNpcToPortalAsync, token);
            if (openNpcId is { } npcId)
            {
                conversation = await StartNpcConversationByNpcIdAsync(
                    npcId, player, session, OpenShopFromNpcAsync, OpenStorageFromNpcAsync, WarpFromNpcAsync, token, WarpFromNpcToPortalAsync);
            }

            if (player.Character.MapId == startMap)
            {
                await session.SendAsync(V113StatsPackets.EnableActions(), token);
            }
        }

        // P170：CHANGE_MAP_SPECIAL(0x5E)：Java PlayerHandler.ChangeMapSpecial → portal.enterPortal（腳本優先）。
        async Task HandleChangeMapSpecialLocalAsync(PacketReader r, CancellationToken token)
        {
            if (player is null) return;
            r.ReadByte();
            var portalName = r.ReadMapleString();
            var portal = _mapService.LoadMap(player.Character.MapId).Portals.FirstOrDefault(p => p.Name == portalName);
            if (portal is not null && WithScriptOverride(player.Character.MapId, portal) is { Script.Length: > 0 } scripted)
            {
                await EnterScriptPortalAsync(scripted, token);
                return;
            }

            currentField = await ChangeMapSpecialByNameAsync(portalName, player, currentField, npcOidToId, session, sessionToken, token);
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
                // P170：script portal → Java enterPortal → executePortalScript（P173：船艙出口由 Boats 事件覆寫腳本）。
                if (portal is not null && WithScriptOverride(chr.MapId, portal) is { Script.Length: > 0 } scripted)
                {
                    await EnterScriptPortalAsync(scripted, token);
                    return;
                }

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
                // P179：Java ChangeMap 死亡分支 eim.revivePlayer(chr) && chr.isAlive() → 由事件處理。
                if (!player.IsAlive && _eventScripts is not null && RunEventHook(() => _eventScripts.OnPlayerRevive(chr.Id)) && player.IsAlive)
                {
                    return;
                }

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

            var cancellations = _skillService.CancelExpiredBuffs(player, DateTimeOffset.UtcNow);
            foreach (var cancellation in cancellations)
            {
                await target.SendAsync(V113SkillPackets.CancelBuff(cancellation.Stats), token);
            }

            await ApplyBuffCancellationSideEffectsAsync(player, currentField, cancellations, target, token);
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

                            // 執行期玩家（持有位置）
                            // P084：對照 Java loadCharFromDB 的 getPortal(initialSpawnPoint) ?? getPortal(0) → setPosition。
                            player = new Player(chr, _mapService.GetLandingPosition(chr.MapId, chr.SpawnPoint) ?? new Position(0, 0, 0, 0), _equipTemplates);
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
                            await SendStartedQuestMobKillsAsync(chr, (pkt, tkn) => s.SendAsync(pkt, tkn), token);

                            // P172：伺服器端換圖排進本連線序列（船班等）。
                            _sessionCommands?.Register(
                                chr.Id,
                                sessionToken,
                                (mapId, portal) => s.Post(async token =>
                                {
                                    if (player is not null)
                                    {
                                        currentField = await WarpAsync(player, currentField, npcOidToId, s, mapId, sessionToken, token, portal);
                                    }
                                }),
                                work => s.Post(work));
                            _remotePlayers[chr.Id] = (player, s);

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
                            // 對照 Java InterServerHandler 登入流程 c.sendPacket(FamilyPacket.getFamilyData())：
                            // 家族特權清單（buff 花費表）是靜態常數表，登入時無條件送給所有玩家（不限
                            // 是否已加入家族），供家族 UI 顯示花費。V113FamilyPackets.FamilyPrivilegeList
                            // 先前已存在但零呼叫者。
                            await s.SendAsync(V113FamilyPackets.FamilyPrivilegeList(), token);

                            await _partyOperationHandler.NotifyLoginAsync(
                                player,
                                _options.ChannelIndex,
                                (pkt, tkn) => s.SendAsync(pkt, tkn),
                                token);

                            var pos = player.Position;
                            currentField = EnterField(chr.MapId, player);

                            _mapRegistry.Register(chr.MapId, chr.Id, player, (pkt, tkn) => s.SendAsync(pkt, tkn), sessionToken);
                            await _partySearchHandler.NotifyMapEntryAsync(player, (pkt, tkn) => s.SendAsync(pkt, tkn), token);
                            await _partyOperationHandler.NotifyMapEntryAsync(player, _options.ChannelIndex, (pkt, tkn) => s.SendAsync(pkt, tkn), token);

                            // Notify existing players of new arrival（並讓新玩家看到現有玩家）
                            var others = _mapRegistry.GetOthers(chr.MapId, chr.Id);
                            foreach (var other in others)
                            {
                                var spawnForOther = await BuildSpawnPlayerPacketAsync(player, pos.X, pos.Y, pos.Stance, pos.Foothold, token);
                                await other.SendPacket(spawnForOther, token);

                                var spawnForNew = await BuildSpawnPlayerPacketAsync(other.Player, 0, 0, 0, 0, token);
                                await s.SendAsync(spawnForNew, token);
                            }

                            // 地圖物件同步：把該地圖的 NPC / monster spawn 給剛進場的玩家。
                            await SpawnMapNpcsAsync(chr.MapId, s, npcOidToId, token);
                            await SendFieldHiredMerchantsAsync(chr.MapId, s, token);
                            await SendFieldMonstersAsync(currentField, s, token);
                            await SendFieldDropsAsync(currentField, s, token);
                            await SendFieldMistsAsync(currentField, s, token);
                            foreach (var doorPacket in _buffEffects.BuildDoorSpawnData(chr.MapId, chr.Id))
                            {
                                await s.SendAsync(doorPacket, token); // P160：MapleDoor.sendSpawnData
                            }

                            if (BoatPacketOnEntry(chr.MapId) is { } boatPacket)
                            {
                                await s.SendAsync(boatPacket, token); // P173：MapleMap.addPlayer 的 hasBoat
                            }

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
                        var damageResult = await V113ReactorHandler.HandleDamageReactorAsync(
                            reader,
                            player,
                            currentField,
                            _reactorService,
                            (packet, tkn) => BroadcastPacketToMapAsync(player.Character, s, packet, tkn),
                            token);
                        await SendReactorScriptEffectsAsync(damageResult?.ScriptContext, player, s, token);
                        if (damageResult?.ScriptContext is { PendingWarpMapId: { } damageWarp } damageCtx)
                        {
                            currentField = await WarpAsync(player, currentField, npcOidToId, s, damageWarp, sessionToken, token, damageCtx.PendingWarpPortal);
                        }

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

                        await SendReactorScriptEffectsAsync(touchResult?.ScriptContext, player, s, token);
                        if (touchResult?.ScriptContext is { PendingWarpMapId: { } touchWarp } touchCtx)
                        {
                            currentField = await WarpAsync(player, currentField, npcOidToId, s, touchWarp, sessionToken, token, touchCtx.PendingWarpPortal);
                        }

                        break;
                    }

                    case V113ChannelRecvOp.MoveLife:
                        if (player is null || currentField is null) break;
                        if (await HandleMoveLifeAsync(reader, player, currentField, s, token) is { } banish)
                        {
                            // P130：Java changeMapBanish → dropMessage(5, msg) → changeMap(target, target.getPortal(portal))。
                            await s.SendAsync(V113BroadcastPackets.ChatNotice(banish.Message), token);
                            var banishPortal = _mapService.LoadMap(banish.MapId).Portals.FirstOrDefault(p => p.Name == banish.PortalName);
                            currentField = await WarpAsync(
                                player, currentField, npcOidToId, s,
                                banish.MapId, sessionToken, token, banishPortal?.Id ?? 0);
                        }

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
                    {
                        if (player is null) break;
                        // P162：Java DistributeAP 一開始就送 updatePlayerStats(空, true)，成功再送 (更新, true)。
                        await s.SendAsync(V113StatsPackets.EnableActions(), token);
                        var apMutation = V113StatsHandlers.HandleDistributeAp(reader, player, _abilityPoints);
                        if (apMutation.Updates.Count > 0)
                        {
                            await s.SendAsync(V113StatsPackets.UpdateStats(apMutation.Updates, itemReaction: true), token);
                        }

                        break;
                    }

                    case V113ChannelRecvOp.HealOverTime:
                        if (player is null) break;
                        var hpBeforeHeal = player.Hp;
                        var healResult = V113StatsHandlers.HandleHealOverTime(reader, player, _statsService);
                        // 對照 Java PlayerHandler.Heal 的 REGEN_HIGH_HP：只記錄不阻擋，回血已經在
                        // HandleHealOverTime 內套用完成，這裡的檢查結果不影響已經發生的回血。
                        if (IsRegenHighHp(healResult.RequestedHp, player.ChairItemId))
                        {
                            _log.LogWarning(
                                "[Channel] 玩家 {Name} 回血量異常 requested={Hp}", player.Character.Name, healResult.RequestedHp);
                        }

                        await HandleStatsMutationAsync(healResult.Mutation, s, sendSkill: false, token);
                        if (player.Hp != hpBeforeHeal)
                        {
                            await _partyHpSync.BroadcastAsync(player, token); // P095：Java setHp → updatePartyMemberHP
                        }

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
                        await HandleSpecialMoveAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.CancelBuff:
                        if (player is null) break;
                        await HandleCancelBuffAsync(reader, player, currentField, s, token);
                        break;

                    case V113ChannelRecvOp.QuestAction:
                        if (player is null) break;
                        await HandleQuestActionLocalAsync(reader, token);
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
                            V113RewardItemHandler.HandleRewardItem(reader, player, _itemUseCatalog, Random.Shared, DateTimeOffset.UtcNow),
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

                    case V113ChannelRecvOp.UseFishMerchant:
                        // P153：對照 Java MapleServerHandler case USE_FISH_MERCHANT → dropMessage(5, "精靈小釣手尚未開放。")。
                        if (player is null) break;
                        await s.SendAsync(V113BroadcastPackets.ChatNotice(FishMerchantClosedMessage), token);
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
                                token,
                                WarpFromNpcToPortalAsync);
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
                        var ctx = new V113ItemUseContext
                        {
                            ReturnMapId = player.Character.MapId,
                            CanUseSummonBag = !FieldLimitType.SummoningBag.Check(_mapService.LoadMap(player.Character.MapId).FieldLimit),
                        };
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
                        var ctx = new V113ItemUseContext
                        {
                            ReturnMapId = player.Character.MapId,
                            // 對照 Java InventoryHandler.UseReturnScroll：跟一般補藥共用 PotionUse 旗標，非獨立旗標。
                            CanUseReturnScroll = !FieldLimitType.PotionUse.Check(_mapService.LoadMap(player.Character.MapId).FieldLimit),
                        };
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
                            token,
                            WarpFromNpcToPortalAsync);
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
                        // 對照 Java InventoryHandler.UseItem：610030600/105100300 是 Java 自己的
                        // "cwk quick hack" 硬編例外地圖，即使場地限制生效也允許使用。
                        var canUsePotion =
                            !FieldLimitType.PotionUse.Check(_mapService.LoadMap(player.Character.MapId).FieldLimit)
                            || player.Character.MapId is 610030600 or 105100300;
                        var hpBeforeUseItem = player.Hp;
                        var useItemResult = _useConsumableHandler.Handle(reader, player, canUsePotion);
                        if (useItemResult.Handled)
                        {
                            foreach (var packet in useItemResult.Packets)
                            {
                                await s.SendAsync(packet, token);
                            }

                            foreach (var packet in useItemResult.ForeignPackets ?? [])
                            {
                                await BroadcastPacketToOthersAsync(player.Character, packet, token);
                            }

                            // P099：Java 藥水 addMPHP/healHP → setHp → updatePartyMemberHP。
                            await _partyHpSync.BroadcastIfChangedAsync(player, hpBeforeUseItem, token);

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
                                    token,
                                    useCashResult.WarpToPortalId);
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
                    {
                        if (player is null) break;
                        // 對照 Java PetHandler.Pet_AutoPotion：與一般補藥共用同一個 PotionUse
                        // 場地限制旗標（見 P031 UseItem 的同款計算）。
                        var petAutoPotCanUse =
                            !FieldLimitType.PotionUse.Check(_mapService.LoadMap(player.Character.MapId).FieldLimit)
                            || player.Character.MapId is 610030600 or 105100300;
                        var hpBeforeAutoPot = player.Hp;
                        var petAutoPotResult = await V113PetHandler.HandlePetAutoPotion(
                            reader, player, s, _petService, _useConsumableHandler, petAutoPotCanUse, token);
                        await _partyHpSync.BroadcastIfChangedAsync(player, hpBeforeAutoPot, token); // P099
                        if (petAutoPotResult.CharacterMutated)
                        {
                            await _charService.UpdateAsync(player.Character, token);
                        }

                        break;
                    }

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
                        await V113SummonHandler.HandleSummonAttackAsync(
                            reader,
                            player,
                            currentField,
                            s,
                            _mapRegistry,
                            _combatService,
                            (hits, tkn) => SendCombatHitsAsync(hits, player, s, tkn),
                            token);
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
                        await HandleChangeMapSpecialLocalAsync(reader, token);
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
                // P179：Java MapleClient.disconnect → eim.playerDisconnected（在地圖移除與存檔之前）。
                var disconnecting = player.Character.Id;
                RunEventHook(() => _eventScripts?.OnPlayerDisconnected(disconnecting));
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
                _sessionCommands?.Unregister(player.Character.Id, sessionToken);
                _remotePlayers.TryRemove(new KeyValuePair<int, (Player, MapleSession)>(player.Character.Id, (player, session)));

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

                await DetachSummonsOnLeaveAsync(player, currentField, player.Character.MapId, session: null, CancellationToken.None);
            }

            if (player is not null)
            {
                await CloseMagicDoorAsync(player.Character.Id, CancellationToken.None); // P088：Java changeRemoval → removeDoor
            }

            // Cleanup: remove from map, notify others
            // P104：對照 Java MapleClient.disconnect 整段包在 player != null 內。chr 讀到後、player 建立前
            // （如帳號查詢拋例外）斷線時，舊碼對 player! 丟 NRE，蓋掉原始例外；此時尚未註冊地圖，無需清理。
            if (chr is not null && player is not null)
            {
                _partySearchHandler.NotifyMapLeave(player);
                await _partyOperationHandler.NotifyLogoutAsync(player, CancellationToken.None);
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

                _mapService.InitializeFieldEnvironment(field, DateTimeOffset.UtcNow);
            }

            field.Add(player);
        }

        return field;
    }

    private async Task<byte[]> BuildSpawnPlayerPacketAsync(
        Player player,
        short x,
        short y,
        byte stance,
        short foothold,
        CancellationToken ct)
    {
        var chr = player.Character;
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

        return V113MapPackets.SpawnPlayer(player, x, y, stance, foothold, guildInfo);
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

    /// <summary>P131：進場時補送現有地圖霧（Java MapleMist.sendSpawnData）。</summary>
    private static async Task SendFieldMistsAsync(FieldInstance field, MapleSession session, CancellationToken ct)
    {
        Mist[] mists;
        lock (field)
        {
            mists = field.Objects.OfType<Mist>().ToArray();
        }

        foreach (var mist in mists)
        {
            await session.SendAsync(V113MistPackets.SpawnMist(mist), ct);
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
        CancellationToken ct,
        Func<int, int, CancellationToken, Task>? warpToPortal = null)
    {
        var oid = reader.ReadInt();
        if (!oidToNpcId.TryGetValue(oid, out var npcId))
        {
            _log.LogDebug("[Channel] NPC_TALK 未知 oid={Oid}", oid);
            return null;
        }

        return await StartNpcConversationByNpcIdAsync(npcId, player, session, openShop, openStorage, warp, ct, warpToPortal);
    }

    private async Task<NpcConversation?> StartNpcConversationByNpcIdAsync(
        int npcId,
        Player player,
        MapleSession session,
        Func<int, CancellationToken, Task> openShop,
        Func<int, CancellationToken, Task> openStorage,
        Func<int, CancellationToken, Task> warp,
        CancellationToken ct,
        Func<int, int, CancellationToken, Task>? warpToPortal = null)
    {
        var ctx = NewNpcContext(npcId, player);
        var script = _npcScripts.TryCreate(npcId, ctx);
        if (script is null)
        {
            _log.LogDebug("[Channel] NPC {Npc} 無對應腳本，略過", npcId);
            return null;
        }

        var convo = CreateConversation(npcId, script, ctx, player, session, openShop, openStorage, warp, warpToPortal);
        await convo.StartAsync(ct);
        _log.LogInformation("[Channel] NPC {Npc} 對話開始", npcId);
        return convo.Active ? convo : null;
    }

    /// <summary>NPC／portal 腳本共用的對話 flush 接線（P170 自兩處 NPC 啟動抽出）。</summary>
    private NpcConversation CreateConversation(
        int npcId,
        INpcScript script,
        NpcContext ctx,
        Player player,
        MapleSession session,
        Func<int, CancellationToken, Task> openShop,
        Func<int, CancellationToken, Task> openStorage,
        Func<int, CancellationToken, Task> warp,
        Func<int, int, CancellationToken, Task>? warpToPortal)
        => new(
            npcId, script, ctx,
            sendDialog: (dlg, c) => session.SendAsync(V113NpcDialogEncoder.Encode(dlg), c),
            warp: warp,
            openShop: openShop,
            openStorage: openStorage,
            sendQuestResult: (result, c) => SendQuestTransactionResultAsync(result, player, session, c),
            sendInfoQuestUpdate: (questId, data, c) => session.SendAsync(V113QuestPackets.UpdateInfoQuest(questId, data), c),
            sendBuddyCapacity: (capacity, c) => session.SendAsync(V113BuddyPackets.UpdateBuddyCapacity((byte)capacity), c),
            increaseGuildCapacity: c => IncreaseGuildCapacityAndBroadcastAsync(player, session, c),
            sendPopupMessage: (msg, c) => session.SendAsync(V113BroadcastPackets.PopupMessage(msg), c),
            disbandGuild: c => DisbandGuildAndCleanupAsync(player, session, c),
            sendRepairWindow: (repairNpcId, c) => session.SendAsync(V113RepairPackets.RepairWindow(repairNpcId), c),
            warpToPortal: warpToPortal,
            sendMessage: (type, msg, c) => session.SendAsync(V113BroadcastPackets.ServerNotice(type, msg), c),
            sendJobChange: (change, c) => SendJobChangeAsync(player, change, session, c),
            sendEnvironment: (broadcast, mode, name, c) => broadcast
                ? BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.EnvironmentChange(name, mode), c)
                : session.SendAsync(V113CombatPackets.EnvironmentChange(name, mode), c),
            sendWorldMessage: (type, msg, c) => BroadcastPacketToAllOnlineAsync(V113BroadcastPackets.ServerNotice(type, msg), c),
            playPortalSe: c => session.SendAsync(V113SkillPackets.ShowOwnBuffEffect(0, 7), c),
            killAllMob: c => _transportEffects is { } effects ? effects.KillAllMonstersAsync(player.Character.MapId, c, animate: true) : Task.CompletedTask,
            sendPartyAction: SendPartyActionAsync,
            sendSquadNotice: (notice, c) => _squadNotifier is { } notifier ? notifier.SendAsync(notice, c) : Task.CompletedTask,
            sendSlotUpdate: (type, slots, c) => session.SendAsync(V113TransportEffects.SlotUpdate((byte)type, slots), c),
            applyWorldEffects: (npcCtx, c) => ApplyNpcWorldEffectsAsync(npcCtx, player, c),
            sendUiEffect: (effect, c) => effect.Kind switch
            {
                NpcUiEffectKind.Hint => session.SendAsync(V113StatsPackets.SendHint(effect.Text, effect.A, effect.B), c),
                NpcUiEffectKind.WzEffect => session.SendAsync(V113StatsPackets.ShowWzEffect(effect.Text), c),
                NpcUiEffectKind.SummonHelper => session.SendAsync(V113UiPackets.SummonHelper(effect.A != 0), c),
                NpcUiEffectKind.SummonMessageText => session.SendAsync(V113UiPackets.SummonMessage(effect.Text), c),
                NpcUiEffectKind.SummonMessageType => session.SendAsync(V113UiPackets.SummonMessage(effect.A), c),
                NpcUiEffectKind.AranBalloon => session.SendAsync(V113UiPackets.AranTutorialBalloon(effect.Text), c),
                NpcUiEffectKind.EvanTutorial => session.SendAsync(V113UiPackets.EvanTutorial(effect.Text), c),
                NpcUiEffectKind.IntroDisableUi => session.SendAsync(V113UiPackets.IntroDisableUi(effect.A != 0), c),
                NpcUiEffectKind.IntroLock => session.SendAsync(V113UiPackets.IntroLock(effect.A != 0), c),
                NpcUiEffectKind.StatusMessage => session.SendAsync(V113UiPackets.StatusMessage(effect.A), c),
                _ => BroadcastPacketToMapAsync(player.Character, session, V113BroadcastPackets.ServerNotice(effect.A, effect.Text), c),
            });

    /// <summary>
    /// P170：Java <c>MapleGenericPortal.enterPortal</c> 的腳本分支 → <c>PortalScriptManager.executePortalScript</c>：
    /// 跑 scripts/portal/&lt;script&gt;.js 的 <c>enter(pi)</c> 並 flush（換圖、訊息、音效…）；<c>pi.openNpc</c> 回傳要開的 NPC。
    /// 找不到腳本回 false（呼叫端照舊 enableActions）。
    /// </summary>
    private async Task<(bool Ran, int? OpenNpcId)> RunPortalScriptAsync(
        MapPortal portal,
        Player player,
        MapleSession session,
        Func<int, CancellationToken, Task> openShop,
        Func<int, CancellationToken, Task> openStorage,
        Func<int, CancellationToken, Task> warp,
        Func<int, int, CancellationToken, Task> warpToPortal,
        CancellationToken ct)
    {
        if (_portalScripts is null || string.IsNullOrEmpty(portal.Script))
        {
            return (false, null);
        }

        var ctx = NewNpcContext(0, player, (portal.Name, portal.Id));
        var script = _portalScripts.TryCreatePortal(portal.Script, ctx);
        if (script is null)
        {
            _log.LogInformation("[Channel] 未處理的傳送腳本 {Script}（地圖 {Map}）", portal.Script, player.Character.MapId);
            return (false, null);
        }

        var convo = CreateConversation(0, script, ctx, player, session, openShop, openStorage, warp, warpToPortal);
        await convo.StartAsync(ct);
        _log.LogInformation("[Channel] {Name} 進入傳送腳本 {Script}", player.Character.Name, portal.Script);
        return (true, convo.PendingOpenNpcId);
    }

    /// <summary>
    /// cm.increaseGuildCapacity 的實際擴充+廣播（對照 Java <c>MapleGuild.increaseCapacity</c> 的
    /// broadcast）。楓幣已在 <see cref="NpcContext.IncreaseGuildCapacity"/> 決定送出請求時，於
    /// <c>GuildService.IncreaseGuildCapacityAsync</c> 內無條件扣除；這裡只負責容量真的增加時的廣播，
    /// 已達上限（<see cref="GuildCommandResult.Succeeded"/> 為 false）則不廣播，僅錢已經扣了。
    /// </summary>
    private async Task IncreaseGuildCapacityAndBroadcastAsync(Player player, MapleSession session, CancellationToken ct)
    {
        var result = await _guildService.IncreaseGuildCapacityAsync(player, ct);
        if (!result.Succeeded || result.Guild is null)
        {
            return;
        }

        var packet = V113GuildPackets.GuildCapacityChange(result.Guild.Id, result.Guild.Capacity);
        foreach (var recipientId in result.Recipients.Distinct())
        {
            if (recipientId == player.Character.Id)
            {
                await session.SendAsync(packet, ct);
                continue;
            }

            var target = _onlinePlayers.FindById(recipientId);
            if (target is null)
            {
                continue;
            }

            try
            {
                await target.SendPacket(packet, ct);
            }
            catch
            {
                // Guild 廣播 best-effort；session 可能剛好斷線。
            }
        }
    }

    /// <summary>
    /// cm.disbandGuild 的實際解散協調。<see cref="GuildService.DisbandGuildAsync(Player, CancellationToken)"/>
    /// 只做「登記表移除＋所有成員持久化欄位重置」；BBS 清理、同盟移除、在線成員記憶體同步與廣播
    /// 是跨服務協調，比照既有 <c>V113GuildOperationHandler</c> 同時持有 GuildService+AllianceService
    /// 協調同盟廣播的模式，在這裡（同時持有 GuildService/AllianceService/IGuildBbsRepository/
    /// IOnlinePlayerRegistry 的 handler）完成。對照 Java <c>MapleGuild.disbandGuild</c>：
    /// 刪 BBS 討論串/回覆 → （已在 DisbandGuildAsync 內完成的）刪公會列 → 同盟中則移除 →
    /// 廣播 guildDisband；另外 Java 對在線成員會直接同步記憶體中的 guildId/guildRank/allianceRank
    /// （<c>World.Guild.setGuildAndRank</c>），這裡對照做同一件事。
    /// </summary>
    private async Task DisbandGuildAndCleanupAsync(Player player, MapleSession session, CancellationToken ct)
    {
        var result = await _guildService.DisbandGuildAsync(player, ct);
        if (!result.Succeeded || result.Guild is null)
        {
            return;
        }

        var guild = result.Guild;

        var threads = await _guildBbs.GetThreadsAsync(guild.Id, ct);
        foreach (var thread in threads)
        {
            await _guildBbs.DeleteThreadAsync(guild.Id, thread.ThreadId, ct);
        }

        if (guild.AllianceId > 0)
        {
            await _allianceService.RemoveGuildAsync(guild.AllianceId, guild.Id, expelled: false, ct);
        }

        var packet = V113GuildPackets.GuildDisband(guild.Id);
        foreach (var member in guild.Members)
        {
            var online = _onlinePlayers.FindById(member.CharacterId);
            if (online is null)
            {
                continue;
            }

            online.Character.GuildId = 0;
            online.Character.GuildRank = Guild.DefaultMemberRank;
            online.Character.AllianceRank = Guild.DefaultAllianceRank;

            if (member.CharacterId == player.Character.Id)
            {
                await session.SendAsync(packet, ct);
                continue;
            }

            try
            {
                await online.SendPacket(packet, ct);
            }
            catch
            {
                // Guild 廣播 best-effort；session 可能剛好斷線。
            }
        }
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
    /// <summary>P173：Boats.js 的 setScriptName（船艙 out00/out01 → OBoat1/2、EBoat1/2）；P177：事件腳本的 setScriptName。</summary>
    /// <summary>
    /// P179：Java killBy → giveExpToCharacter(highestDamage) 的 monsterKilled，與 unregisterMonster。
    /// 最高傷害者＝擊殺者（KillerHighestDamage）或其他領經驗者中標記最高者；沒有經驗分配（無獎勵擊殺）則只 unregister。
    /// </summary>
    private void NotifyEventMonsterKilled(Player killer, CombatMobHit hit)
    {
        if (_eventScripts is null)
        {
            return;
        }

        int? highest = hit.Rewards switch
        {
            null => null,
            { KillerHighestDamage: true } => killer.Character.Id,
            { OtherAwards: { } others } => others.FirstOrDefault(static a => a.HighestDamage)?.Receiver.Character.Id,
            _ => null,
        };
        RunEventHook(() => _eventScripts.OnMonsterKilled(killer.Character.MapId, hit.ObjectId, hit.MonsterId, highest));
    }

    /// <summary>P179：事件腳本掛勾不得中斷遊戲流程（腳本例外已在主機吞掉，這裡再保險）。</summary>
    private void RunEventHook(Action hook)
    {
        try
        {
            hook();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[EventScript] 事件掛勾失敗");
        }
    }

    private bool RunEventHook(Func<bool> hook)
    {
        try
        {
            return hook();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[EventScript] 事件掛勾失敗");
            return false;
        }
    }

    private MapPortal WithScriptOverride(int mapId, MapPortal portal)
        => _portalOverrides.Get(mapId, portal.Name) is { } script
            ? new MapPortal { Id = portal.Id, Name = portal.Name, Type = portal.Type, X = portal.X, Y = portal.Y, TargetMapId = portal.TargetMapId, TargetPortalName = portal.TargetPortalName, Script = script }
            : portal;

    /// <summary>
    /// P173：Java <c>MapleMap.addPlayer</c>：<c>hasBoat() == 2</c>（船班 docked）→ boatPacket(true)；<c>== 1</c>（WZ shipObj）→ boatPacket(false)。
    /// </summary>
    private byte[]? BoatPacketOnEntry(int mapId)
        => _transport?.IsDocked(mapId) == true
            ? V113TransportEffects.BoatPacket(true)
            : _mapService.LoadMap(mapId).HasShip ? V113TransportEffects.BoatPacket(false) : null;

    /// <summary>P169：NPC cm 的領域依賴（道具存在、地圖人數、returnMap、P170 portal 名稱→ID）。</summary>
    private NpcContext NewNpcContext(int npcId, Player player, (string Name, int Id)? portal = null, int questId = 0)
        => new(
            npcId,
            player,
            _questService,
            _jobAdvancement,
            _equipTemplates is { } templates ? templates.ItemExists : null,
            playerCount: mapId => _mapRegistry.GetAll(mapId).Count(),
            returnMapOf: mapId => _mapService.LoadMap(mapId).ReturnMapId,
            portal: portal,
            portalIdOf: (mapId, name) => _mapService.LoadMap(mapId).Portals.FirstOrDefault(p => p.Name == name)?.Id ?? -1,
            transport: _transport,
            questId: questId,
            monstersOf: mapId => FieldMonsterIds(mapId),
            reactorStateOf: (mapId, name) => FieldReactorState(mapId, name),
            parties: _partyRegistry,
            playersOnMap: mapId => _mapRegistry.GetAll(mapId).Select(static e => e.CharId).ToArray(),
            expRate: _dropOptions?.ExpRate ?? 1,
            squads: _squads,
            strings: _strings,
            guildPointsOf: guildId => _guildService.GetGuildAsync(guildId).GetAwaiter().GetResult()?.GuildPoints ?? 0,
            isCash: _equipTemplates is { } cashTemplates ? cashTemplates.IsCash : null,
            channelNumber: _options.ChannelIndex + 1,
            reactorsOf: FieldReactorStates);

    /// <summary>
    /// P176：跨隊員腳本效果——排進該隊員自己的連線序列執行（warp／經驗／道具），結果照一般 cm 送包流程送給對方。
    /// </summary>
    private Task SendPartyActionAsync(NpcPartyAction action, CancellationToken ct)
    {
        if (_sessionCommands is null)
        {
            return Task.CompletedTask;
        }

        if (action.Kind == NpcPartyActionKind.Warp)
        {
            _sessionCommands.Warp(action.CharacterId, action.MapId, action.Portal);
            return Task.CompletedTask;
        }

        // P199：setPartyBossLog——隊員連線序列內寫入。
        if (action.Kind == NpcPartyActionKind.BossLog)
        {
            _sessionCommands.TryPost(action.CharacterId, _ =>
            {
                if (_remotePlayers.TryGetValue(action.CharacterId, out var member) && action.Text is { } bossId)
                {
                    member.Player.SetBossLog(bossId, DateTimeOffset.UtcNow);
                }

                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        }

        _sessionCommands.TryPost(action.CharacterId, async token =>
        {
            if (!_remotePlayers.TryGetValue(action.CharacterId, out var target))
            {
                return;
            }

            var ctx = NewNpcContext(0, target.Player);
            var script = new DelegateNpcScript(() =>
            {
                if (action.Kind == NpcPartyActionKind.Exp)
                {
                    ctx.GainExp(action.Amount);
                }
                else
                {
                    ctx.GainItem(action.ItemId, action.RemoveAll ? -ctx.ItemQuantity(action.ItemId) : action.Quantity);
                }

                ctx.Dispose();
            });
            var convo = new NpcConversation(
                0, script, ctx,
                sendDialog: (_, _) => Task.CompletedTask,
                warp: (_, _) => Task.CompletedTask,
                sendQuestResult: (result, c) => SendQuestTransactionResultAsync(result, target.Player, target.Session, c));
            await convo.StartAsync(token);
        });
        return Task.CompletedTask;
    }

    /// <summary>P176：以委派實作的單步腳本（跨隊員效果用）。</summary>
    private sealed class DelegateNpcScript(Action start) : INpcScript
    {
        public void Start() => start();

        public void Resume(int mode, int type, int selection)
        {
        }
    }

    /// <summary>P175：腳本查詢地圖怪物（存活）ID。</summary>
    private IReadOnlyList<int> FieldMonsterIds(int mapId)
    {
        var field = _fieldRegistry.GetOrCreate(mapId, out _);
        lock (field)
        {
            return field.Objects.OfType<Mob>().Where(static m => m.IsAlive).Select(static m => m.Definition.MonsterId).ToArray();
        }
    }

    /// <summary>P175：腳本查詢反應堆狀態（名稱不分大小寫，同 Java equalsIgnoreCase）；找不到回 -1。</summary>
    /// <summary>P200：本圖所有反應堆（ID、狀態）。</summary>
    private IReadOnlyList<(int ReactorId, byte State)> FieldReactorStates(int mapId)
    {
        var field = _fieldRegistry.GetOrCreate(mapId, out _);
        lock (field)
        {
            return field.Objects.OfType<Reactor>().Select(static r => (r.ReactorId, r.State)).ToArray();
        }
    }

    private int FieldReactorState(int mapId, string name)
    {
        var field = _fieldRegistry.GetOrCreate(mapId, out _);
        lock (field)
        {
            return field.Objects.OfType<Reactor>().FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) is { } reactor
                ? reactor.State
                : -1;
        }
    }

    private static async Task ContinueNpcConversationAsync(PacketReader reader, NpcConversation convo, CancellationToken ct)
    {
        var lastMsg = reader.ReadByte();
        int mode = (sbyte)reader.ReadByte();   // 1=下一步/是, 0=上一步/否, -1=ESC

        // P168：對照 Java NPCMoreTalk：getText（lastMsg 2）→ action ≠ 0 讀字串後推進，否則 dispose。
        if (lastMsg == 2)
        {
            if (mode != 0 && reader.Remaining >= 2)
            {
                await convo.ContinueWithTextAsync(mode, lastMsg, reader.ReadMapleString(), ct);
            }
            else
            {
                await convo.ContinueAsync(-1, lastMsg, -1, ct);
            }

            return;
        }

        int selection = -1;
        if (reader.Remaining >= 4) selection = reader.ReadInt();
        else if (reader.Remaining > 0) selection = (sbyte)reader.ReadByte();

        // Java：npc ≠ 9000069 且選單（lastMsg 4）沒選項 → dispose；selection < -1 或 ESC 也 dispose。
        if ((convo.NpcId != 9000069 && lastMsg == 4 && selection == -1) || selection < -1)
        {
            await convo.ContinueAsync(-1, lastMsg, selection, ct);
            return;
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
        // P179：Java changeMapInternal 開頭 eventInstance.changedMap(this, to.getId())。
        RunEventHook(() => _eventScripts?.OnChangedMap(chr.Id, mapId));
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

        var carriedSummons = await DetachSummonsOnLeaveAsync(player, currentField, oldMapId, session, ct);

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

        // P084：對照 Java changeMapInternal 的 setPosition(portal)——伺服器端位置同步到落地點，
        // 不再沿用舊地圖座標直到客戶端第一次移動（撿物距離等伺服器端判斷依賴它）。
        if (_mapService.GetLandingPosition(mapId, spawnPortalId) is { } landing)
        {
            player.MoveTo(landing);
        }

        var setField = V113ChannelPackets.SetField(chr, _options.ChannelIndex);
        await session.SendAsync(setField, ct);

        var field = EnterField(mapId, player);
        _mapRegistry.Register(mapId, chr.Id, player, (pkt, tkn) => session.SendAsync(pkt, tkn), sessionToken);
        await _partySearchHandler.NotifyMapEntryAsync(player, (pkt, tkn) => session.SendAsync(pkt, tkn), ct);
        await _partyOperationHandler.NotifyMapEntryAsync(player, _options.ChannelIndex, (pkt, tkn) => session.SendAsync(pkt, tkn), ct);
        await SpawnMapNpcsAsync(mapId, session, oidToNpcId, ct);
        await SendFieldHiredMerchantsAsync(mapId, session, ct);
        await SendFieldMonstersAsync(field, session, ct);
        await SendFieldDropsAsync(field, session, ct);
        await SendFieldMistsAsync(field, session, ct);
        foreach (var doorPacket in _buffEffects.BuildDoorSpawnData(mapId, chr.Id))
        {
            await session.SendAsync(doorPacket, ct); // P160：MapleDoor.sendSpawnData
        }

        if (BoatPacketOnEntry(mapId) is { } boatPacket)
        {
            await session.SendAsync(boatPacket, ct); // P173：MapleMap.addPlayer 的 hasBoat
        }

        // P179：MapleMap.addPlayer——事件實例計時中 → getClock(剩餘秒)。
        if (_eventScripts?.GetTimeLeftSeconds(chr.Id) is { } timeLeft)
        {
            await session.SendAsync(V113TransportEffects.Clock(timeLeft), ct);
        }

        await V113ReactorHandler.SendFieldReactorsAsync(field, session, ct);

        // P085：對照 Java MapleMap.addPlayer 的 SUMMON 分支——跟隨型召喚獸在落地位置重新出生並廣播（含自己）。
        foreach (var carried in carriedSummons)
        {
            Summon? reattached;
            lock (field)
            {
                reattached = _summonService.Reattach(field, player, carried, player.Position);
            }

            if (reattached is not null)
            {
                await BroadcastPacketToMapAsync(chr, session, V113SummonPackets.SpawnSummon(reattached, chr.Level), ct);
            }
        }

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

        // P060：對照 Java MapleGuild.memberLevelJobUpdate，扭蛋機經驗值升級跟打怪升級（P059）一樣要
        // 同步公會成員快取。
        if (result.LeveledUp)
        {
            await _guildOperationHandler.SyncMemberLevelJobAsync(
                player, (pkt, tkn) => session.SendAsync(pkt, tkn), ct);
            await OnLeveledUpAsync(player, ct);
        }
    }

    /// <summary>
    /// P167：Java <c>changeJob</c> 的封包順序：resetStatsByJob（updatePlayerStats false）→ updateSp → updateSingleStat(JOB)
    /// → MAXHP/MAXMP/HP/MP → 他人 showForeignEffect(id, 8) → silentPartyUpdate → guildUpdate → baseSkills（updateSkill）。
    /// </summary>
    private async Task SendJobChangeAsync(Player player, JobChangeResult change, MapleSession session, CancellationToken ct)
    {
        if (change.ResetStats.Count > 0)
        {
            await session.SendAsync(V113StatsPackets.UpdateStats(change.ResetStats), ct);
        }

        foreach (var update in change.SpAndJob)
        {
            await session.SendAsync(V113StatsPackets.UpdateStats([update]), ct);
        }

        await session.SendAsync(V113StatsPackets.UpdateStats(change.Vitals), ct);
        await BroadcastPacketToOthersAsync(player.Character, V113StatsPackets.ShowForeignEffect(player.Character.Id, 8), ct);
        await _partyOperationHandler.NotifySilentUpdateAsync(player, ct);
        await _guildOperationHandler.SyncMemberLevelJobAsync(player, (pkt, tkn) => session.SendAsync(pkt, tkn), ct);
        foreach (var (skillId, level, masterLevel) in change.BaseSkills)
        {
            await session.SendAsync(V113StatsPackets.UpdateSkill(skillId, level, masterLevel), ct);
        }
    }

    /// <summary>
    /// P165：Java <c>levelUp</c> 尾段：<c>map.broadcastMessage(this, showForeignEffect(id, 0), false)</c>（他人看到升級特效）→
    /// <c>silentPartyUpdate()</c>（隊伍狀態的等級）。公會同步由呼叫端處理。
    /// </summary>
    private async Task OnLeveledUpAsync(Player player, CancellationToken ct)
    {
        await BroadcastPacketToOthersAsync(player.Character, V113StatsPackets.ShowForeignEffect(player.Character.Id, 0), ct);
        await _partyOperationHandler.NotifySilentUpdateAsync(player, ct);
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
        // 對照 Java PlayerHandler.TrockAddMap：新增（VIP 與一般）都要先過 FieldLimitType.VipRock
        // 檢查，該圖設了這個旗標就整段跳過（連錯誤訊息都不送，靜默略過）；刪除不受此限制。
        var canAddRock = !FieldLimitType.VipRock.Check(_mapService.LoadMap(mapId).FieldLimit);
        var changed = false;
        if (request.IsVip)
        {
            if (request.IsDelete)
            {
                changed = player.RemoveVipRock(request.MapId);
            }
            else if (request.IsAdd && canAddRock && mapId != 180000000)
            {
                changed = player.AddVipRock(mapId);
            }
        }
        else if (request.IsDelete)
        {
            changed = player.RemoveRegularRock(request.MapId);
        }
        else if (request.IsAdd && canAddRock && mapId <= 197010000 && mapId != 180000000)
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
        await SendQuestTransactionResultAsync(result, player, session, ct);
    }

    private async Task SendQuestTransactionResultAsync(
        QuestTransactionResult result,
        Player player,
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

        // P168：外觀改變 → Java equipChanged：他人 updateCharLook、聊天室外觀同步。
        if (result.LookChanged)
        {
            await BroadcastPacketToOthersAsync(player.Character, V113MapPackets.UpdateCharLook(player), ct);
            await _messengerHandler.NotifyLookChangedAsync(player, _options.ChannelIndex + 1, ct);
        }

        // P166：cm.gainMeso／gainItem 的聊天欄提示（showMesoGain／getShowItemGain inChat）。
        if (result.MesoGained is { } mesoGained)
        {
            await session.SendAsync(V113DropPackets.ShowMesoGain(mesoGained, inChat: true), ct);
        }

        foreach (var (itemId, quantity) in result.ItemGainNotices)
        {
            await session.SendAsync(V113DropPackets.ShowItemGain(itemId, (short)quantity, inChat: true), ct);
        }

        // P163：exp／pop／sp → updatePlayerStats；gainExp(show, inChat, white) → GainEXP_Others；
        // addFame → getShowFameGain；changeSkillLevel → updateSkill。
        if (result.StatUpdates.Count > 0)
        {
            await session.SendAsync(V113StatsPackets.UpdateStats(result.StatUpdates), ct);
        }

        if (result.ExpGained != 0)
        {
            await session.SendAsync(V113BuffItemPackets.GainExpOthers(result.ExpGained, inChat: true, white: true), ct);
        }

        if (result.StatUpdates.Any(static u => u.Kind == PlayerStatKind.Level))
        {
            await _guildOperationHandler.SyncMemberLevelJobAsync(player, (pkt, tkn) => session.SendAsync(pkt, tkn), ct);
            await OnLeveledUpAsync(player, ct);
        }

        if (result.FameGained != 0)
        {
            await session.SendAsync(V113NotePackets.ShowFameGain(result.FameGained), ct);
        }

        foreach (var (skillId, level, masterLevel) in result.SkillChanges)
        {
            await session.SendAsync(V113StatsPackets.UpdateSkill(skillId, level, masterLevel), ct);
        }

        // P164：buffItemID → getItemEffect(id).applyTo(c)（不消耗道具）。
        if (result.BuffItemId is { } buffItemId)
        {
            var (self, foreign) = _useConsumableHandler.ApplyItemWithoutConsume(player, buffItemId);
            foreach (var packet in self)
            {
                await session.SendAsync(packet, ct);
            }

            foreach (var packet in foreign)
            {
                await BroadcastPacketToOthersAsync(player.Character, packet, ct);
            }
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

        // P091：對照 Java MapleInventoryManipulator.equip/unequip → chr.equipChanged() →
        // map.broadcastMessage(this, updateCharLook(this), false)（不含自己）。
        if (result.Success && result.Operation is V113InventoryMoveOperation.Equip or V113InventoryMoveOperation.Unequip)
        {
            await BroadcastPacketToOthersAsync(player.Character, V113MapPackets.UpdateCharLook(player), ct);
            await _messengerHandler.NotifyLookChangedAsync(player, _options.ChannelIndex + 1, ct); // P097：Java equipChanged → updateMessenger（1-based，P098）

            // P116：Java equip/unequip → recalcLocalStats → enforceMaxHpMp（卸下 +HP 裝備時壓回上限）。
            var enforced = player.EnforceMaxHpMp();
            if (enforced.Count > 0)
            {
                await session.SendAsync(V113StatsPackets.UpdateStats(enforced), ct);
            }
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

    private async Task HandleSpecialMoveAsync(PacketReader reader, Player player, FieldInstance? field, MapleSession session, CancellationToken ct)
    {
        V113SkillHandleResult handled;
        var hpBeforeCast = player.Hp;
        try
        {
            handled = V113SkillMoveHandler.HandleSpecialMove(reader, player, _skillService, DateTimeOffset.UtcNow);
        }
        catch (InvalidDataException)
        {
            return;
        }

        // 封包順序對照 Java SpecialMove：武陵能量歸零（P108）→ skillCooldown（套用前）→ applyTo 的 HP/MP 更新 → buff。
        if (handled.MulungEnergyPacket is not null)
        {
            await session.SendAsync(handled.MulungEnergyPacket, ct);
        }

        if (handled.CooldownPacket is not null)
        {
            await session.SendAsync(handled.CooldownPacket, ct);
        }

        if (handled.StatsPacket is not null)
        {
            await session.SendAsync(handled.StatsPacket, ct);
        }

        // P099：Java applyTo 的 setHp（HpCon / 治癒）→ updatePartyMemberHP。
        await _partyHpSync.BroadcastIfChangedAsync(player, hpBeforeCast, ct);

        // P157：騎寵施放前解除 POWERGUARD／MANA_REFLECTION（cancelEffectFromBuffStat → cancelBuff＋cancelForeignBuff）。
        if (handled.Cast?.CancelledBuffs is { } castCancelled)
        {
            foreach (var cancellation in castCancelled)
            {
                await session.SendAsync(V113SkillPackets.CancelBuff(cancellation.Stats), ct);
            }

            await ApplyBuffCancellationSideEffectsAsync(player, field, castCancelled, session, ct);
        }

        // P127：驅散／英雄意志 → dispelDebuff：自己 cancelDebuff、他人 cancelForeignDebuff。
        foreach (var cured in handled.Cast?.CuredDiseases ?? [])
        {
            await session.SendAsync(V113DiseasePackets.CancelDebuff(cured), ct);
            await BroadcastPacketToOthersAsync(player.Character, V113DiseasePackets.CancelForeignDebuff(player.Character.Id, cured), ct);
        }

        if (handled.Packet is not null)
        {
            await session.SendAsync(handled.Packet, ct);
        }

        // P139：Java applyBuffEffect 的 giveForeignBuff（同圖其他人看得到隱身／鬥氣／影分身等外觀）；
        // P147：海盜衝刺類改送 giveForeignPirate。
        if (handled.Cast is { Status: SkillCastStatus.Success, AppliedBuff: { } appliedBuff, Effect: { } buffEffect })
        {
            // P157：騎寵 → showMonsterRiding（parseMountInfo 的道具）。
            var foreignPacket = handled.Cast.Mount is { } mount
                ? V113SkillPackets.ShowMonsterRiding(player.Character.Id, mount.ForeignMountItemId, handled.SourceId)
                : ForeignBuffPacket(player.Character.Id, buffEffect, appliedBuff);
            if (foreignPacket is not null)
            {
                await BroadcastPacketToOthersAsync(player.Character, foreignPacket, ct);
            }
        }

        foreach (var resetPacket in handled.CooldownResetPackets ?? Array.Empty<byte[]>())
        {
            await session.SendAsync(resetPacket, ct);
        }

        // P081：對照 Java applyTo 在 buff 之後建立召喚獸（map.spawnSummon 廣播全圖含自己）；
        // 重複施放先移除舊召喚獸（deregisterBuffStats → removeSummon(summon, true)）。
        if (field is not null && handled.Cast is { Status: SkillCastStatus.Success, Effect: { } effect })
        {
            var position = handled.Request is { X: { } x, Y: { } y }
                ? new Position(x, y, player.Position.Stance, player.Position.Foothold)
                : player.Position;
            SummonSpawnResult? spawned;
            lock (field)
            {
                spawned = _summonService.TrySpawn(field, player, handled.SourceId, (byte)player.GetSkillLevel(handled.SourceId), effect, position);
            }

            if (spawned is not null)
            {
                if (spawned.Replaced is not null)
                {
                    await BroadcastPacketToMapAsync(player.Character, session, V113SummonPackets.RemoveSummon(spawned.Replaced, animated: true), ct);
                }

                await BroadcastPacketToMapAsync(
                    player.Character,
                    session,
                    V113SummonPackets.SpawnSummon(spawned.Summon, player.Character.Level),
                    ct);
            }
        }

        // P132：Java applyTo 的 isMist() 分支——pos 為封包座標（無則玩家位置）；面向依 SpecialMove 改寫後的 stance
        // （Java 變數 faceLeft = byte == 0 → stance &= 0xFE → isFacingLeft() 為 false，故實際面向左 = !FaceLeft）。
        if (field is not null && handled.Cast is { Status: SkillCastStatus.Success, Effect: { } mistEffect })
        {
            var mistPosition = handled.Request is { X: { } mx, Y: { } my }
                ? new Position(mx, my, player.Position.Stance, player.Position.Foothold)
                : player.Position;
            var facingLeft = handled.Request?.FaceLeft is { } faceLeftFlag ? !faceLeftFlag : player.Position.Stance % 2 != 0;
            await SpawnPlayerMistAsync(player, field, mistEffect, mistPosition, facingLeft, session, ct);

            // P145：Java applyTo primary 的 applyBuff——隊伍 buff 套到範圍內隊友（GM buff 不限隊伍）。
            await ApplyPartyBuffAsync(player, field, mistEffect, facingLeft, ct);
        }

        if (handled.Cast is { Status: SkillCastStatus.Success } && DoorService.IsMagicDoorSkill(handled.SourceId))
        {
            await OpenMagicDoorAsync(player, handled.Request, session, ct);
        }

        if (handled.Cast is { Status: not SkillCastStatus.Success } cast)
        {
            _log.LogDebug("[Channel] SPECIAL_MOVE ignored skill={SkillId} status={Status}", cast.SkillId, cast.Status);
        }
    }

    /// <summary>
    /// P087：對照 Java <c>MapleStatEffect.applyTo</c> 的 <c>isMagicDoor()</c> 分支——建門成功後依
    /// <c>MapleMap.spawnDoor</c> 送生成封包（P160：含村莊與隊伍 partyPortal）；村莊沒空位時聊天欄提示。重複施放先對舊門送 destroy data。
    /// </summary>
    private async Task OpenMagicDoorAsync(Player player, V113SpecialMoveRequest? request, MapleSession session, CancellationToken ct)
    {
        var targetMap = _mapService.LoadMap(player.Character.MapId);
        var townMap = _mapService.LoadMap(targetMap.ReturnMapId);
        var targetPosition = request is { X: { } x, Y: { } y }
            ? new Position(x, y, 0, 0)
            : player.Position;
        var opened = _doorService.TryOpenDoor(player, targetMap, townMap, targetPosition);
        if (opened.Door is not { } door)
        {
            await session.SendAsync(V113BroadcastPackets.ChatNotice("無法使用時空門，村莊不可容納。"), ct);
            return;
        }

        // P160：舊門先送 destroy data，新門照 Java MapleMap.spawnDoor 對目標地圖與村莊送生成封包。
        if (opened.Replaced is { } old)
        {
            await _buffEffects.SendDoorDestroyAsync(old, ct);
        }

        await _buffEffects.SendDoorOpenedAsync(door, ct);

        // P161：Java applyTo isMagicDoor → applyto.getParty() != null 時 silentPartyUpdate()（更新隊伍狀態的門欄位）。
        await _partyOperationHandler.NotifyDoorChangedAsync(player, ct);
    }

    private Task CloseMagicDoorAsync(int ownerId, CancellationToken ct)
        => _buffEffects.CloseMagicDoorAsync(ownerId, ct);

    private async Task HandleCancelBuffAsync(PacketReader reader, Player player, FieldInstance? field, MapleSession session, CancellationToken ct)
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

        if (handled.Cancel is { Status: CancelBuffStatus.Success } cancel)
        {
            await ApplyBuffCancellationSideEffectsAsync(player, field, cancel.Cancellations, session, ct);
        }

        if (handled.Cancel?.Status == CancelBuffStatus.ChargeSkill)
        {
            // P158：Java CancelBuffHandler 蓄力技能分支 → broadcastMessage(chr, skillCancel(chr, sourceid), false)。
            await BroadcastPacketToOthersAsync(player.Character, V113SkillPackets.CancelSkillEffect(player.Character.Id, handled.SourceId), ct);
        }
    }

    /// <summary>
    /// P083：主人離開地圖時卸下召喚獸（對照 Java <c>MapleMap.removePlayer</c>）。原地型/傀儡：取消其 buff
    /// （有 session 時送 CancelBuff 給本人）並對舊地圖其他人廣播 removeSummon；跟隨型只靜默移出並回傳，供換圖後重新出生。
    /// </summary>
    private async Task<IReadOnlyList<Summon>> DetachSummonsOnLeaveAsync(
        Player player,
        FieldInstance? field,
        int oldMapId,
        MapleSession? session,
        CancellationToken ct)
    {
        if (field is null)
        {
            return Array.Empty<Summon>();
        }

        DetachedSummons detached;
        lock (field)
        {
            detached = _summonService.DetachOwnerSummons(field, player.Character.Id);
        }

        foreach (var summon in detached.Cancelled)
        {
            foreach (var cancellation in player.CancelBuffBySource(summon.SkillId))
            {
                if (session is not null)
                {
                    await session.SendAsync(V113SkillPackets.CancelBuff(cancellation.Stats), ct);
                }
            }

            var removePacket = V113SummonPackets.RemoveSummon(summon, animated: true);
            foreach (var other in _mapRegistry.GetOthers(oldMapId, player.Character.Id))
            {
                try { await other.SendPacket(removePacket, ct); } catch { /* session 可能正在關 */ }
            }
        }

        return detached.Carried;
    }

    /// <summary>buff 結束的召喚獸/時空門副作用（P082/P088，P089 起委派 <see cref="V113BuffCancellationEffects"/>）。</summary>
    private Task ApplyBuffCancellationSideEffectsAsync(
        Player player,
        FieldInstance? field,
        IReadOnlyList<PlayerBuffCancellation> cancellations,
        MapleSession session,
        CancellationToken ct)
        => _buffEffects.ApplyAsync(player, field, cancellations, ct, (pkt, tkn) => session.SendAsync(pkt, tkn));

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

        // 對照 Java InventoryHandler.ItemPickup 的反作弊距離檢查（P033 同款「只記錄不阻擋」
        // 慣例，registerOffense 只累積違規次數）：用戶端回報座標離掉落物太遠只記錄
        // ITEMVAC_CLIENT；只有用戶端回報距離「沒有」異常時，才進一步比對伺服器已知座標，
        // 太遠則記錄 ITEMVAC_SERVER 並真的擋下（對照 Java if/else if，兩者互斥非各自獨立判斷）。
        if (field.Get(req.ObjectId) is MapDrop pickupTarget)
        {
            if (IsFarFromDropClientReported(req.ClientPosition, pickupTarget.Position))
            {
                _log.LogWarning(
                    "[Channel] ItemPickup 用戶端回報距離異常 charId={CharId} objectId={ObjectId} clientPos=({Cx},{Cy}) dropPos=({Dx},{Dy})",
                    player.Character.Id, req.ObjectId, req.ClientPosition.X, req.ClientPosition.Y, pickupTarget.Position.X, pickupTarget.Position.Y);
            }
            else if (IsFarFromDropServer(player.Position, pickupTarget.Position))
            {
                _log.LogWarning(
                    "[Channel] ItemPickup 伺服器已知距離異常 charId={CharId} objectId={ObjectId} charPos=({Cx},{Cy}) dropPos=({Dx},{Dy})",
                    player.Character.Id, req.ObjectId, player.Position.X, player.Position.Y, pickupTarget.Position.X, pickupTarget.Position.Y);
                await session.SendAsync(V113StatsPackets.EnableActions(), ct);
                return;
            }
        }

        DropPickupResult result;
        lock (field)
        {
            result = _dropService.TryPickup(field, player, req.ObjectId);
        }

        if (result.Status == DropPickupStatus.PickupRestricted)
        {
            await session.SendAsync(V113DropPackets.InventoryFull(), ct);
            await session.SendAsync(V113DropPackets.ShowItemUnavailable(), ct);
            return;
        }

        if (!result.Success || result.Drop is null)
        {
            // P181：背包滿只撿一部分（Java addFromDrop 已送的堆疊更新／新格），掉落物留在地上。
            if (result.Stack is { AnyChange: true } partial && result.InventoryType is { } partialType)
            {
                await SendStackGainAsync(session, partialType, partial, ct);
            }

            return;
        }

        await BroadcastPacketToMapAsync(
            player.Character,
            session,
            V113DropPackets.RemoveItemFromMap(result.Drop.ObjectId, animation: 2, characterId: player.Character.Id),
            ct);

        if (result.Stack is { } stack && result.InventoryType is { } type && result.Drop.Item is { } pickedItem)
        {
            await SendStackGainAsync(session, type, stack, ct);
            await session.SendAsync(V113DropPackets.ShowItemGain(pickedItem.ItemId, result.PickedQuantity), ct);
        }
        else if (result.GainedMeso > 0)
        {
            await session.SendAsync(V113DropPackets.UpdateMeso(player.Character.Meso), ct);
            await session.SendAsync(V113DropPackets.ShowMesoGain(result.GainedMeso), ct);
        }
    }

    /// <summary>
    /// P182：反應堆腳本（rm）效果的封包：dropItems 的掉落物全圖 DROP_ITEM_FROM_MAPOBJECT、mapMessage 全圖／playerMessage 本人
    /// serverNotice、gainItem 的背包更新與 getShowItemGain(id, qty, true)。
    /// </summary>
    private async Task SendReactorScriptEffectsAsync(Maple.Application.Reactors.ReactorScriptContext? ctx, Player player, MapleSession session, CancellationToken ct)
    {
        if (ctx is null)
        {
            return;
        }

        foreach (var drop in ctx.SpawnedDrops)
        {
            await BroadcastPacketToMapAsync(player.Character, session, V113DropPackets.DropItemFromMapObject(drop), ct);
        }

        foreach (var mob in ctx.SpawnedMobs)
        {
            await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.SpawnMonster(mob), ct); // P183
        }

        foreach (var (objectId, animation) in ctx.KilledMobs)
        {
            await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.KillMonster(objectId, animation), ct);
        }

        // P185：rm.getMap(id).setReactorState／resetReactors——field 鎖外套用（可能是別張圖）。
        foreach (var (mapId, state) in ctx.ReactorStateChanges)
        {
            RunEventHook(() => _eventWorld?.SetReactorState(mapId, state));
        }

        foreach (var song in ctx.Music)
        {
            await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.EnvironmentChange(song, 6), ct);
        }

        foreach (var (mapWide, type, message) in ctx.Notices)
        {
            var packet = V113BroadcastPackets.ServerNotice(type, message);
            if (mapWide)
            {
                await BroadcastPacketToMapAsync(player.Character, session, packet, ct);
            }
            else
            {
                await session.SendAsync(packet, ct);
            }
        }

        foreach (var gain in ctx.InventoryGains)
        {
            var type = gain.Added.Count > 0 ? Player.InventoryTypeOf(gain.Added[0].ItemId) : gain.Merged[0].Type;
            await SendStackGainAsync(session, type, gain, ct);
        }

        foreach (var removal in ctx.InventoryRemovals)
        {
            await session.SendAsync(V113ItemUsePackets.ModifyInventoryQuantity(removal), ct);
        }

        foreach (var (itemId, quantity) in ctx.ItemGainNotices)
        {
            await session.SendAsync(V113DropPackets.ShowItemGain(itemId, (short)quantity, inChat: true), ct);
        }

        // P206：showEffect → environmentChange(effect, 3)。
        foreach (var (broadcast, effect) in ctx.Effects)
        {
            var packet = V113CombatPackets.EnvironmentChange(effect, 3);
            if (broadcast)
            {
                await BroadcastPacketToMapAsync(player.Character, session, packet, ct);
            }
            else
            {
                await session.SendAsync(packet, ct);
            }
        }

        if (ctx.PendingGuildPoints != 0)
        {
            await ApplyGuildPointsAsync(player.Character.GuildId, ctx.PendingGuildPoints, ct);
        }

        // P207：givePartyExp／forceCompleteQuest 與 NPC 腳本同屬 AbstractPlayerInteraction——借 NPC 情境套用
        // （倍率、升級封包、同圖隊員分派、任務完成封包）。
        if (ctx.PendingPartyExp.Count > 0 || ctx.PendingForceCompletedQuests.Count > 0)
        {
            var npcCtx = NewNpcContext(0, player);
            var script = new DelegateNpcScript(() =>
            {
                foreach (var amount in ctx.PendingPartyExp)
                {
                    npcCtx.GivePartyExp(amount);
                }

                foreach (var questId in ctx.PendingForceCompletedQuests)
                {
                    npcCtx.ForceCompleteQuest(questId);
                }

                npcCtx.Dispose();
            });
            var convo = new NpcConversation(
                0, script, npcCtx,
                sendDialog: (_, _) => Task.CompletedTask,
                warp: (_, _) => Task.CompletedTask,
                sendQuestResult: (result, c) => SendQuestTransactionResultAsync(result, player, session, c),
                sendPartyAction: SendPartyActionAsync);
            await convo.StartAsync(ct);
        }
    }

    /// <summary>P196/P206：World.Guild.gainGP——加點後對線上成員送 UPDATE_GP。</summary>
    private async Task ApplyGuildPointsAsync(int guildId, int amount, CancellationToken ct)
    {
        var gp = await _guildService.GainGuildPointsAsync(guildId, amount, ct);
        if (gp.Succeeded && gp.Guild is { } guild)
        {
            var gpPacket = V113GuildPackets.UpdateGuildPoints(guild.Id, guild.GuildPoints);
            foreach (var recipientId in gp.Recipients.Distinct())
            {
                if (_onlinePlayers.FindById(recipientId) is { } member)
                {
                    try { await member.SendPacket(gpPacket, ct); } catch { /* best-effort */ }
                }
            }
        }
    }

    /// <summary>
    /// P193：NPC 腳本的跨玩家／地圖效果：全服轉蛋公告、生怪（事件世界的 spawnMonsterOnGroundBelow 並廣播）、
    /// warpMap（目前地圖每位玩家排進各自連線換圖）。
    /// </summary>
    private async Task ApplyNpcWorldEffectsAsync(NpcContext ctx, Player player, CancellationToken ct)
    {
        foreach (var (message, item) in ctx.PendingGachaponMegas)
        {
            await BroadcastPacketToAllOnlineAsync(V113BroadcastPackets.GachaponMega(message, _options.ChannelIndex + 1, item), ct);
        }

        foreach (var (mapId, monsterId, quantity, x, y) in ctx.PendingMobSpawns)
        {
            for (var i = 0; i < quantity; i++)
            {
                RunEventHook(() => _eventWorld?.SpawnMonster(mapId, monsterId, x, y));
            }
        }

        // P200：cancelItem——CANCEL_BUFF 與連帶效果。
        if (ctx.PendingBuffCancellations.Count > 0 && _onlinePlayers.FindById(player.Character.Id) is { } canceller)
        {
            foreach (var cancellation in ctx.PendingBuffCancellations)
            {
                await canceller.SendPacket(V113SkillPackets.CancelBuff(cancellation.Stats), ct);
            }

            if (_remotePlayers.TryGetValue(player.Character.Id, out var cancellerEntry) && _fieldRegistry.GetOrCreate(player.Character.MapId, out _) is { } cancelField)
            {
                await ApplyBuffCancellationSideEffectsAsync(player, cancelField, ctx.PendingBuffCancellations, cancellerEntry.Session, ct);
            }
        }

        // P199：resetMap（resetFully：清怪重生）。
        foreach (var resetMapId in ctx.PendingMapResets)
        {
            RunEventHook(() => _eventWorld?.ResetMap(resetMapId));
        }

        // P196：genericGuildMessage／gainGP（寫入公會，通知線上成員 updateGP）。
        foreach (var code in ctx.PendingGuildMessages)
        {
            if (_onlinePlayers.FindById(player.Character.Id) is { } me)
            {
                await me.SendPacket(V113GuildPackets.GenericGuildMessage(code), ct);
            }
        }

        if (ctx.PendingGuildPoints != 0)
        {
            await ApplyGuildPointsAsync(player.Character.GuildId, ctx.PendingGuildPoints, ct);
        }

        // P195：warpBack——getClock(秒)，到時排進自己的連線：stopClock、回 retmap、黃字提示。
        if (ctx.PendingClock is { } clockSeconds && _onlinePlayers.FindById(player.Character.Id) is { } self)
        {
            await self.SendPacket(V113TransportEffects.Clock(clockSeconds), ct);
        }

        if (ctx.PendingWarpBack is { } back && _sessionCommands is { } commands)
        {
            var characterId = player.Character.Id;
            var online = _onlinePlayers;
            _ = Task.Delay(TimeSpan.FromSeconds(back.Seconds)).ContinueWith(async _ =>
            {
                if (online.FindById(characterId) is not { } target)
                {
                    return;
                }

                await target.SendPacket(V113TransportEffects.StopClock(), CancellationToken.None);
                commands.Warp(characterId, back.ReturnMapId, 0);
                await target.SendPacket(V113BroadcastPackets.ServerNotice(6, "已經到達目的地了!"), CancellationToken.None);
            }, TaskScheduler.Default);
        }

        if (ctx.PendingMapWarp is { } warp && _sessionCommands is not null)
        {
            foreach (var entry in _mapRegistry.GetAll(player.Character.MapId).ToArray())
            {
                _sessionCommands.Warp(entry.CharId, warp.MapId, warp.Portal);
            }
        }
    }

    /// <summary>P181：疊加取得的背包封包：補到既有堆疊 → 數量更新（mode 1），新格 → 新增（mode 0）。</summary>
    private static async Task SendStackGainAsync(MapleSession session, Maple.Core.Inventory.InventoryType type, Maple.Core.Inventory.InventoryStackGain stack, CancellationToken ct)
    {
        foreach (var merged in stack.Merged)
        {
            await session.SendAsync(V113ItemUsePackets.ModifyInventoryQuantity(merged), ct);
        }

        foreach (var added in stack.Added)
        {
            await session.SendAsync(V113DropPackets.ModifyInventoryAdd(type, added), ct);
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
                if (result.Status == ShopTransactionStatus.Success && result.Stack is { } stack)
                {
                    await SendStackGainAsync(session, Player.InventoryTypeOf(req.ItemId), stack, ct);
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

        // P210：取出——Java gainMeso(-1000) 的楓幣更新 → addFromDrop 的背包封包 → sendTakenOut；失敗時 dropMessage(1)。
        if (result.PopupMessage is { } popup)
        {
            await session.SendAsync(V113BroadcastPackets.ServerNotice(1, popup), ct);
        }

        if (result.MesoChanged)
        {
            await session.SendAsync(V113ShopPackets.UpdateMeso(player.Character.Meso, itemReaction: true), ct);
        }

        if (result.Gained is { } gained && result.ChangedType is { } gainedType)
        {
            await SendStackGainAsync(session, gainedType, gained, ct);
        }

        // P211：存入——gainMeso(-cost) 後 removeFromSlot 的背包封包，再 sendStored。
        if (result.Removed is { } removed)
        {
            await session.SendAsync(V113ItemUsePackets.ModifyInventoryQuantity(removed), ct);
        }

        if (result.EnableActions)
        {
            await session.SendAsync(V113StatsPackets.EnableActions(), ct);
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
        // 對照 Java PlayerHandler.closeRangeAttack：死亡玩家的攻擊在解析封包前就整段擋下
        // （registerOffense(ATTACKING_WHILE_DEAD) + return），不像現在這樣仍會把攻擊動作廣播
        // 給同圖其他玩家看見（ApplyAttack 內部雖然已經擋傷害，但廣播發生在呼叫它之前）。
        if (!player.IsAlive)
        {
            _log.LogWarning("[Channel] 玩家 {Name} 死亡狀態下嘗試近戰攻擊", player.Character.Name);
            return;
        }

        V113CloseRangeAttack attack;
        try
        {
            attack = V113CombatPackets.ParseCloseRangeAttack(reader);
        }
        catch (InvalidDataException ex)
        {
            _log.LogDebug("[Channel] CLOSE_RANGE_ATTACK parse failed: {Message}", ex.Message);
            return;
        }

        // 對照 Java PlayerHandler.closeRangeAttack：技能種類跟宣稱的攻擊封包分類不符
        // （registerOffense(ATTACK_TYPE_ERROR) + return，整包丟棄，不處理傷害也不廣播）。
        if (!IsCloseRangedAttackSkill(attack.SkillId))
        {
            _log.LogWarning(
                "[Channel] 玩家 {Name} 近戰攻擊技能種類不符 skill={Skill}", player.Character.Name, attack.SkillId);
            return;
        }

        // P077：對照 Java closeRangeAttack 的 attack.skill != 0 → getAttackEffect == null → return（未學技能）。
        if (attack.SkillId != 0 && !EnsureAttackSkillLearned(player, attack.SkillId, "近戰"))
        {
            return;
        }

        if (!await CheckAttackCooldownAsync(player, attack.SkillId, session, ct))
        {
            return;
        }

        var attackBroadcast = V113CombatPackets.CloseRangeAttackBroadcast(player.Character.Id, attack, player.Character.Level);
        await BroadcastPacketToOthersAsync(player.Character, attackBroadcast, ct);

        if (!await CheckMulungAttackAsync(player, attack.SkillId, requireFullEnergy: true, session, ct))
        {
            return;
        }

        CombatAttackResult result;
        lock (field)
        {
            result = _combatService.ApplyAttack(
                field,
                player,
                attack.ToCombatAttack() with { SkillEffect = _skillService.GetAttackEffect(player, attack.SkillId), SkillId = attack.SkillId });
        }

        await CancelBuffsOnAttackAsync(player, field, result, session, ct);

        if (_log.IsEnabled(LogLevel.Debug))
        {
            var combat = attack.ToCombatAttack();
            _log.LogDebug(
                "[Channel] CLOSE_RANGE_ATTACK skill={Skill} targets=[{Targets}] hits=[{Hits}]",
                attack.SkillId,
                string.Join(",", combat.Targets.Select(t => $"{t.ObjectId}:{t.TotalDamage}")),
                string.Join(",", result.Hits.Select(h => $"{h.ObjectId}:{h.AppliedDamage}/hp{h.RemainingHp}{(h.Killed ? ":killed" : "")}")));
        }

        await SendCombatHitsAsync(result.Hits, player, session, ct);
        await ApplyReflectedDamageAsync(result.Hits, player, field, session, ct);

        // P133：Java applyAttack 尾段 applyTo（近戰解析器未保留座標，霧位置用玩家位置）；P138：中止時不執行。
        if (!result.Aborted)
        {
            await ApplyAttackSkillEffectAsync(player, field, attack.SkillId, attack.Targets, isMagic: false, player.Position, session, ct);
        }
    }

    private async Task HandleRangedAttackAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        // 對照 Java PlayerHandler.rangedAttack：死亡玩家整段擋下（見 HandleCloseRangeAttackAsync
        // 註解）；RangedMagicCombatService.ApplyRangedAttack 內部已有同等保護（AttackerDead 狀態），
        // 這裡提前擋是為了避免不必要的封包解析與跟另外兩種攻擊路徑保持一致的判斷順序。
        if (!player.IsAlive)
        {
            _log.LogWarning("[Channel] 玩家 {Name} 死亡狀態下嘗試遠程攻擊", player.Character.Name);
            return;
        }

        V113RangedAttack attack;
        try
        {
            attack = V113RangedMagicAttackPackets.ParseRangedAttack(reader);
        }
        catch (InvalidDataException)
        {
            return;
        }

        // 對照 Java PlayerHandler.rangedAttack：技能種類跟宣稱的攻擊封包分類不符（見
        // HandleCloseRangeAttackAsync 註解）。
        if (!IsRangedAttackSkill(attack.SkillId))
        {
            _log.LogWarning(
                "[Channel] 玩家 {Name} 遠程攻擊技能種類不符 skill={Skill}", player.Character.Name, attack.SkillId);
            return;
        }

        // P077：對照 Java rangedAttack 的 attack.skill != 0 → getAttackEffect == null → return（未學技能）。
        if (attack.SkillId != 0 && !EnsureAttackSkillLearned(player, attack.SkillId, "遠程"))
        {
            return;
        }

        if (!await CheckAttackCooldownAsync(player, attack.SkillId, session, ct))
        {
            return;
        }

        if (!await CheckMulungAttackAsync(player, attack.SkillId, requireFullEnergy: true, session, ct))
        {
            return;
        }

        RangedAttackApplyResult result;
        lock (field)
        {
            var rangedAttack = attack.ToCombatRangedAttack();
            result = _rangedMagicCombatService.ApplyRangedAttack(
                field,
                player,
                rangedAttack with
                {
                    Attack = rangedAttack.Attack with { SkillEffect = _skillService.GetAttackEffect(player, attack.SkillId), SkillId = attack.SkillId },
                },
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

        await CancelBuffsOnAttackAsync(player, field, result.Combat, session, ct);
        await SendCombatHitsAsync(result.Combat.Hits, player, session, ct);
        await ApplyReflectedDamageAsync(result.Combat.Hits, player, field, session, ct);

        // P132/P133：Java applyAttack 尾段 effect.applyTo(player, skillposition ?? position)——HP/MP 消耗、毒炸彈 14111006 毒霧。
        // MapleForge 解析器只保留 attack.position（skillposition 未解析），先以其為準。P138：中止時不執行。
        if (result.Combat.Aborted)
        {
            return;
        }

        await ApplyAttackSkillEffectAsync(
            player,
            field,
            attack.SkillId,
            attack.Targets,
            isMagic: false,
            new Position(attack.Position.X, attack.Position.Y, player.Position.Stance, player.Position.Foothold),
            session,
            ct);
    }

    private async Task HandleMagicAttackAsync(
        PacketReader reader,
        Player player,
        FieldInstance field,
        MapleSession session,
        CancellationToken ct)
    {
        // 對照 Java PlayerHandler.MagicDamage：死亡玩家整段擋下（見 HandleCloseRangeAttackAsync 註解）。
        if (!player.IsAlive)
        {
            _log.LogWarning("[Channel] 玩家 {Name} 死亡狀態下嘗試魔法攻擊", player.Character.Name);
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

        // 對照 Java PlayerHandler.MagicDamage：技能種類跟宣稱的攻擊封包分類不符（見
        // HandleCloseRangeAttackAsync 註解）。
        if (!IsMagicAttackSkill(attack.SkillId))
        {
            _log.LogWarning(
                "[Channel] 玩家 {Name} 魔法攻擊技能種類不符 skill={Skill}", player.Character.Name, attack.SkillId);
            return;
        }

        // P077：對照 Java MagicDamage 的 getAttackEffect == null → return（未學技能；魔法攻擊不略過技能 0）。
        if (!EnsureAttackSkillLearned(player, attack.SkillId, "魔法"))
        {
            return;
        }

        if (!await CheckAttackCooldownAsync(player, attack.SkillId, session, ct))
        {
            return;
        }

        if (!await CheckMulungAttackAsync(player, attack.SkillId, requireFullEnergy: false, session, ct))
        {
            return;
        }

        CombatAttackResult result;
        lock (field)
        {
            result = _rangedMagicCombatService.ApplyMagicAttack(
                field,
                player,
                attack.ToCombatAttack() with
                {
                    SkillEffect = _skillService.GetAttackEffect(player, attack.SkillId),
                    SkillId = attack.SkillId,
                    IsMagic = true,
                });
        }

        var attackBroadcast = V113RangedMagicAttackPackets.MagicAttackBroadcast(
            player.Character.Id,
            attack,
            player.Character.Level,
            GetSkillLevelByte(player, attack.SkillId));
        await BroadcastPacketToOthersAsync(player.Character, attackBroadcast, ct);

        await SendCombatHitsAsync(result.Hits, player, session, ct);
        await ApplyReflectedDamageAsync(result.Hits, player, field, session, ct);

        // P132/P133：Java applyAttackMagic 尾段 effect.applyTo(player)（pos = null → 玩家位置）——HP/MP 消耗、
        // 劇毒霧 2111003／火焰齒輪 12111005。
        // P137/P138：傷害封鎖門檻或射程中止時 Java 已 return，尾段 applyTo 不執行。
        if (!result.Aborted)
        {
            await ApplyAttackSkillEffectAsync(player, field, attack.SkillId, attack.Targets, isMagic: true, player.Position, session, ct);
        }
    }

    /// <summary>
    /// P141：物理攻擊處理到射程內怪物時（有任何命中紀錄）解除疾風步／隱身術：本人 CANCEL_BUFF，
    /// 其餘（CANCEL_FOREIGN_BUFF 等）走取消副作用。
    /// </summary>
    private async Task CancelBuffsOnAttackAsync(Player player, FieldInstance field, CombatAttackResult result, MapleSession session, CancellationToken ct)
    {
        if (result.Hits.Count == 0)
        {
            return;
        }

        var cancellations = _skillService.CancelBuffsOnAttack(player);
        foreach (var cancellation in cancellations)
        {
            await session.SendAsync(V113SkillPackets.CancelBuff(cancellation.Stats), ct);
        }

        await ApplyBuffCancellationSideEffectsAsync(player, field, cancellations, session, ct);
    }

    /// <summary>
    /// P133：攻擊尾段的 <c>effect.applyTo(player, pos)</c>——HP/MP 結算（不足時 Java return false：不送封包、不生霧）→
    /// <c>updatePlayerStats(hpmpupdate, true)</c>（MP 有變才帶、HP 一律帶）→ 隊伍 HP → 霧（P132）。
    /// P159：之後 applyBuffEffect（overTime buff、歸巢）。
    /// </summary>
    private async Task ApplyAttackSkillEffectAsync(
        Player player,
        FieldInstance field,
        int skillId,
        int targetCount,
        bool isMagic,
        Position position,
        MapleSession session,
        CancellationToken ct)
    {
        var hpBefore = player.Hp;
        if (_skillService.ApplyAttackSkillCost(player, skillId, targetCount, isMagic) is not { Applied: true } cost)
        {
            return;
        }

        await session.SendAsync(V113StatsPackets.UpdateStats(cost.Updates, itemReaction: true), ct);
        await _partyHpSync.BroadcastIfChangedAsync(player, hpBefore, ct);

        if (_skillService.GetAttackEffect(player, skillId) is { } effect)
        {
            await SpawnPlayerMistAsync(player, field, effect, position, player.Position.Stance % 2 != 0, session, ct);
        }

        // P159：applyBuffEffect——一般 giveBuff＋giveForeignBuff；指定攻擊／精準砲擊改送 cancelHoming＋giveHoming（不廣播）。
        if (_skillService.ApplyAttackSkillBuff(player, skillId, DateTimeOffset.UtcNow) is { } attackBuff)
        {
            if (attackBuff.HomingMobObjectId is { } mobObjectId)
            {
                await session.SendAsync(V113SkillPackets.CancelHoming(), ct);
                await session.SendAsync(V113SkillPackets.GiveHoming(attackBuff.Effect.SourceId, mobObjectId), ct);
            }
            else
            {
                await session.SendAsync(V113SkillMoveHandler.BuildGiveBuff(attackBuff.Effect.SourceId, attackBuff.Buff, attackBuff.Effect), ct);
                if (ForeignBuffPacket(player.Character.Id, attackBuff.Effect, attackBuff.Buff) is { } foreignPacket)
                {
                    await BroadcastPacketToOthersAsync(player.Character, foreignPacket, ct);
                }
            }
        }
    }

    /// <summary>
    /// P145：隊伍 buff 封包（對照 Java applyBuff 對每位隊友：applyTo(primary=false) 的 updatePlayerStats(hpmpupdate, true)、
    /// 驅散的 cancelDebuff、applyBuffEffect 的 giveBuff＋giveForeignBuff → showOwnBuffEffect(sid, 2) → 他人 showBuffeffect
    /// （變身中不送）→ 時空置換 skillCooldown(id, 0)），並同步隊伍 HP。
    /// </summary>
    private async Task ApplyPartyBuffAsync(Player caster, FieldInstance field, MapleStatEffect effect, bool facingLeft, CancellationToken ct)
    {
        if (_partyBuffs is null)
        {
            return;
        }

        IReadOnlyList<PartyBuffApplication> applied;
        lock (field)
        {
            applied = _partyBuffs.Apply(field, caster, effect, facingLeft, DateTimeOffset.UtcNow);
        }

        foreach (var application in applied)
        {
            var target = application.Target;
            var entry = _mapRegistry.GetAll(target.Character.MapId).FirstOrDefault(e => e.CharId == target.Character.Id);
            if (entry is null)
            {
                continue;
            }

            var others = _mapRegistry.GetOthers(target.Character.MapId, target.Character.Id);
            if (application.StatUpdates.Count > 0)
            {
                await SendBestEffortAsync(entry, V113StatsPackets.UpdateStats(application.StatUpdates, itemReaction: true), ct);
            }

            foreach (var cured in application.CuredDiseases)
            {
                await SendBestEffortAsync(entry, V113DiseasePackets.CancelDebuff(cured), ct);
                await SendAllBestEffortAsync(others, V113DiseasePackets.CancelForeignDebuff(target.Character.Id, cured), ct);
            }

            if (application.Buff is { } buff)
            {
                await SendBestEffortAsync(entry, V113SkillMoveHandler.BuildGiveBuff(effect.SourceId, buff, effect), ct);
                if (ForeignBuffPacket(target.Character.Id, effect, buff) is { } foreignPacket)
                {
                    await SendAllBestEffortAsync(others, foreignPacket, ct);
                }
            }

            if (application.StatUpdates.Count > 0)
            {
                await SendBestEffortAsync(entry, V113SkillPackets.ShowOwnBuffEffect(effect.SourceId, 2), ct);
                if (target.GetBuffValue(MapleBuffStat.MORPH) is null)
                {
                    await SendAllBestEffortAsync(others, V113SkillPackets.ShowForeignBuffEffect(target.Character.Id, effect.SourceId, 2), ct);
                }
            }

            foreach (var skillId in application.ResetCooldownSkillIds)
            {
                await SendBestEffortAsync(entry, V113SkillPackets.SkillCooldown(skillId, 0), ct);
            }

            await _partyHpSync.BroadcastIfChangedAsync(target, application.HpBefore, ct);
        }
    }

    /// <summary>P139/P147：他人可見的 buff 封包——海盜衝刺類 giveForeignPirate，其餘依 <see cref="SkillService.GetForeignBuffStats"/>。</summary>
    private static byte[]? ForeignBuffPacket(int characterId, MapleStatEffect effect, PlayerBuffChange buff)
        => SkillService.IsPirateBuff(effect)
            ? V113SkillPackets.GiveForeignPirate(buff.Stats, buff.DurationMilliseconds / 1000, characterId, effect.SourceId)
            // P156：變身 → giveForeignBuff([MORPH, getMorph(chr)])，isMorph（WZ morph > 0）時多寫 1 byte。
            : SkillService.IsMorphBuff(effect) && buff.Stats.FirstOrDefault(static s => s.Stat == MapleBuffStat.MORPH) is { Stat: MapleBuffStat.MORPH } morph
                ? V113BuffItemPackets.GiveForeignBuff(characterId, [morph], isMorph: effect.MorphId > 0)
                : SkillService.GetForeignBuffStats(effect) is { } foreign
                    ? V113BuffItemPackets.GiveForeignBuff(characterId, foreign, isMorph: false)
                    : null;

    private static async Task SendBestEffortAsync(MapPlayerEntry entry, byte[] packet, CancellationToken ct)
    {
        try
        {
            await entry.SendPacket(packet, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // best-effort：對方 session 可能正在關閉。
        }
    }

    private static async Task SendAllBestEffortAsync(IEnumerable<MapPlayerEntry> entries, byte[] packet, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            await SendBestEffortAsync(entry, packet, ct);
        }
    }

    /// <summary>
    /// P132：Java <c>MapleStatEffect.applyTo</c> 的 <c>isMist()</c> → <c>map.spawnMist</c>（spawnAndAddRangedMapObject：全圖 SPAWN_MIST）。
    /// 非霧技能直接略過。
    /// </summary>
    private async Task SpawnPlayerMistAsync(
        Player player,
        FieldInstance field,
        MapleStatEffect effect,
        Position position,
        bool facingLeft,
        MapleSession session,
        CancellationToken ct)
    {
        if (_playerMists is null || !PlayerMistService.IsMistSkill(effect.SourceId))
        {
            return;
        }

        Mist? mist;
        lock (field)
        {
            mist = _playerMists.TrySpawn(field, player, effect, position, facingLeft, DateTimeOffset.UtcNow);
        }

        if (mist is not null)
        {
            await BroadcastPacketToMapAsync(player.Character, session, V113MistPackets.SpawnMist(mist), ct);
        }
    }

    /// <summary>
    /// 對照 Java 三個攻擊 handler 共用的冷卻區塊（在技能種類檢查之後、廣播/傷害/消耗之前）：
    /// 冷卻中送 enableActions 並丟棄整次攻擊（回 false）；登記冷卻則送 COOLDOWN 封包。
    /// </summary>
    /// <summary>P077：未學該攻擊技能時記錄並回 false（呼叫端丟棄整次攻擊，Java 不回任何封包）。</summary>
    private bool EnsureAttackSkillLearned(Player player, int skillId, string kind)
    {
        if (SkillService.HasAttackSkillLevel(player, skillId))
        {
            return true;
        }

        _log.LogWarning("[Channel] 玩家 {Name} 使用未學習的{Kind}攻擊技能 skill={Skill}", player.Character.Name, kind, skillId);
        return false;
    }

    /// <summary>
    /// P108：武陵技能攻擊的能量判斷（對照 Java <c>DamageParse.applyAttack</c>／<c>applyAttackMagic</c>）：
    /// 被擋回 false（整次攻擊丟棄、不送封包）；通過時能量歸零並送能量封包。
    /// </summary>
    private async Task<bool> CheckMulungAttackAsync(
        Player player,
        int skillId,
        bool requireFullEnergy,
        MapleSession session,
        CancellationToken ct)
    {
        switch (SkillService.CheckMulungAttack(player, skillId, requireFullEnergy))
        {
            case MulungAttackGate.Blocked:
                _log.LogDebug("[Channel] 武陵技能攻擊被擋 skill={SkillId} energy={Energy}", skillId, player.MulungEnergy);
                return false;
            case MulungAttackGate.Consumed:
                await session.SendAsync(V113SkillPackets.MulungEnergy(player.MulungEnergy), ct);
                return true;
            default:
                return true;
        }
    }

    private async Task<bool> CheckAttackCooldownAsync(Player player, int skillId, MapleSession session, CancellationToken ct)
    {
        var (blocked, packet) = V113SkillMoveHandler.HandleAttackCooldown(player, skillId, _skillService, DateTimeOffset.UtcNow);
        if (packet is not null)
        {
            await session.SendAsync(packet, ct);
        }

        if (blocked)
        {
            _log.LogDebug("[Channel] 攻擊技能冷卻中，丟棄攻擊 skill={SkillId}", skillId);
        }

        return !blocked;
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

        var wasAlive = player.IsAlive;
        var applied = request.Damage > 0 ? player.TakeDamage(request.Damage) : (short)0;
        await OnPlayerHpLostAsync(player, field, session, wasAlive, applied, ct);

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
            if (hit.HpDisplay is { } display)
            {
                await SendMobHpDisplayAsync(hit, display, player, session, ct);
            }

            // P108：Java damage() 的血條分派後 mulungEnergyModify(true) 送能量封包。
            if (hit.MulungEnergyAfter is { } energy)
            {
                await session.SendAsync(V113SkillPackets.MulungEnergy(energy), ct);
            }

            // P111：Java applyStatus → applyMonsterStatus（廣播全圖，含控制者）。
            if (hit.AppliedStatuses is { } statuses)
            {
                foreach (var status in statuses)
                {
                    await BroadcastPacketToMapAsync(
                        player.Character,
                        session,
                        V113MonsterStatusCodec.ApplyMonsterStatus(hit.ObjectId, [status]),
                        ct);
                }
            }

            // P144：偷錢 → spawnMesoDrop（全圖 dropItemFromMapObject，動畫 1）。
            foreach (var drop in hit.PickPocketDrops ?? [])
            {
                await BroadcastPacketToMapAsync(player.Character, session, V113DropPackets.DropItemFromMapObject(drop), ct);
            }

            // P143：吸魔成功 → showOwnBuffEffect(skill, 1)；未變身時對他人 showBuffeffect(id, skill, 1)。
            if (hit.MpEaterSkillId != 0)
            {
                await session.SendAsync(V113SkillPackets.ShowOwnBuffEffect(hit.MpEaterSkillId, 1), ct);
                if (player.GetBuffValue(MapleBuffStat.MORPH) is null)
                {
                    await BroadcastPacketToOthersAsync(
                        player.Character,
                        V113SkillPackets.ShowForeignBuffEffect(player.Character.Id, hit.MpEaterSkillId, 1),
                        ct);
                }
            }

            if (hit.Killed)
            {
                await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.KillMonster(hit.ObjectId), ct);
                await NotifyControllerStoppedAsync(hit.ControllerId, hit.ObjectId, player, session, ct);
                NotifyEventMonsterKilled(player, hit);

                // P148：spawnRevives → 全圖 SPAWN_MONSTER(-2, link = 死亡 oid)，攻擊者接手控制。
                foreach (var revived in hit.Revived ?? [])
                {
                    await BroadcastPacketToMapAsync(
                        player.Character,
                        session,
                        V113CombatPackets.SpawnMonster(revived, spawnType: -2, link: revived.LinkObjectId),
                        ct);
                    await session.SendAsync(V113CombatPackets.SpawnMonsterControl(revived, newSpawn: true, aggro: false), ct);
                }

                if (hit.Rewards is { } rewards)
                {
                    await SendMobKillRewardsAsync(player, session, rewards, ct);
                }
            }
        }
    }

    /// <summary>
    /// 玩家扣血後的共用收尾（對照 Java <c>PlayerStats.setHp</c> → 死亡分支 <c>playerDead</c>、<c>updateSingleStat(HP)</c>、
    /// <c>updatePartyMemberHP</c>）：P076 死亡序列、P090 buff 連帶效果、HP 更新、P095 隊伍血條。TAKE_DAMAGE 與 P124 反射共用。
    /// </summary>
    private async Task OnPlayerHpLostAsync(Player player, FieldInstance field, MapleSession session, bool wasAlive, short applied, CancellationToken ct)
    {
        if (wasAlive && !player.IsAlive)
        {
            var outcome = _playerDeaths.OnPlayerDied(player);
            foreach (var packet in V113DeathPackets.Build(player, outcome))
            {
                await session.SendAsync(packet, ct);
            }

            if (outcome.SoulStoneRevived)
            {
                // P205：Java changeMap(getMap(), getPortal(0))——排進自己的連線換圖。
                _sessionCommands?.Warp(player.Character.Id, player.Character.MapId, 0);
            }
            else
            {
                RunEventHook(() => _eventScripts?.OnPlayerDead(player.Character.Id)); // P179：Java playerDead → eim.playerKilled
            }

            await _buffEffects.ApplyAsync(player, field, outcome.CancelledBuffs, ct, (pkt, tkn) => session.SendAsync(pkt, tkn));
        }

        if (applied > 0)
        {
            await session.SendAsync(
                V113StatsPackets.UpdateStats(new[]
                {
                    new PlayerStatUpdate(PlayerStatKind.Hp, player.Hp),
                }),
                ct);
            await _partyHpSync.BroadcastAsync(player, ct); // P095：Java setHp → updatePartyMemberHP
        }
    }

    /// <summary>
    /// P124：命中帶反射狀態的怪物後的收尾（扣血已在 <c>CombatService</c> 依序套用）。Java 每隻怪各 addHP 一次、各送一次 HP；
    /// MapleForge 合併成一次（死亡判斷以反射前 HP 是否 &gt; 0 為準），封包數不同、最終狀態相同。
    /// </summary>
    private async Task ApplyReflectedDamageAsync(IReadOnlyList<CombatMobHit> hits, Player player, FieldInstance field, MapleSession session, CancellationToken ct)
    {
        var reflected = hits.Sum(static h => h.ReflectedDamage);
        if (reflected <= 0)
        {
            return;
        }

        var wasAlive = player.Hp + reflected > 0;
        await OnPlayerHpLostAsync(player, field, session, wasAlive, (short)Math.Min(short.MaxValue, reflected), ct);
    }

    /// <summary>P107：命中後的血條顯示（分派規則見 <see cref="V113MobHpDisplayPackets"/>）。</summary>
    private async Task SendMobHpDisplayAsync(
        CombatMobHit hit,
        CombatMobHpDisplay display,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        foreach (var (packet, onlyTo) in V113MobHpDisplayPackets.Build(hit, display))
        {
            if (onlyTo is null)
            {
                await BroadcastPacketToMapAsync(player.Character, session, packet, ct);
            }
            else
            {
                await SendToRecentAttackersAsync(onlyTo, packet, player, session, ct);
            }
        }
    }

    /// <summary>只送給同地圖、最近攻擊過該怪的玩家（對照 Java <c>cattacker.getAttacker().getMap() == from.getMap()</c>）。</summary>
    private async Task SendToRecentAttackersAsync(
        IReadOnlyList<int> attackerIds,
        byte[] packet,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        IReadOnlyList<MapPlayerEntry>? others = null;
        foreach (var attackerId in attackerIds)
        {
            if (attackerId == player.Character.Id)
            {
                await session.SendAsync(packet, ct);
                continue;
            }

            others ??= _mapRegistry.GetOthers(player.Character.MapId, player.Character.Id);
            var other = others.FirstOrDefault(o => o.CharId == attackerId);
            if (other is null)
            {
                continue;
            }

            try
            {
                await other.SendPacket(packet, ct);
            }
            catch { /* session might be closing */ }
        }
    }

    /// <summary>
    /// 怪物死亡時通知控制者釋放控制權（對照 Java <c>MapleMonster</c> 死亡流程：
    /// <c>controll.getClient().sendPacket(MobPacket.stopControllingMonster(...))</c>）。
    /// 一般情況控制者就是擊殺者本人（自己走 session 直送）；Java 註解特別標註「這只有隱身 GM
    /// 攻擊怪物時才會跟擊殺者不同」，此時透過 <see cref="IOnlinePlayerRegistry"/> 找控制者送包，
    /// 找不到（已離線）則略過——沒有控制者（<paramref name="controllerId"/> 為 0）時不送任何包。
    /// </summary>
    private async Task NotifyControllerStoppedAsync(
        int controllerId,
        int mobObjectId,
        Player player,
        MapleSession session,
        CancellationToken ct)
    {
        if (controllerId == 0)
        {
            return;
        }

        var packet = V113CombatPackets.StopControllingMonster(mobObjectId);
        if (controllerId == player.Character.Id)
        {
            await session.SendAsync(packet, ct);
            return;
        }

        var controller = _onlinePlayers.FindById(controllerId);
        if (controller is null)
        {
            return;
        }

        try
        {
            await controller.SendPacket(packet, ct);
        }
        catch
        {
            // best-effort；controller session 可能剛好斷線。
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

            // P059：對照 Java MapleGuild.memberLevelJobUpdate，升級時同步公會成員快取欄位並廣播。
            if (mutation.Updates.Any(static u => u.Kind == PlayerStatKind.Level))
            {
                await _guildOperationHandler.SyncMemberLevelJobAsync(
                    player, (pkt, tkn) => session.SendAsync(pkt, tkn), ct);
                await OnLeveledUpAsync(player, ct);
            }
        }
        else if (rewards.ExpGained > 0)
        {
            await session.SendAsync(V113DropPackets.UpdateExp(player.Character.Exp), ct);
        }

        if (rewards.ExpGained > 0)
        {
            await session.SendAsync(ExpGainPacket(rewards.KillerAward, rewards.ExpGained, rewards.KillerHighestDamage), ct);
        }

        // P150：其他攻擊者／隊友依 Java killBy 分得的經驗（各自的 session）。
        foreach (var award in rewards.OtherAwards ?? [])
        {
            var entry = _mapRegistry.GetAll(award.Receiver.Character.MapId).FirstOrDefault(e => e.CharId == award.Receiver.Character.Id);
            if (entry is null)
            {
                continue;
            }

            if (award.StatsMutation is { } otherMutation && V113StatsHandlers.EncodeUpdateStats(otherMutation) is { } otherStats)
            {
                await SendBestEffortAsync(entry, otherStats, ct);
                if (otherMutation.Updates.Any(static u => u.Kind == PlayerStatKind.Level))
                {
                    await _guildOperationHandler.SyncMemberLevelJobAsync(award.Receiver, entry.SendPacket, ct);
                    await OnLeveledUpAsync(award.Receiver, ct);
                }
            }
            else
            {
                await SendBestEffortAsync(entry, V113DropPackets.UpdateExp(award.Receiver.Character.Exp), ct);
            }

            await SendBestEffortAsync(entry, ExpGainPacket(award, award.Exp, award.HighestDamage), ct);
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

    /// <summary>P151：Java GainEXP_Monster(gain + Additional, white, …, Party_EXP, Party_Bonus_EXP, …)。</summary>
    private static byte[] ExpGainPacket(MobExpAward? award, int exp, bool white)
        => award is null
            ? V113DropPackets.ShowExpGainMonster(exp, white)
            : V113DropPackets.ShowExpGainMonster(award.ShownExp, white, award.PartyExp, award.PartyBonusExp);

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

    /// <summary>
    /// 對照 Java InterServerHandler 登入流程：
    /// <c>for (MapleQuestStatus status : player.getStartedQuests()) { if (status.hasMobKills())
    /// { c.sendPacket(MaplePacketCreator.updateQuestMobKills(status)); } }</c>——SET_FIELD 內嵌的任務
    /// 段只有 questId/customData，怪物擊殺進度要另外逐一補送，否則客戶端任務清單的擊殺計數在登入當下
    /// 是空的，要等玩家自己再殺一隻怪才會更新。<see cref="V113QuestPackets.UpdateQuestMobKills"/>
    /// 這個封包建構式先前已存在但零呼叫者。
    /// </summary>
    internal static async Task SendStartedQuestMobKillsAsync(
        Character chr,
        Func<byte[], CancellationToken, Task> sendSelf,
        CancellationToken ct)
    {
        foreach (var quest in chr.Quests)
        {
            if (quest.Status == (byte)QuestStatus.Started && quest.MobKills.Count > 0)
            {
                await sendSelf(V113QuestPackets.UpdateQuestMobKills(quest), ct);
            }
        }
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

        await DetachSummonsOnLeaveAsync(player, currentField, player.Character.MapId, session: null, ct);
        await CloseMagicDoorAsync(player.Character.Id, ct); // P088：Java changeRemoval → removeDoor

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
    /// <returns>P130：怪物放逐了控制者時回傳目的地，由主迴圈換圖；否則 null。</returns>
    private async Task<MobBanishInfo?> HandleMoveLifeAsync(PacketReader reader, Player player, FieldInstance field, MapleSession session, CancellationToken ct)
    {
        V113MoveLifeData data;
        try
        {
            data = V113MobMovementPackets.ParseMoveLife(reader);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        Mob? mob;
        lock (field)
        {
            mob = field.GetMob(data.ObjectId);
        }

        if (mob is null || !mob.IsAlive) return null;

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

        // 對照 Java MobHandler.CheckMobVac_x（反作弊 3 件第 3 件的簡化子項）：非飛行怪物單次
        // 移動的 X 座標落差過大只記錄，不阻擋（Java 用計數器每 10 次才記一次避免洗版，這裡簡化
        // 成每次觸發都記，偵測邏輯與門檻本身忠實對照，差別僅在記錄頻率，不影響防護效果）。
        if (IsMobVacXAnomaly(mob.Stats.Fly, mob.Position.X, data.StartX))
        {
            _log.LogWarning(
                "[Channel] MoveLife X 座標異常 charId={CharId} mapId={MapId} monsterId={MonsterId} objectId={ObjectId} startX={StartX} endX={EndX}",
                player.Character.Id, player.Character.MapId, mob.Definition.MonsterId, data.ObjectId, data.StartX, mob.Position.X);
        }

        // P122：對照 Java MobHandler.MoveMonster 的 useSkill 區塊——施放成功才在回應帶 realskill/level（否則 0/0）。
        MobSkillUse? skillUse = null;
        if (data.UseSkill > 0 && _mobSkills is not null)
        {
            lock (field)
            {
                skillUse = mob.IsAlive ? _mobSkills.TryUseSkill(field, mob, DateTimeOffset.UtcNow, player) : null;
            }
        }

        // P123：Java applyEffect（在送回應之前）→ applyMonsterBuff——同狀態先 cancelStatus（CANCEL），再對全圖送整組 applyMonsterStatus。
        if (skillUse?.Buffs is { } buffs)
        {
            foreach (var buff in buffs)
            {
                foreach (var cancelled in buff.Cancelled)
                {
                    await BroadcastPacketToMapAsync(player.Character, session, V113MonsterStatusCodec.CancelMonsterStatus(buff.ObjectId, [cancelled]), ct);
                }

                await BroadcastPacketToMapAsync(player.Character, session, V113MonsterStatusCodec.ApplyMonsterStatus(buff.ObjectId, buff.Applied), ct);
            }
        }

        // P126：Java getDiseaseBuff → 自己 giveDebuff、同圖他人 giveForeignDebuff。
        if (skillUse?.Disease is { } disease)
        {
            await session.SendAsync(V113DiseasePackets.GiveDebuff(disease), ct);
            await BroadcastPacketToOthersAsync(player.Character, V113DiseasePackets.GiveForeignDebuff(player.Character.Id, disease), ct);
        }

        // P129：Java player.dispel() → cancelEffect（CANCEL_BUFF）；連帶移除召喚獸、壓回 HP/MP 上限。
        if (skillUse?.DispelledBuffs is { } dispelled)
        {
            foreach (var cancellation in dispelled)
            {
                await session.SendAsync(V113SkillPackets.CancelBuff(cancellation.Stats), ct);
            }

            await _buffEffects.ApplyAsync(player, field, dispelled, ct, (pkt, tkn) => session.SendAsync(pkt, tkn));
        }

        // P131：Java spawnMist → spawnAndAddRangedMapObject（全圖 SPAWN_MIST）。
        if (skillUse?.SpawnedMist is { } mist)
        {
            await BroadcastPacketToMapAsync(player.Character, session, V113MistPackets.SpawnMist(mist), ct);
        }

        // P125：Java heal(…, broadcast=true) → map.broadcastMessage(healMonster)。
        if (skillUse?.Heals is { } heals)
        {
            foreach (var heal in heals)
            {
                await BroadcastPacketToMapAsync(player.Character, session, V113CombatPackets.HealMonster(heal.ObjectId, heal.Amount), ct);
            }
        }

        // P128：Java spawnMonsterWithEffect → spawnMonster(monster, -2, effect, 0) 給全圖，控制者收 SPAWN_MONSTER_CONTROL。
        if (skillUse?.Summoned is { } summoned)
        {
            foreach (var summon in summoned)
            {
                await BroadcastPacketToMapAsync(
                    player.Character,
                    session,
                    V113CombatPackets.SpawnMonster(summon, spawnType: -2, effect: skillUse.Skill.SpawnEffect),
                    ct);
                await session.SendAsync(V113CombatPackets.SpawnMonsterControl(summon, newSpawn: true, aggro: false), ct);
            }
        }

        // Response 給控制者
        await session.SendAsync(
            V113MobMovementPackets.MoveMonsterResponse(
                data.ObjectId,
                data.MoveId,
                mob.Mp,
                aggro: false,
                skillId: (byte)(skillUse?.Skill.SkillId ?? 0),
                skillLevel: (byte)(skillUse?.Skill.Level ?? 0)),
            ct);

        // 廣播移動給同圖其他玩家
        var broadcast = V113MobMovementPackets.BroadcastMoveMonster(
            data.ObjectId, data.UseSkill, data.SkillIndex, data.SkillData,
            data.StartX, data.StartY, data.RawMovement);
        await BroadcastPacketToOthersAsync(player.Character, broadcast, ct);
        return skillUse?.Banish is { MapId: > 0 } banish ? banish : null;
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
        // P138：Java InnerPortal 開頭 chr.updateUsingPortal(true)——之後 2 秒內物理攻擊不做射程檢查。
        player.MarkUsingPortal(DateTimeOffset.UtcNow);

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

        // 對照 Java PlayerHandler.InnerPortal：跟宣稱使用的傳送門距離過遠只記錄警訊，不擋動作
        // 本身（Java 的 registerOffense 只是累積違規次數供後續判斷，不是即時封鎖）。
        if (IsFarFromPortal(portal, player.Position))
        {
            _log.LogWarning(
                "[Channel] 玩家 {Name} 使用內部傳送門距離過遠 charPos=({Cx},{Cy}) portal={Portal}({Px},{Py})",
                player.Character.Name, player.Position.X, player.Position.Y, request.PortalName, portal.X, portal.Y);
        }

        player.MoveTo(new Position(request.X, request.Y, player.Position.Stance, player.Position.Foothold));
        await session.SendAsync(V113InnerPortalPackets.CurrentMapWarp((byte)portal.Id), ct);
    }

    /// <summary>對照 Java <c>portal.getPosition().distanceSq(chr.getPosition()) &gt; 22500</c>（150 格）。</summary>
    internal static bool IsFarFromPortal(MapPortal portal, Position position)
    {
        var dx = portal.X - position.X;
        var dy = portal.Y - position.Y;
        return ((long)dx * dx) + ((long)dy * dy) > 22500;
    }

    /// <summary>對照 Java <c>Client_Reportedpos.distanceSq(mapitem.getPosition()) &gt; 2500</c>（50 格）。</summary>
    internal static bool IsFarFromDropClientReported(Position clientPosition, Position dropPosition)
        => DistanceSquared(clientPosition, dropPosition) > 2500;

    /// <summary>對照 Java <c>chr.getPosition().distanceSq(mapitem.getPosition()) &gt; 90000.0</c>（300 格）。</summary>
    internal static bool IsFarFromDropServer(Position serverPosition, Position dropPosition)
        => DistanceSquared(serverPosition, dropPosition) > 90000;

    private static long DistanceSquared(Position a, Position b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return ((long)dx * dx) + ((long)dy * dy);
    }

    /// <summary>對照 Java <c>MobHandler.CheckMobVac_x</c>：非飛行怪物 <c>abs(startX - endX) &gt; 500</c>。</summary>
    internal static bool IsMobVacXAnomaly(bool fly, int endX, int startX)
        => !fly && Math.Abs(endX - startX) > 500;

    /// <summary>對照 Java <c>PlayerHandler.Heal</c> 的 REGEN_HIGH_HP：<c>healHP &gt;
    /// shouldHealHP() * 5</c>。刻意簡化：Java 的 <c>shouldHealHP()</c> 還會疊加特定技能（HP
    /// 增幅類技能等）的加成，MapleForge 目前沒有對應的技能效果查詢管線，這裡先只算基礎值
    /// （10）+ 坐椅加成（150，對照 <c>chr.getChair() != 0</c>）。持有那些技能的玩家可能偶爾
    /// 觸發誤判 log，但不影響實際回血（Java 這個檢查本身也只記錄不阻擋，回血照樣套用）。
    /// Java 的 REGEN_HIGH_MP 檢查在原始碼裡是註解掉的死碼，這裡比照不移植。</summary>
    internal static bool IsRegenHighHp(int requestedHp, int chairItemId)
    {
        var shouldHealHp = 10 + (chairItemId != 0 ? 150 : 0);
        return requestedHp > shouldHealHp * 5;
    }

    // ── P058：ATTACK_TYPE_ERROR（反作弊 3 件第 3 件另一子項，拆解自 registerOffense 籠統標籤）──
    // 對照 Java constants.SkillConstants：三個攻擊封包（近戰/遠程/魔法）宣稱使用的技能 id
    // 必須屬於對應分類的硬編碼清單，否則 PlayerHandler.closeRangeAttack/rangedAttack/MagicDamage
    // 會 registerOffense(ATTACK_TYPE_ERROR) 並直接 return（整包丟棄，不處理傷害也不廣播）。
    // 清單內容用 awk/grep 對原始碼機械抽取，並逐段人工核對 switch-case 本體（無 fallthrough
    // 陷阱、無多值合併），確保跟 Java 逐一比對過，降低「手抄漏字誤傷合法玩家攻擊」風險。
    // isSpecialMove（type 4）在 Java 是空 switch（只有 default），永遠回傳 false，MapleForge
    // 目前 3 個攻擊封包沒有對應 type=4 的入口，故不需要移植。

    internal static readonly HashSet<int> CloseRangedAttackSkillIds =
    [
        0, 1009, 1020, 1001004, 1001005, 1100002, 1100003, 1111003, 1111004, 1111005,
        1111006, 1111008, 1121006, 1121008, 1200002, 1200003, 1211002, 1221007, 1221009, 1221011,
        1300002, 1300003, 1311001, 1311002, 1311003, 1311004, 1311005, 1311006, 1321003, 3101003,
        3201003, 4001002, 4001334, 4121008, 4201004, 4201005, 4211002, 4211004, 4211006, 4221001,
        4221007, 5001001, 5001002, 5101002, 5101003, 5101004, 5110001, 5111002, 5111004, 5111006,
        5121001, 5121004, 5121005, 5121007, 5201002, 5201004, 5221003, 9001006, 10001009, 10001020,
        11001002, 11001003, 11101002, 11111003, 11111004, 11111006, 14001002, 14111006, 15001001, 15001002,
        15100004, 15101003, 15101004, 15101005, 15111001, 15111003, 15111004, 20000014, 20000015, 20000016,
        20001009, 20001020, 20011020, 21000002, 21100001, 21100002, 21101003, 21110003, 21110006, 21110007,
        21110008, 21120005, 21120009, 21120010,
    ];

    internal static readonly HashSet<int> RangedAttackSkillIds =
    [
        0, 3001004, 3001005, 3100001, 3101005, 3110001, 3111003, 3111004, 3111006, 3121003,
        3121004, 3200001, 3201005, 3210001, 3211003, 3211004, 3211006, 3221001, 3221003, 3221007,
        4001344, 4101005, 4111004, 4111005, 4121003, 4121007, 4221003, 5001003, 5121002, 5201001,
        5201006, 5210000, 5211004, 5211005, 5211006, 5220011, 5221004, 5221007, 5221008, 5221009,
        11101004, 13001003, 13101002, 13101005, 13111000, 13111001, 13111002, 13111006, 13111007, 14001004,
        14101006, 14111002, 14111005, 15111006, 15111007, 21100004, 21110004, 21120006,
    ];

    internal static readonly HashSet<int> MagicAttackSkillIds =
    [
        1000, 2001004, 2001005, 2101004, 2101005, 2111002, 2111003, 2111006, 2121001, 2121003,
        2121006, 2121007, 2201004, 2201005, 2211002, 2211003, 2211006, 2221001, 2221003, 2221006,
        2221007, 2301002, 2301005, 2311004, 2321001, 2321007, 2321008, 10001000, 12101002, 12001003,
        12101006, 12111003, 12111005, 12111006, 20001000,
    ];

    /// <summary>對照 Java <c>SkillConstants.isCloseRangedAttack</c>（91 個技能 id）。</summary>
    internal static bool IsCloseRangedAttackSkill(int skillId) => CloseRangedAttackSkillIds.Contains(skillId);

    /// <summary>對照 Java <c>SkillConstants.isRangedAttack</c>（58 個技能 id）。</summary>
    internal static bool IsRangedAttackSkill(int skillId) => RangedAttackSkillIds.Contains(skillId);

    /// <summary>對照 Java <c>SkillConstants.isMagicAttack</c>（35 個技能 id）。</summary>
    internal static bool IsMagicAttackSkill(int skillId) => MagicAttackSkillIds.Contains(skillId);

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

        // P187：killBy 的經驗分配（不掉寶）。
        if (result.Rewards is { } bombRewards)
        {
            await SendMobKillRewardsAsync(player, session, bombRewards, ct);
        }

        // P186：Java 自爆走 killMonster → killBy → eventInstance.monsterKilled（最高傷害者）／unregisterMonster。
        if (result.Killed && _eventScripts is not null)
        {
            int? highest = result.Rewards switch
            {
                null => null,
                { KillerHighestDamage: true } => player.Character.Id,
                { OtherAwards: { } others } => others.FirstOrDefault(static a => a.HighestDamage)?.Receiver.Character.Id,
                _ => null,
            };
            RunEventHook(() => _eventScripts.OnMonsterKilled(field.MapId, result.Request.MobObjectId, result.MonsterId, highest));
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

    private async Task<FieldInstance?> ChangeMapSpecialByNameAsync(
        string portalName, Player player, FieldInstance? currentField,
        Dictionary<int, int> npcOidToId, MapleSession session,
        object sessionToken, CancellationToken ct)
    {
        var chr = player.Character;

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

        // P158：Java SkillEffect 只在伺服器技能等級相符且為蓄力技能時記錄按住時間並廣播。
        if (!_skillService.TryStartChargeSkill(player, skillId, level, DateTimeOffset.UtcNow))
        {
            return;
        }

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
