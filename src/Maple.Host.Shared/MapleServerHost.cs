using Maple.Adapters.V113.Channel;
using Maple.Adapters.V113.Login;
using Maple.Application.Accounts;
using Maple.Application.Buddies;
using Maple.Application.CashShop;
using Maple.Application.Characters;
using Maple.Application.Chats;
using Maple.Application.Combat;
using Maple.Application.Duey;
using Maple.Application.Drops;
using Maple.Application.Events;
using Maple.Application.Fame;
using Maple.Application.Guilds;
using Maple.Application.Guilds.Bbs;
using Maple.Application.Items;
using Maple.Application.Maps;
using Maple.Application.Npcs;
using Maple.Application.NpcItemServices;
using Maple.Application.OnlinePlayers;
using Maple.Application.Parties;
using Maple.Application.Pets;
using Maple.Application.PlayerShops;
using Maple.Application.Quests;
using Maple.Application.Reactors;
using Maple.Application.Security;
using Maple.Application.Shops;
using Maple.Application.Skills;
using Maple.Application.Alliances;
using Maple.Application.Social;
using Maple.Application.Stats;
using Maple.Application.Storage;
using Maple.Application.Trades;
using Maple.Content.CashShop;
using Maple.Content.Items;
using Maple.Content.Quests;
using Maple.Content.Shops;
using Maple.Content.Skills;
using Maple.Scripting;
using Maple.Content.Wz;
using Maple.Core.CashShop;
using Maple.Core.Data;
using Maple.Core.Items;
using Maple.Core.NpcItemServices;
using Maple.Core.Quests;
using Maple.Core.Shops;
using Maple.Core.Skills;
using Maple.Host.Shared.Configuration;
using Maple.Net;
using Maple.Persistence;
using Maple.Persistence.Duey;
using Maple.Persistence.Guilds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Maple.Host.Shared;

