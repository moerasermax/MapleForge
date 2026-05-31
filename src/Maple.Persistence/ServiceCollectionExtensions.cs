using LiteDB;
using Maple.Core.Accounts;
using Maple.Persistence.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace Maple.Persistence;

/// <summary>Maple.Persistence 的 DI 擴充方法。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 向 DI 容器注冊 LiteDB 持久層（options、LiteDatabase 連線、所有 repository）。
    /// LiteDatabase 以 singleton 注冊；DI 容器釋放時會自動呼叫 Dispose。
    /// </summary>
    /// <param name="services">服務集合。</param>
    /// <param name="optionsFactory">
    ///   從 <see cref="IServiceProvider"/> 建立 <see cref="MapleDatabaseOptions"/> 的工廠，
    ///   可讀取 IConfiguration 中的實例設定（DataDirectory、InstanceName）。
    /// </param>
    public static IServiceCollection AddMaplePersistence(
        this IServiceCollection services,
        Func<IServiceProvider, MapleDatabaseOptions> optionsFactory)
    {
        services.AddSingleton(optionsFactory);

        services.AddSingleton<LiteDatabase>(sp =>
        {
            var opts = sp.GetRequiredService<MapleDatabaseOptions>();
            // 確保資料目錄存在，避免 LiteDB 開啟時因路徑不存在而例外
            Directory.CreateDirectory(opts.DataDirectory);
            return new LiteDatabase(opts.DatabasePath);
        });

        services.AddSingleton<IAccountRepository, LiteDbAccountRepository>();

        return services;
    }
}
