using Maple.Adapters.V113.Login;
using Maple.Application.Accounts;
using Maple.Application.Security;
using Maple.Host.Shared.Configuration;
using Maple.Net;
using Maple.Persistence;
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
            return new MapleDatabaseOptions { DataDirectory = o.DataDirectory, InstanceName = o.Name };
        });

        // M2：帳密驗證（BCrypt + AuthService）。
        builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        builder.Services.AddSingleton<AuthService>();

        // v113 登入選項（由實例設定投影）。
        builder.Services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ServerInstanceOptions>>().Value;
            return new V113LoginOptions(o.AutoRegister, o.Name);
        });

        // v113 連線處理（握手 + 帳密驗證 + 登入成功/失敗）。版本抽象接縫延到 M3。
        builder.Services.AddSingleton<IConnectionHandler, V113LoginConnectionHandler>();

        builder.Services.AddHostedService<TcpLoginListener>();

        return builder;
    }
}
