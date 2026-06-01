using Maple.Tools.HeadlessClient;

// ── 已知機器碼尾段（22 bytes，windower 擷取 ground truth）
var machineCodeTail = Convert.FromHexString("D8BBC18E37BEEE4A365E00000000AD7A000000000200");

// ── CLI 引數
string host     = args.Length > 0 ? args[0] : "127.0.0.1";
int    loginPort = args.Length > 1 ? int.Parse(args[1]) : 8484;
string account  = args.Length > 2 ? args[2] : "testuser";
string password = args.Length > 3 ? args[3] : "test1234";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// ── Phase 1: Login Server (8484) ────────────────────────────────────────────

var loginCatalog = new PacketCatalog("login");
int  selectedCharId = 0;
string? channelIp  = null;
int     channelPort = 0;

Console.WriteLine($"[headless] 連線 {host}:{loginPort}  account={account}");

MapleConnection loginConn;
try
{
    loginConn = await MapleConnection.ConnectAsync(host, loginPort, cts.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[headless] 連線失敗：{ex.Message}");
    Console.Error.WriteLine("           請先啟動 login server（port 8484）再執行。");
    return;
}

// login phase 有自己的 CTS，收到 ServerIp 後取消（不影響 Ctrl+C cts）
using var loginPhaseCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);

await using (loginConn)
{
    Console.WriteLine("[headless] [login] Hello 完成，cipher 就緒");
    await loginConn.SendAsync(C2S.Login(account, password, machineCodeTail), loginPhaseCts.Token);
    Console.WriteLine($"[headless] [login] → Login  account={account}");

    // s2c opcodes（Login SendOp）
    const ushort LoginStatus   = 0x0000;
    const ushort Serverlist    = 0x0002;
    const ushort Charlist      = 0x0003;
    const ushort ServerIp      = 0x0004;
    const ushort Ping          = 0x0009;

    bool sentServerlistReq = false;
    bool sentCharlistReq   = false;
    bool sentCharSelect    = false;

    try
    {
        while (!loginPhaseCts.Token.IsCancellationRequested)
        {
            var payload = await loginConn.ReceiveAsync(loginPhaseCts.Token);
            ushort opcode = (ushort)(payload[0] | (payload[1] << 8));
            loginCatalog.Record(payload);
            Console.WriteLine(
                $"[headless] [login] ← s2c 0x{opcode:X4} len={payload.Length,-4} {HexPreview(payload)}");

            switch (opcode)
            {
                case Ping:
                    await loginConn.SendAsync(C2S.Pong(), loginPhaseCts.Token);
                    Console.WriteLine("[headless] [login] → Pong (0x0E)");
                    break;

                case LoginStatus:
                    byte loginType = payload.Length > 2 ? payload[2] : (byte)0xFF;
                    if (loginType == 0) // 成功
                    {
                        Console.WriteLine("[headless] [login] 登入成功");
                        if (!sentServerlistReq)
                        {
                            await loginConn.SendAsync(C2S.ServerlistRequest(), loginPhaseCts.Token);
                            sentServerlistReq = true;
                            Console.WriteLine("[headless] [login] → ServerlistRequest (0x03)");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[headless] [login] 登入失敗 type=0x{loginType:X2}" +
                            " (3=封鎖 4=密碼錯 5=未註冊)");
                        loginPhaseCts.Cancel();
                    }
                    break;

                case Serverlist:
                    // EndOfServerList = opcode 0x02 + byte 0xFF（共 3 bytes）
                    if (payload.Length == 3 && payload[2] == 0xFF)
                    {
                        Console.WriteLine("[headless] [login] EndOfServerList");
                        if (!sentCharlistReq)
                        {
                            // ServerStatusRequest 補取 s2c 0x16
                            await loginConn.SendAsync(C2S.ServerStatusRequest(), loginPhaseCts.Token);
                            Console.WriteLine("[headless] [login] → ServerStatusRequest (0x18)");
                            await loginConn.SendAsync(C2S.CharlistRequest(), loginPhaseCts.Token);
                            sentCharlistReq = true;
                            Console.WriteLine("[headless] [login] → CharlistRequest (0x04)");
                        }
                    }
                    break;

                case Charlist:
                    int charId = S2CReader.ParseFirstCharId(payload);
                    if (charId == 0)
                    {
                        Console.WriteLine("[headless] [login] CharList: 無角色（先 create-test-char.ps1 建角）");
                        loginPhaseCts.Cancel();
                        break;
                    }
                    selectedCharId = charId;
                    Console.WriteLine($"[headless] [login] CharList: 第一個 charId={charId}");
                    if (!sentCharSelect)
                    {
                        await loginConn.SendAsync(C2S.CharSelect(charId), loginPhaseCts.Token);
                        sentCharSelect = true;
                        Console.WriteLine($"[headless] [login] → SelectChar (0x06) charId={charId}");
                    }
                    break;

                case ServerIp:
                    try
                    {
                        (channelIp, channelPort) = S2CReader.ParseChannelAddress(payload);
                        Console.WriteLine(
                            $"[headless] [login] ServerIp → {channelIp}:{channelPort}  charId={selectedCharId}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[headless] [login] ServerIp 解析失敗：{ex.Message}");
                    }
                    loginPhaseCts.Cancel(); // login phase 結束
                    break;
            }
        }
    }
    catch (OperationCanceledException) { /* 正常退出 */ }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        Console.Error.WriteLine($"[headless] [login] 錯誤：{ex.Message}");
    }
}

