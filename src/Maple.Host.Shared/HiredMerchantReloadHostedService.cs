using Maple.Adapters.V113.Channel;
using Maple.Application.PlayerShops;
using Maple.Core.PlayerShops;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Host.Shared;

internal sealed class HiredMerchantReloadHostedService : IHostedService
{
    private const int MerchantRoomFirstMapId = 910000001;
    private const int MerchantRoomLastMapId = 910000022;

    private readonly PlayerShopService _shops;
    private readonly IHiredMerchantRepository _merchants;
    private readonly V113ChannelOptions _channelOptions;
    private readonly ILogger<HiredMerchantReloadHostedService> _log;

    public HiredMerchantReloadHostedService(
        PlayerShopService shops,
        IHiredMerchantRepository merchants,
        V113ChannelOptions channelOptions,
        ILogger<HiredMerchantReloadHostedService> log)
    {
        _shops = shops;
        _merchants = merchants;
        _channelOptions = channelOptions;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await _shops.ExpireOpenMerchantsAsync(now, cancellationToken).ConfigureAwait(false);

        var channel = (byte)(_channelOptions.ChannelIndex + 1);
        var open = 0;
        for (var mapId = MerchantRoomFirstMapId; mapId <= MerchantRoomLastMapId; mapId++)
        {
            var merchants = await _merchants.FindOpenByMapAsync(channel, mapId, cancellationToken)
                .ConfigureAwait(false);
            open += merchants.Count;
        }

        _log.LogInformation(
            "[HiredMerchant] startup reload channel={Channel} open={OpenCount} expiredToClaimable={ExpiredCount}",
            channel,
            open,
            expired);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
