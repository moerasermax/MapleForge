using LiteDB;
using Maple.Core.Accounts;
using Maple.Core.Characters;
using Maple.Persistence.Accounts;
using Maple.Persistence.Characters;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Maple.Persistence;

/// <summary>Maple.Persistence 的 DI 擴充方法。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 向 DI 容器注冊持久層（options、資料庫連線、所有 repository）。
    /// 預設 provider 為 MongoDB，可透過 <see cref="MapleDatabaseOptions.Provider"/> 切回 LiteDB。
    /// </summary>
    /// <param name="services">服務集合。</param>
    /// <param name="optionsFactory">
    ///   從 <see cref="IServiceProvider"/> 建立 <see cref="MapleDatabaseOptions"/> 的工廠，
    ///   可讀取 IConfiguration 中的 provider、MongoDB 或 LiteDB 設定。
    /// </param>
    public static IServiceCollection AddMaplePersistence(
        this IServiceCollection services,
        Func<IServiceProvider, MapleDatabaseOptions> optionsFactory)
    {
        services.AddSingleton(optionsFactory);

        services.AddSingleton<MongoClient>(sp =>
        {
            var opts = sp.GetRequiredService<MapleDatabaseOptions>();
            return new MongoClient(opts.MongoConnectionString);
        });

        services.AddSingleton<IMongoDatabase>(sp =>
        {
            var opts = sp.GetRequiredService<MapleDatabaseOptions>();
            return sp.GetRequiredService<MongoClient>().GetDatabase(opts.EffectiveMongoDatabaseName);
        });

        services.AddSingleton<MongoSequenceGenerator>();

        services.AddSingleton<LiteDatabase>(sp =>
        {
            var opts = sp.GetRequiredService<MapleDatabaseOptions>();
            // 確保資料目錄存在，避免 LiteDB 開啟時因路徑不存在而例外
            Directory.CreateDirectory(opts.DataDirectory);
            return new LiteDatabase(opts.DatabasePath);
        });

        services.AddSingleton<LiteDbAccountRepository>();
        services.AddSingleton<LiteDbCharacterRepository>();
        services.AddSingleton<MongoAccountRepository>();
        services.AddSingleton<MongoCharacterRepository>();

        services.AddSingleton<IAccountRepository>(sp =>
        {
            var opts = sp.GetRequiredService<MapleDatabaseOptions>();
            return opts.Provider switch
            {
                MapleDatabaseProvider.LiteDb => sp.GetRequiredService<LiteDbAccountRepository>(),
                _ => sp.GetRequiredService<MongoAccountRepository>(),
            };
        });

        services.AddSingleton<ICharacterRepository>(sp =>
        {
            var opts = sp.GetRequiredService<MapleDatabaseOptions>();
            return opts.Provider switch
            {
                MapleDatabaseProvider.LiteDb => sp.GetRequiredService<LiteDbCharacterRepository>(),
                _ => sp.GetRequiredService<MongoCharacterRepository>(),
            };
        });

        return services;
    }
}
