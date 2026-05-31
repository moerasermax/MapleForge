using Maple.Host.Shared;
using Microsoft.Extensions.Hosting;

// MapleForge Login 伺服器進入點（M0 骨架）。
// Generic Host 內建 DI、設定（appsettings.json）、logging。
var builder = Host.CreateApplicationBuilder(args);

builder.AddMapleServerInstance();

var host = builder.Build();
host.Run();