/// <summary>
/// 組裝根（composition root）：把一個伺服器實例所需的服務掛上 Generic Host。
/// M0 只掛 Login 監聽 stub；後續里程碑在此擴充。
/// 多實例（instances[]）將在 M5-6 以「每實例一個 ServerInstance 範圍」擴展。
/// </summary>
public static class MapleServerHost
{
    public static THostBuilder AddMapleServerInstance<THostBuilder>(this THostBuilder builder)
        where THostBuilder : IHostApplicationBuilder
    {
        builder.Services
            .AddOptions<ServerInstanceOptions>()
            .Bind(builder.Configuration.GetSection(ServerInstanceOptions.SectionName))
            .ValidateOnStart();

        // 將實例設定投影成 Maple.Net 用的最小設定，避免下層依賴 Host 層型別。
        builder.Services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            return new LoginListenerSettings(o.Name, o.ListenIp, o.LoginPort);
        });

        // M2：LiteDB 持久層（每實例一個 .db 檔，見設計書 §4.4）。
        builder.Services.AddMaplePersistence(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            return new MapleDatabaseOptions
            {
                Provider = ParseDatabaseProvider(builder.Configuration["Persistence:Provider"]),
                InstanceName = o.Name,
                DataDirectory = o.DataDirectory,
                MongoConnectionString = builder.Configuration["Persistence:MongoConnectionString"] ?? "mongodb://localhost:27017",
                MongoDatabaseName = builder.Configuration["Persistence:MongoDatabaseName"] ?? string.Empty,
            };
        });
        builder.Services.AddMapleDueyPersistence();
        builder.Services.AddGuildBbsPersistence();

        // M2：帳密驗證 + 角色服務。
        builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<CharacterService>();

        // M3-4/M3-6：WZ 資料提供者（process 級快取）+ 地圖服務。
        builder.Services.AddSingleton<IDataProvider>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            return new WzDataProvider(o.WzDirectory);
        });
        builder.Services.AddSingleton<MapService>();

        // M3-7：地圖 session 登記表（thread-safe process 單例）。
        builder.Services.AddSingleton<IMapSessionRegistry, InMemoryMapSessionRegistry>();
        builder.Services.AddSingleton<IFieldInstanceRegistry, InMemoryFieldInstanceRegistry>();

        // NPC 商店 / 倉庫 / 戰鬥用例。
        builder.Services.AddSingleton<IShopCatalog>(_ => new JsonShopCatalog(ResolveShopCatalogPath(builder)));
        builder.Services.AddSingleton<ShopService>();
        builder.Services.AddSingleton<StorageService>();
        builder.Services.AddSingleton<ICashItemCatalog>(_ => new JsonCashItemCatalog(ResolveCashItemCatalogPath(builder)));
        builder.Services.AddSingleton<CashShopService>();
        builder.Services.AddSingleton<ISkillCatalog>(_ => new InMemorySkillCatalog(Array.Empty<MapleSkill>()));
        builder.Services.AddSingleton<ISkillBookCatalog>(_ => new JsonSkillBookCatalog(ResolveSkillBookCatalogPath(builder)));
        builder.Services.AddSingleton<SkillService>();
        builder.Services.AddSingleton<IMonsterDropCatalog>(_ =>
            new InMemoryMonsterDropCatalog(new Dictionary<int, IReadOnlyList<MonsterDropEntry>>()));
        builder.Services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            return new DropServiceOptions(
                ExpRate: o.Rates.Exp,
                DropRate: o.Rates.Drop,
                MesoRate: o.Rates.Meso);
        });
        builder.Services.AddSingleton<DropService>();
        builder.Services.AddSingleton<IMobKillHandler>(sp => sp.GetRequiredService<DropService>());
        builder.Services.AddSingleton<CombatService>();
        builder.Services.AddSingleton<RangedMagicCombatService>();
        builder.Services.AddSingleton<IOnlinePlayerRegistry, InMemoryOnlinePlayerRegistry>();
        builder.Services.AddSingleton<FameService>();
        builder.Services.AddSingleton<BuddyService>();
        builder.Services.AddSingleton<IPartyRegistry, InMemoryPartyRegistry>();
        builder.Services.AddSingleton<PartyService>();
        builder.Services.AddSingleton<IV113PartySessionHook, CentralPartySessionHook>();
        builder.Services.AddSingleton<IGuildRegistry, InMemoryGuildRegistry>();
        builder.Services.AddSingleton<GuildService>();
        builder.Services.AddSingleton<GuildBbsService>();
        builder.Services.AddSingleton<IV113GuildSessionHook, CentralGuildSessionHook>();
        builder.Services.AddSingleton<ChatService>();
        builder.Services.AddSingleton<IV113ChatSessionHook, CentralChatSessionHook>();
        builder.Services.AddSingleton<IQuestCatalog, MinimalQuestCatalog>();
        builder.Services.AddSingleton<QuestService>();
        builder.Services.AddSingleton<StatsService>();
        builder.Services.AddSingleton<DueyService>();
        builder.Services.AddSingleton<TradeService>();
        builder.Services.AddSingleton<PlayerShopService>();
        builder.Services.AddSingleton<RingService>();
        builder.Services.AddSingleton<FollowService>();
        builder.Services.AddSingleton<IEquipRepairCatalog, EmptyEquipRepairCatalog>();
        builder.Services.AddSingleton<EquipRepairService>();
        builder.Services.AddSingleton<IOwlSearchCatalog, EmptyOwlSearchCatalog>();
        builder.Services.AddSingleton<OwlService>();
        builder.Services.AddSingleton<PetService>();
        builder.Services.AddSingleton<IV113XmasSurpriseRewardSource, V113XmasSurpriseRewardSource>();
        builder.Services.AddSingleton<V113BuddyHandler>();
        builder.Services.AddSingleton<V113PartyOperationHandler>();
        builder.Services.AddSingleton<V113GuildOperationHandler>();
        builder.Services.AddSingleton<V113CashShopOperationHandler>();
        builder.Services.AddSingleton<V113ChatHandler>();
        builder.Services.AddSingleton<V113PlayerInteractionRouter>();
        builder.Services.AddSingleton<V113HiredMerchantHandler>();
        builder.Services.AddSingleton<V113DueyHandler>();
        builder.Services.AddSingleton<V113BbsHandler>();
        builder.Services.AddSingleton<V113RingHandler>();
        builder.Services.AddSingleton<V113FollowHandler>();
        builder.Services.AddSingleton<V113RepairHandler>();
        builder.Services.AddSingleton<V113OwlHandler>();
        builder.Services.AddSingleton<V113BuffItemHandler>();
        builder.Services.AddSingleton<IItemEffectCatalog, HardcodedItemEffectCatalog>();
        builder.Services.AddSingleton<UseItemService>();
        builder.Services.AddSingleton<IItemUseCatalog, WzItemUseCatalog>();
        builder.Services.AddSingleton<ItemUseService>();
        builder.Services.AddSingleton<IItemMakeCatalog, WzItemMakeCatalog>();
        builder.Services.AddSingleton<IItemMakerRandomSource, ItemMakerRandomSource>();
        builder.Services.AddSingleton<ItemMakerService>();
        builder.Services.AddSingleton<IV113ItemUseRandomSource, V113ItemUseRandomSource>();
        builder.Services.AddSingleton<V113ItemUseHandler>();
        builder.Services.AddSingleton<IScrollCatalog, HardcodedScrollCatalog>();
        builder.Services.AddSingleton<ScrollService>();
        builder.Services.AddSingleton<V113ScrollHandler>();
        builder.Services.AddSingleton<V113UseConsumableHandler>();
        builder.Services.AddSingleton<V113UseCashItemHandler>();
        builder.Services.AddSingleton<Maple.Core.Alliances.IAllianceRepository, InMemoryAllianceRepository>();
        builder.Services.AddSingleton<AllianceService>();
        builder.Services.AddSingleton<IV113AllianceSessionHook, CentralAllianceSessionHook>();
        builder.Services.AddSingleton<V113AllianceHandler>();
        builder.Services.AddSingleton<MessengerService>();
        builder.Services.AddSingleton<IV113MessengerSessionHook, CentralMessengerSessionHook>();
        builder.Services.AddSingleton<V113MessengerHandler>();
        builder.Services.AddSingleton<DoorService>();
        builder.Services.AddSingleton<V113DoorHandler>();
        builder.Services.AddSingleton<Maple.Core.Social.INoteRepository, Maple.Persistence.Notes.LiteDbNoteRepository>();
        builder.Services.AddSingleton<NoteService>();
        builder.Services.AddSingleton<V113NoteHandler>();
        builder.Services.AddSingleton<Maple.Core.Families.IFamilyRepository, Maple.Application.Families.InMemoryFamilyRepository>();
        builder.Services.AddSingleton<Maple.Application.Families.IFamilyRegistry, Maple.Application.Families.FamilyService>();
        builder.Services.AddSingleton<Maple.Application.Families.FamilyService>();
        builder.Services.AddSingleton<IV113FamilySessionHook, CentralFamilySessionHook>();
        builder.Services.AddSingleton<V113FamilyHandler>();
        builder.Services.AddSingleton<CoconutEventService>();
        builder.Services.AddSingleton<V113EventMiniGameHandler>();

        // v113 登入選項（由實例設定投影）。
        builder.Services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            var ip = System.Net.IPAddress.Parse(o.ChannelIp).GetAddressBytes();
            return new V113LoginOptions(o.AutoRegister, o.Name, ChannelIp: ip, ChannelPort: o.ChannelPort);
        });

        // v113 連線處理（握手 + 帳密驗證 + 世界/頻道列表 + 角色列表 + 建角/選角）。
        builder.Services.AddSingleton<IConnectionHandler, V113LoginConnectionHandler>();
        builder.Services.AddHostedService<TcpLoginListener>();

        // Channel 監聽器設定。
        builder.Services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            return new ChannelListenerSettings(o.Name, o.ListenIp, o.ChannelPort);
        });

        // 路線圖②：NPC 對話腳本引擎（Jint 跑既有 OdinMS .js；CLR sandbox off）。
        builder.Services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            return new NpcScriptOptions { ScriptsDirectory = o.ScriptsDirectory };
        });
        builder.Services.AddSingleton<INpcScriptFactory, JintNpcScriptFactory>();
        builder.Services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            return new ReactorScriptOptions { ScriptsDirectory = o.ScriptsDirectory };
        });
        builder.Services.AddSingleton<IReactorScriptFactory, JintReactorScriptFactory>();
        builder.Services.AddSingleton<ReactorService>();

        // v113 Channel 選項。
        builder.Services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            var ip = System.Net.IPAddress.Parse(o.ChannelIp).GetAddressBytes();
            return new V113ChannelOptions(ChannelIndex: 0, ChannelIp: ip, ChannelPort: o.ChannelPort);
        });

        // v113 Channel 連線處理。
        builder.Services.AddSingleton<IChannelConnectionHandler, V113ChannelConnectionHandler>();
        builder.Services.AddHostedService<TcpChannelListener>();

        return builder;
    }

    private static MapleDatabaseProvider ParseDatabaseProvider(string? value)
        => Enum.TryParse<MapleDatabaseProvider>(value, ignoreCase: true, out var provider)
            ? provider
            : MapleDatabaseProvider.MongoDb;

    private static string ResolveShopCatalogPath(IHostApplicationBuilder builder)
    {
        var configured = builder.Configuration["Content:ShopCatalogPath"]
            ?? builder.Configuration["Shops:CatalogPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var fromOutput = Path.Combine(AppContext.BaseDirectory, "Shops", "npc-shops.v113.json");
        if (File.Exists(fromOutput))
        {
            return fromOutput;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Maple.Content", "Shops", "npc-shops.v113.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return fromOutput;
    }

    private static string ResolveCashItemCatalogPath(IHostApplicationBuilder builder)
    {
        var configured = builder.Configuration["Content:CashItemCatalogPath"]
            ?? builder.Configuration["CashShop:CatalogPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var fromOutput = Path.Combine(AppContext.BaseDirectory, "CashShop", "minimal-cash-items.v113.json");
        if (File.Exists(fromOutput))
        {
            return fromOutput;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Maple.Content", "CashShop", "minimal-cash-items.v113.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return fromOutput;
    }

    private static string ResolveSkillBookCatalogPath(IHostApplicationBuilder builder)
    {
        var configured = builder.Configuration["Content:SkillBookCatalogPath"]
            ?? builder.Configuration["Skills:SkillBookCatalogPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var fromOutput = Path.Combine(AppContext.BaseDirectory, "Skills", "minimal-skill-books.v113.json");
        if (File.Exists(fromOutput))
        {
            return fromOutput;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Maple.Content", "Skills", "minimal-skill-books.v113.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return fromOutput;
    }
}
