using Maple.Core.Guilds.Bbs;
using Microsoft.Extensions.DependencyInjection;

namespace Maple.Persistence.Guilds;

public static class GuildBbsPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddGuildBbsPersistence(this IServiceCollection services)
    {
        services.AddSingleton<LiteDbGuildBbsRepository>();
        services.AddSingleton<MongoGuildBbsRepository>();
        services.AddSingleton<IGuildBbsRepository>(sp =>
        {
            var opts = sp.GetRequiredService<MapleDatabaseOptions>();
            return opts.Provider switch
            {
                MapleDatabaseProvider.LiteDb => sp.GetRequiredService<LiteDbGuildBbsRepository>(),
                _ => sp.GetRequiredService<MongoGuildBbsRepository>(),
            };
        });

        return services;
    }
}
