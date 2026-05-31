using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maple.Net;

/// <summary>
/// M0 階段的 Login 監聽 stub：綁定設定的 loginPort、接受連線、記 log，
/// <b>不做任何封包解碼</b>。M1 會接上 Session + cipher 管線。
/// </summary>
public sealed class TcpLoginListener : BackgroundService
{
    private readonly ILogger<TcpLoginListener> _log;
    private readonly LoginListenerSettings _settings;

    public TcpLoginListener(ILogger<TcpLoginListener> log, LoginListenerSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(_settings.ListenIp), _settings.LoginPort);
        var listener = new TcpListener(endpoint);
        listener.Start();
        _log.LogInformation(
            "[{Instance}] Login 監聽啟動於 {Endpoint}（M0 stub：只接受並記錄，不解碼）",
            _settings.InstanceName, endpoint);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _log.LogInformation(
                    "[{Instance}] 接受連線 {Remote}",
                    _settings.InstanceName, client.Client.RemoteEndPoint);

                // M0：不做後續處理。M1 在此掛上 Session、握手與 cipher 管線。
                _ = HandleStubAsync(client, stoppingToken);
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

    private static async Task HandleStubAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                // M0 stub：短暫保留連線後關閉。真正的握手在 M1 實作。
                await Task.Delay(100, ct);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
