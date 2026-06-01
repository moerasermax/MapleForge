using Maple.Tools.HeadlessClient;

// ── 已知機器碼尾段（22 bytes，windower 擷取 ground truth，私服不驗硬體）
var machineCodeTail = Convert.FromHexString("D8BBC18E37BEEE4A365E00000000AD7A000000000200");

// ── CLI 引數（皆可覆蓋）
string host     = args.Length > 0 ? args[0] : "127.0.0.1";
int    port     = args.Length > 1 ? int.Parse(args[1]) : 8484;
string account  = args.Length > 2 ? args[2] : "testuser";
string password = args.Length > 3 ? args[3] : "test1234";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var catalog = new PacketCatalog();

Console.WriteLine($"[headless] 連線 {host}:{port}  account={account}");

MapleConnection conn;
try
{
    conn = await MapleConnection.ConnectAsync(host, port, cts.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[headless] 連線失敗：{ex.Message}");
    Console.Error.WriteLine("           請先啟動 login server（port 8484）再執行。");
    return;
}

await using (conn)
{
    Console.WriteLine("[headless] Hello 完成，cipher 就緒");

    await conn.SendAsync(C2S.Login(account, password, machineCodeTail), cts.Token);
    Console.WriteLine($"[headless] → c2s  0x0001 Login  account={account}");

    // s2c opcode 常數（登入伺服器 SendOp）
    const ushort S2cLoginStatus   = 0x0000;
    const ushort S2cServerlist    = 0x0002;
    const ushort S2cCharlist      = 0x0003;
    const ushort S2cServerIp      = 0x0004;
    const ushort S2cPing          = 0x0009;

    bool sentServerlistReq = false;
    bool sentCharlistReq   = false;

    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            byte[] payload = await conn.ReceiveAsync(cts.Token);
            ushort opcode  = (ushort)(payload[0] | (payload[1] << 8));
            catalog.Record(payload);

            string hexPreview = Convert.ToHexString(payload.AsSpan(0, Math.Min(24, payload.Length)));
            if (payload.Length > 24) hexPreview += "…";
            Console.WriteLine($"[headless] ← s2c  0x{opcode:X4}  len={payload.Length,-4}  {hexPreview}");

            switch (opcode)
            {
                case S2cPing:
                    await conn.SendAsync(C2S.Pong(), cts.Token);
                    Console.WriteLine("[headless] → c2s  0x000E Pong");
                    break;

                case S2cLoginStatus:
                    byte loginType = payload.Length > 2 ? payload[2] : (byte)0xFF;
                    if (loginType == 0) // 成功
                    {
                        Console.WriteLine("[headless] 登入成功 → 送出 ServerlistRequest");
                        if (!sentServerlistReq)
                        {
                            await conn.SendAsync(C2S.ServerlistRequest(), cts.Token);
                            sentServerlistReq = true;
                            Console.WriteLine("[headless] → c2s  0x0003 ServerlistRequest");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[headless] 登入失敗 type=0x{loginType:X2}（3=封鎖 4=密碼錯誤 5=未註冊）");
                        cts.Cancel();
                    }
                    break;

                case S2cServerlist:
                    // EndOfServerList：opcode 0x02 + byte 0xFF（共 3 bytes）
                    if (payload.Length == 3 && payload[2] == 0xFF)
                    {
                        Console.WriteLine("[headless] EndOfServerList → 送出 CharlistRequest");
                        if (!sentCharlistReq)
                        {
                            await conn.SendAsync(C2S.CharlistRequest(), cts.Token);
                            sentCharlistReq = true;
                            Console.WriteLine("[headless] → c2s  0x0004 CharlistRequest");
                        }
                    }
                    break;

                case S2cCharlist:
                    Console.WriteLine("[headless] 收到角色列表 — login server 探索完成，Ctrl+C 停止或繼續等待");
                    break;

                case S2cServerIp:
                    Console.WriteLine("[headless] 收到 ServerIp（可連頻道 8585）— 第一版至此結束");
                    break;
            }
        }
    }
    catch (OperationCanceledException) { /* Ctrl+C 正常退出 */ }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        Console.Error.WriteLine($"[headless] 錯誤：{ex.Message}");
    }
}

catalog.PrintSummary(Console.Out);

string jsonPath = Path.Combine(AppContext.BaseDirectory, "catalog.json");
File.WriteAllText(jsonPath, catalog.ToJson());
Console.WriteLine($"[headless] catalog JSON 已存 → {jsonPath}");