loginCatalog.PrintSummary(Console.Out);
SaveJson(loginCatalog, "catalog-login.json");

// ── Phase 2: Channel Server (8585) ──────────────────────────────────────────

if (channelIp is null || selectedCharId == 0 || cts.IsCancellationRequested)
{
    Console.WriteLine("\n[headless] 未到達頻道（login phase 未完成）");
    return;
}

Console.WriteLine($"\n[headless] ── 進入頻道 {channelIp}:{channelPort}  charId={selectedCharId} ──");

var channelCatalog = new PacketCatalog("channel");

MapleConnection channelConn;
try
{
    channelConn = await MapleConnection.ConnectAsync(channelIp, channelPort, cts.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[headless] [channel] 連線失敗：{ex.Message}");
    Console.Error.WriteLine("           請確認 channel server（port 8585）已啟動。");
    return;
}

await using (channelConn)
{
    Console.WriteLine("[headless] [channel] Hello 完成，cipher 就緒");
    await channelConn.SendAsync(C2S.PlayerLoggedIn(selectedCharId), cts.Token);
    Console.WriteLine($"[headless] [channel] → PlayerLoggedIn (0x07) charId={selectedCharId}");

    // s2c opcodes（Channel SendOp）
    const ushort ChPing     = 0x0009;
    const ushort ChSetField = 0x007B;

    bool reachedSetField = false;

    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            var payload = await channelConn.ReceiveAsync(cts.Token);
            ushort opcode = (ushort)(payload[0] | (payload[1] << 8));
            channelCatalog.Record(payload);
            Console.WriteLine(
                $"[headless] [channel] ← s2c 0x{opcode:X4} len={payload.Length,-4} {HexPreview(payload)}");

            switch (opcode)
            {
                case ChPing:
                    await channelConn.SendAsync(C2S.Pong(), cts.Token);
                    Console.WriteLine("[headless] [channel] → Pong (0x0E)");
                    break;

                case ChSetField:
                    if (!reachedSetField)
                    {
                        reachedSetField = true;
                        Console.WriteLine("[headless] [channel] ★ SetField — IN GAME！角色已進入地圖");
                        Console.WriteLine("           繼續收 s2c 直到 Ctrl+C…");
                    }
                    break;
            }
        }
    }
    catch (OperationCanceledException) { /* 正常退出 */ }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        Console.Error.WriteLine($"[headless] [channel] 錯誤：{ex.Message}");
        if (!reachedSetField)
            Console.Error.WriteLine("           未到達 SetField（channel server 實作邊界）");
    }
}

channelCatalog.PrintSummary(Console.Out);
SaveJson(channelCatalog, "catalog-channel.json");

// ── Helpers ──────────────────────────────────────────────────────────────────

static string HexPreview(byte[] data, int maxBytes = 24)
{
    string h = Convert.ToHexString(data.AsSpan(0, Math.Min(maxBytes, data.Length)));
    return data.Length > maxBytes ? h + "…" : h;
}

static void SaveJson(PacketCatalog catalog, string fileName)
{
    string path = Path.Combine(AppContext.BaseDirectory, fileName);
    File.WriteAllText(path, catalog.ToJson());
    Console.WriteLine($"[headless] catalog JSON → {path}");
}
