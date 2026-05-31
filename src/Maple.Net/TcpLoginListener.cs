using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Net;

/// <summary>
/// Login 監聽器：綁定 loginPort，接受連線後建立 <see cref="MapleSession"/> 交給
/// <see cref="IConnectionHandler"/>（M1 = v113 握手 + 登入失敗）。
/// </summary>
public sealed class TcpLoginListener : BackgroundService
{
    private readonly ILogger<TcpLoginListener> _log;
    private readonly ILoggerFactory _loggerFactory;
    private readonly LoginListenerSettings _settings;
    private readonly IConnectionHandler _handler;

    public TcpLoginListener(
        ILogger<TcpLoginListener> log,
        ILoggerFactory loggerFactory,
        LoginListenerSettings settings,
        IConnectionHandler handler)
    {
        _log = log;
        _loggerFactory = loggerFactory;
        _settings = settings;
        _handler = handler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(_settings.ListenIp), _settings.LoginPort);
        var listener = new TcpListener(endpoint);
        listener.Start();
        _log.LogInformation("[{Instance}] Login 監聽啟動於 {Endpoint}", _settings.InstanceName, endpoint);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var socket = await listener.AcceptSocketAsync(stoppingToken);
                _log.LogInformation("[{Instance}] 接受連線 {Remote}", _settings.InstanceName, socket.RemoteEndPoint);
                _ = HandleAsync(socket, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常關閉。
        }
        finally
        {
            listener.Stop();
            _log.LogInformation("[{Instance}] Login 監聽已停止", _settings.InstanceName);
        }
    }

    private async Task HandleAsync(Socket socket, CancellationToken ct)
    {
        var session = new MapleSession(socket, _loggerFactory.CreateLogger<MapleSession>());
        await using (session)
        {
            try
            {
                await _handler.HandleConnectionAsync(session, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[{Instance}] 連線處理例外 {Remote}", _settings.InstanceName, session.Remote);
            }
        }
    }
}
