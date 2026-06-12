using LiteDB;
using Maple.Core.Duey;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Maple.Persistence.Duey;

public static class DueyServiceCollectionExtensions
{
    /// <summary>
    /// Registers Duey package persistence. Call after AddMaplePersistence so shared database services exist.
    /// </summary>
    public static IServiceCollection AddMapleDueyPersistence(this IServiceCollection services)
    {
        services.AddSingleton<LiteDbDueyPackageRepository>();
        services.AddSingleton<MongoDueyPackageRepository>();

        services.AddSingleton<IDueyPackageRepository>(sp =>
        {
            var opts = sp.GetRequiredService<MapleDatabaseOptions>();
            return opts.Provider switch
            {
                MapleDatabaseProvider.LiteDb => sp.GetRequiredService<LiteDbDueyPackageRepository>(),
                _ => sp.GetRequiredService<MongoDueyPackageRepository>(),
            };
        });

        return services;
    }
}
