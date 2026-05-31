using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Net;

/// <summary>
/// Channel 監聽器：綁定 channelPort，接受連線後建立 MapleSession 交給
/// IConnectionHandler（v113 握手 + PLAYER_LOGGEDIN → SET_FIELD）。
/// </summary>
public sealed class TcpChannelListener : BackgroundService
{
    private readonly ILogger<TcpChannelListener> _log;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ChannelListenerSettings _settings;
    private readonly IChannelConnectionHandler _handler;

    public TcpChannelListener(
        ILogger<TcpChannelListener> log,
        ILoggerFactory loggerFactory,
        ChannelListenerSettings settings,
        IChannelConnectionHandler handler)
    {
        _log = log;
        _loggerFactory = loggerFactory;
        _settings = settings;
        _handler = handler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(_settings.ListenIp), _settings.ChannelPort);
        var listener = new TcpListener(endpoint);
        listener.Start();
        _log.LogInformation("[{Instance}] Channel 監聽啟動於 {Endpoint}", _settings.InstanceName, endpoint);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var socket = await listener.AcceptSocketAsync(stoppingToken);
                _log.LogInformation("[{Instance}] Channel 接受連線 {Remote}", _settings.InstanceName, socket.RemoteEndPoint);
                _ = HandleAsync(socket, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
            _log.LogInformation("[{Instance}] Channel 監聽已停止", _settings.InstanceName);
        }
    }

    private async Task HandleAsync(Socket socket, CancellationToken ct)
    {
        var session = new MapleSession(socket, _loggerFactory.CreateLogger<MapleSession>());
        await using (session)
        {
            try
            {
                await _handler.HandleChannelConnectionAsync(session, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[{Instance}] Channel 連線例外 {Remote}", _settings.InstanceName, session.Remote);
            }
        }
    }
}
