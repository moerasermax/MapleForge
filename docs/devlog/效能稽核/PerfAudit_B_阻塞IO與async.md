# PerfAudit B - 阻塞 I/O 與 async 正確性

稽核日期：2026-06-06  
角色：MapleForge 效能稽核員 B  
範圍：`src/Maple.Net`、`src/Maple.Adapters.V113`、`src/Maple.Host.*`、主要 handler/service/repository。

## 路徑總覽

封包收包主路徑是 `TcpLoginListener` / `TcpChannelListener` 接受 socket 後 fire-and-forget 進 `HandleAsync`，建立 `MapleSession`，再由 `MapleSession.RunAsync` 串行讀取 header/body、解密、呼叫 handler：

- `src/Maple.Net/TcpLoginListener.cs:42` 非同步 `AcceptSocketAsync`，`src/Maple.Net/TcpLoginListener.cs:44` 以 `_ = HandleAsync(...)` 啟動連線處理。
- `src/Maple.Net/TcpChannelListener.cs:42` 非同步 `AcceptSocketAsync`，`src/Maple.Net/TcpChannelListener.cs:44` 以 `_ = HandleAsync(...)` 啟動連線處理。
- `src/Maple.Net/MapleSession.cs:85` / `src/Maple.Net/MapleSession.cs:101` 使用 `ReadExactlyAsync` 收 header/body。
- `src/Maple.Net/MapleSession.cs:106` `await onPacket(...)`，所以任一 handler 裡的 DB、檔案、log、其他玩家送包延遲，都會直接延後同連線下一個封包的讀取。
- `src/Maple.Net/MapleSession.cs:66` / `src/Maple.Net/MapleSession.cs:67` 送包使用 `NetworkStream.WriteAsync` / `FlushAsync`，沒有同步 socket write。

全專案搜尋未發現 `.Result`、`.Wait()`、`.GetAwaiter().GetResult()`、`async void` 或 `Task.Run` 包同步 I/O 的典型 sync-over-async。主要風險不是死鎖型 sync-over-async，而是「async handler 中仍執行同步 I/O」與「送包 backpressure 直接卡住收包 loop」。

## 發現

### 1. 【嚴重】`SendAsync` 會就地加密 caller 的 `byte[]`，廣播重用同一份 packet 會污染後續收件者

位置：

- `src/Maple.Net/MapleSession.cs:62` 建立 framed buffer。
- `src/Maple.Net/MapleSession.cs:64` 對傳入的 `packet` 呼叫 `_send.Crypt(packet)`，直接改寫 caller buffer。
- `src/Maple.Net/MapleSession.cs:65` 再把已改寫的 `packet` copy 到 framed。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1276` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1283` 同一個 `packet` 在 foreach 中送給多個 `other.SendPacket(...)`。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1289` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1292` `BroadcastPacketToMapAsync` 先送自己，再把同一個 packet 廣播給其他人。
- `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:517` - `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:528` guild broadcast 也是建一次 packet 後送多個 recipient。

為何是雷：

每個 Maple session 的 cipher 狀態不同。第一個 `SendAsync` 會把明文 packet 改成該 session 的加密 payload；第二個收件者拿到的是已被前一個 session 改寫的陣列，再用自己的 cipher 加密一次，容易產生錯包、時序錯覺與跨玩家廣播不穩。這也讓並發送包暴露共享 mutable buffer 競態。

具體修法：

- `MapleSession.SendAsync` 改成不修改 caller buffer：把 payload clone 到區域 buffer 後再 `_send.Crypt(payload)`；API 最好改成 `ReadOnlyMemory<byte>`。
- 若擔心 allocation，用 `ArrayPool<byte>.Shared.Rent(packet.Length + 4)`，在單一 send writer 中填 header/payload，寫完歸還。
- 在廣播層把 packet 視為 immutable；不要靠 caller 逐一 clone 來補洞，因為漏一處就會再破壞。

### 2. 【嚴重】沒有 per-connection outbound queue；慢收件者會反壓發送者的封包處理

位置：

- `src/Maple.Net/MapleSession.cs:18` 只有 `_sendLock`，沒有送包佇列。
- `src/Maple.Net/MapleSession.cs:59` - `src/Maple.Net/MapleSession.cs:67` 每次送包都等待同一把 lock 並直接 await socket write。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:176` handler 在 `session.RunAsync` callback 內處理封包。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:248` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:251` 移動封包會在收包 callback 內 await 廣播。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1276` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1283` 逐一 await 地圖其他玩家送包。
- `src/Maple.Adapters.V113/Channel/V113ChatHandler.cs:78` - `src/Maple.Adapters.V113/Channel/V113ChatHandler.cs:83` 群聊逐一 await 遠端送包。
- `src/Maple.Adapters.V113/Channel/V113PartyOperationHandler.cs:265` - `src/Maple.Adapters.V113/Channel/V113PartyOperationHandler.cs:278` 組隊更新逐一 await 遠端送包。
- `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:518` - `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:528` 公會廣播逐一 await 遠端送包。

為何是雷：

`WriteAsync` 本身非阻塞 OS thread，但會在 socket send buffer 滿、對端慢讀、網路抖動時延後完成。因為目前廣播是在發送者的收包 callback 裡逐一 await，任一慢收件者都會拖住發送者下一個封包的處理。OdinMS 類型遊戲最熱的移動/攻擊/聊天路徑會因此出現跨玩家互卡。

具體修法：

- 每個 `MapleSession` 建立 bounded `Channel<OutboundPacket>` 或專用 MPSC queue，由單一 background send loop 序列化 cipher + socket write。
- 對外暴露 `EnqueueSendAsync` / `TryEnqueueSend`，廣播只排入目標 session queue，不直接 await 目標 socket 寫完。
- 設計 queue 滿的策略：踢慢連線、丟可丟棄封包、或對關鍵封包 backpressure，但不要讓 A 玩家在處理移動封包時等待 B 玩家 socket。
- send loop 中集中處理例外與 session close，避免每個廣播點吞例外後狀態殘留。

### 3. 【高】封包擷取模式在收包熱路徑同步寫檔且 `AutoFlush`

位置：

- `src/Maple.Net/MapleSession.cs:102` - `src/Maple.Net/MapleSession.cs:105` 每個收到的封包在進 handler 前呼叫 `_capture.WriteRecv(...)`。
- `src/Maple.Net/PacketCapture.cs:33` 建立 `StreamWriter` 並設定 `AutoFlush = true`。
- `src/Maple.Net/PacketCapture.cs:47` - `src/Maple.Net/PacketCapture.cs:58` 在 lock 內組 hex 字串並同步 `_w.WriteLine(...)`。
- `src/Maple.Net/PacketCapture.cs:71` Dispose 時同步寫 end record。

為何是雷：

雖然 `MAPLEFORGE_CAPTURE=1` 才啟用，但啟用後每個 c2s 封包都會在收包 loop 內同步做 hex 轉換、取得 lock、寫 NDJSON、AutoFlush 到檔案。這會把診斷 I/O 插在「收包 -> handler」之前，足以重現過去「送包前同步 log 破壞時序」同類問題。

具體修法：

- 擷取器改成 bounded channel + 單一背景 writer，封包 thread 只複製必要 bytes 並嘗試 enqueue。
- 背景 writer 用 `FileStream.WriteAsync` / 批次 flush；不要 `AutoFlush` 每包 flush。
- 對 capture queue 設上限與 dropped counter；診斷工具不得反壓遊戲封包處理。
- 效能測試與真機 smoke 預設禁止 `MAPLEFORGE_CAPTURE=1`。

### 4. 【高】LiteDB repository 是假 async；切到 LiteDB provider 時 DB I/O 會在 handler thread 同步執行

位置：

- `src/Maple.Persistence/Accounts/LiteDbAccountRepository.cs:10` 註解明示 LiteDB 同步 API 以 `Task.FromResult` / `Task.CompletedTask` 包裝。
- `src/Maple.Persistence/Accounts/LiteDbAccountRepository.cs:31` - `src/Maple.Persistence/Accounts/LiteDbAccountRepository.cs:34` `FindOne` 後 `Task.FromResult`。
- `src/Maple.Persistence/Accounts/LiteDbAccountRepository.cs:45` - `src/Maple.Persistence/Accounts/LiteDbAccountRepository.cs:61` `Insert` / `Update` 同步執行後回 completed task。
- `src/Maple.Persistence/Characters/LiteDbCharacterRepository.cs:21` - `src/Maple.Persistence/Characters/LiteDbCharacterRepository.cs:47` `Find` / `FindById` / `FindOne` / `Insert` / `Update` 都是同步 LiteDB API。
- `src/Maple.Persistence/Guilds/LiteDbGuildRepository.cs:16` - `src/Maple.Persistence/Guilds/LiteDbGuildRepository.cs:48` `FindAll` / `FindById` / `FindOne` / `Insert` / `Update` / `Delete` 都是同步 LiteDB API。
- `src/Maple.Persistence/ServiceCollectionExtensions.cs:63` - `src/Maple.Persistence/ServiceCollectionExtensions.cs:86` 設定為 `LiteDb` provider 時會注入上述 repository。

熱路徑例子：

- 登入：`src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:131` await `_auth.AuthenticateAsync(...)`，`src/Maple.Application/Accounts/AuthService.cs:41` / `src/Maple.Application/Accounts/AuthService.cs:73` 查詢與更新帳號。
- 角色列表/建角/選角：`src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:238`、`src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:286`、`src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:310`。
- 進圖：`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1210` 查角色，`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:190` 查帳號。
- 倉庫 close：`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:997` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1000` flush 後 await account update。
- Cash shop 操作：`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:426` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:433` 直接在封包 handler 更新 account/character。
- 登出 flush：`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:461`、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:471` 更新帳號/角色。

為何是雷：

handler 看起來 await 了 async repository，但 LiteDB provider 下 await 不會釋放 thread；同步磁碟 I/O 會直接佔住連線處理 thread，且 `MapleSession.RunAsync` 是逐包串行。登入、進圖、倉庫、商城、好友、公會等路徑都可能把 DB latency 變成封包 latency。

具體修法：

- 正式或壓測設定預設使用 MongoDB provider，不使用 LiteDB provider 承載熱路徑。
- 若保留 LiteDB，建立單一 persistence actor/queue，把同步 LiteDB I/O 移出 socket handler；handler 更新記憶體狀態並排持久化命令。
- 對倉庫/cash shop/角色保存改成 transaction intent + 背景 flush，必要處只 await 記憶體一致性，不 await 磁碟完成。
- 啟動時若 `Persistence:Provider=LiteDb`，log 明確 warning：不適合多人效能測試。

### 5. 【高】NPC 腳本第一次互動會在封包 handler 內同步讀檔

位置：

- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:374` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:384` 收到 `NpcTalk` 後進 `StartNpcConversationAsync`。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:642` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:644` 在 handler 內呼叫 `_npcScripts.TryCreate(...)`。
- `src/Maple.Scripting/JintNpcScriptFactory.cs:31` - `src/Maple.Scripting/JintNpcScriptFactory.cs:39` `TryCreate` 透過 `_sourceCache.GetOrAdd` 載入 source 並同步 `engine.Execute(source)`。
- `src/Maple.Scripting/JintNpcScriptFactory.cs:49` - `src/Maple.Scripting/JintNpcScriptFactory.cs:53` 第一次載入 NPC script 時同步 `File.Exists` + `File.ReadAllText`。

為何是雷：

source cache 只避開第二次之後的檔案 I/O。某 NPC 第一次被玩家點擊時，封包 handler 會同步碰檔案系統並執行 Jint 編譯/執行。磁碟抖動、Windows Defender 掃描、腳本目錄在慢碟時，NPC_TALK 封包會直接卡住該連線；若腳本 `start()` 內再觸發 warp/open shop，後續送包時序也會被這段同步工作推遲。

具體修法：

- 啟動期或地圖載入期 preload NPC scripts，包含 negative cache；遊戲封包 handler 只查記憶體。
- 若要支援 hot reload，獨立背景 watcher/loader 更新 immutable cache，不在玩家互動時 `ReadAllText`。
- 將 Jint script parse/execute 成本納入 warmup；至少對熱門 NPC 做預熱。

### 6. 【高】WZ/地圖資料首次載入在進圖/warp 熱路徑同步讀檔

位置：

- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:240` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:242` 進圖後送 NPC/monster/drop。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:514` `SpawnMapNpcsAsync` 直接 `_mapService.LoadMap(mapId)`。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:536` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:542` 第一次建立 field 時在 lock 內 `SpawnMapMonsters`。
- `src/Maple.Application/Combat/CombatService.cs:49` 產怪時 `_maps.LoadMap(mapId)`，`src/Maple.Application/Combat/CombatService.cs:60` 每種怪 `_maps.LoadMobStats(...)`。
- `src/Maple.Application/Maps/MapService.cs:24` - `src/Maple.Application/Maps/MapService.cs:40` `LoadMap` 直接從 `IDataProvider` 讀 Map WZ node。
- `src/Maple.Content/Wz/WzDataProvider.cs:72` - `src/Maple.Content/Wz/WzDataProvider.cs:77` 第一次碰某 WZ 檔時同步 `WzFile.Open(path)`。
- `src/Maple.Content/Wz/WzModel.cs:130` - `src/Maple.Content/Wz/WzModel.cs:140` `WzFile.Open` 同步建立 `FileStream` / `BinaryReader` 並讀 header。
- `src/Maple.Content/Wz/WzModel.cs:54` 與 `src/Maple.Content/Wz/WzModel.cs:285` - `src/Maple.Content/Wz/WzModel.cs:290` WZ image properties lazy 載入時在 lock 內同步 seek/read。

為何是雷：

`WzDataProvider` 會快取 WZ file/root，WZ image properties 也會 lazy cache；但第一次進某張地圖或第一次讀某 monster/image 時，磁碟 I/O 發生在 channel handler。更糟的是初始怪物生成包在 `EnterField` 的 field lock 內呼叫，首次載入慢時會同時拖住該 map 的 runtime 狀態操作。

具體修法：

- 啟動期 warmup：預先開啟 `Map.wz` / `Mob.wz`，載入常用地圖、怪物 stats、NPC life 資料。
- `MapService` 應實作真正的 `ConcurrentDictionary<int, MapData>` / `ConcurrentDictionary<int, MobStats?>` 快取；目前註解說快取，但類別內沒有 MapData 快取欄位。
- 不要在 `lock (field)` 內做可能觸發 WZ I/O 的工作；先載入 immutable map/mob templates，再進 lock 寫 field。
- 對首次載入做非玩家請求驅動的背景預熱與 metrics，避免第一位玩家替整個 server 付 I/O 成本。

### 7. 【中】熱路徑 logging 走預設 Generic Host provider，沒有 async/buffered logger；`Information` log 太靠近封包時序

位置：

- `src/Maple.Host.Login/Program.cs:6` 使用 `Host.CreateApplicationBuilder(args)`。
- `src/Maple.Host.Login/appsettings.json:18` - `src/Maple.Host.Login/appsettings.json:22` `Default` log level 是 `Information`。
- `src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:141` - `src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:159` 登入成功路徑在送 CHOOSE_GENDER/AuthSuccess/ServerList 前後有 Information log。
- `src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:238` - `src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs:240` 角色列表送包後 Information log。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1208` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1219` 進圖查角色與 SET_FIELD 前後有 Information log。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:531`、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:593`、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:612` 地圖物件 replay 以 Information 記錄。
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1261` - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1263` 一般聊天送自己、廣播後 Information log 聊天內容。

為何是雷：

程式碼沒有看到 `ClearProviders`、Serilog async sink、NLog async wrapper 或自訂 buffered logger。Generic Host 預設 logging provider 在 console/file sink 下通常不是遊戲封包時序友善；若 console sink 被啟用，Information 級別的登入、進圖、聊天、地圖 replay log 會出現在熱路徑。這與專案前科「Console log 同步印在送 socket 之前曾破壞時序」屬同一類風險。

具體修法：

- production/smoke 使用 async/buffered logger；例如 provider 外層加 bounded queue，sink 在背景寫 console/file。
- 將移動/攻擊/聊天/進圖 replay 類 log 降到 `Debug` 或採樣；Information 保留 lifecycle 與錯誤摘要。
- 嚴禁在「關鍵送包前」做 console/file log；需要 trace 時寫入非阻塞 ring buffer，再由背景 dump。
- 對 logger queue 滿時採 drop/sampling，不反壓 socket handler。

### 8. 【中】Guild registry 在全域 gate 內 await repository I/O，公會相關封包會互相串住

位置：

- `src/Maple.Application/Guilds/GuildService.cs:93` `InMemoryGuildRegistry` 使用單一 `SemaphoreSlim _gate`。
- `src/Maple.Application/Guilds/GuildService.cs:140` - `src/Maple.Application/Guilds/GuildService.cs:170` `CreateGuildAsync` 取得 gate 後 await `_repository.AddAsync(...)`。
- `src/Maple.Application/Guilds/GuildService.cs:476` - `src/Maple.Application/Guilds/GuildService.cs:500` `SetMemberOnlineAsync` 取得 gate 後 await `_repository.UpdateAsync(...)`。
- `src/Maple.Application/Guilds/GuildService.cs:586` - `src/Maple.Application/Guilds/GuildService.cs:594` `EnsureLoadedAsync` 取得 gate 後 await `_repository.GetAllAsync(...)`。
- `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:147` 玩家登入時更新公會上線狀態。
- `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:164` 玩家登出時更新公會離線狀態。

為何是雷：

這不是 sync-over-async 死鎖，但會把所有公會操作串在同一把 gate 後面。Mongo provider 下會釋放 thread 但仍序列化公會封包；LiteDB provider 下 `_repository.UpdateAsync` 其實同步磁碟 I/O，會在 gate 內阻塞 thread。玩家登入/登出若有公會也會走這條路。

具體修法：

- gate 只保護記憶體狀態變更與 snapshot 產生；持久化 command 在釋放 gate 後送到 persistence queue。
- 對上線/離線 presence 不要每次立刻持久化整份 guild；改成記憶體狀態 + 週期性 flush，或只持久化真正需要跨重啟保存的 guild metadata。
- 第一次 `EnsureLoadedAsync` 放到 server startup warmup，不讓第一個公會封包觸發全量載入。

### 9. 【低】listener 使用 fire-and-forget session task，例外有 catch，但沒有追蹤 in-flight sessions

位置：

- `src/Maple.Net/TcpLoginListener.cs:44` `_ = HandleAsync(socket, stoppingToken)`。
- `src/Maple.Net/TcpLoginListener.cs:70` - `src/Maple.Net/TcpLoginListener.cs:72` session task 內部 catch/log 例外。
- `src/Maple.Net/TcpChannelListener.cs:44` `_ = HandleAsync(socket, stoppingToken)`。
- `src/Maple.Net/TcpChannelListener.cs:69` - `src/Maple.Net/TcpChannelListener.cs:71` session task 內部 catch/log 例外。

為何是雷：

這不是未觀測例外問題，因為 `HandleAsync` 內部 catch 了非取消例外。但 BackgroundService 沒有保存 task/session 清單，stop 時無法 drain 或統一取消仍在做 DB flush/socket send 的連線。`V113ChannelConnectionHandler` finally 又用 `CancellationToken.None` 做登出 flush：`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:461`、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:471`，若 DB/socket 卡住，host shutdown 可見度不足。

具體修法：

- listener 維護 `ConcurrentDictionary<int, Task>` / session registry，`StopAsync` 時關閉 session、取消 send loop、限時 await drain。
- 登出 flush 改成有 timeout 的 server shutdown token，或交給 persistence queue；不要無限期 `CancellationToken.None` await 外部 I/O。
- 保留 catch/log，但把 session lifecycle 變成可觀測 metrics：active sessions、send queue depth、flush pending。

## 未列為問題的項目

- 未找到 `.Result`、`.Wait()`、`.GetAwaiter().GetResult()`。
- 未找到 `async void`。
- 未找到不必要的 `Task.Run` 包同步碼。
- Mongo repository 熱路徑大多使用 MongoDB Driver async API，例如 `FirstOrDefaultAsync` / `ToListAsync` / `InsertOneAsync` / `ReplaceOneAsync`，且多數 repository 內部有 `ConfigureAwait(false)`。
- handler 層缺少 `ConfigureAwait(false)` 不列為缺陷；Generic Host socket service 沒有 UI/ASP.NET SynchronizationContext，這裡的主要問題不是 continuation 回同步化內容，而是 handler 中 await 的工作本身太重或是假 async。

## I/O/async 體質總評

MapleForge 已避開 OdinMS 最典型的 sync-over-async 死鎖型問題：socket read/write 使用 async，沒有 `.Result/.Wait/GetResult`，也沒有 `async void`。但目前「非阻塞」只到 API 表面；封包 callback 是逐包串行，任何在 callback 中 await 的 DB、檔案、logger、遠端 socket 寫入，都會變成該連線的封包延遲。

最大體質問題是送包架構：沒有 per-connection outbound queue，且 `SendAsync` 會改寫 caller buffer。這同時是效能反壓問題與封包正確性問題，優先級高於單點 DB 調整。

第二層風險是資料載入策略：LiteDB 假 async、NPC script 首次讀檔、WZ 首次載入都可能在玩家封包觸發時同步打磁碟。這些應移到啟動 warmup、背景 loader 或 persistence actor。

logging 目前沒有看到 async/buffered sink；在 `Information` 預設等級下，登入、進圖、聊天等熱路徑 log 仍有踩回舊 Console 同步時序坑的風險。

建議修復順序：

1. 修 `MapleSession.SendAsync` 不改 caller buffer，並導入 per-session send queue。
2. 把廣播改成 enqueue 到目標 session，不在發送者 handler 等待目標 socket。
3. 禁止 LiteDB provider 參與多人壓測；建立 persistence queue 或改用 Mongo async provider。
4. 啟動 warmup NPC script / WZ map/mob data，移除封包 handler 內首次檔案 I/O。
5. 導入 async/buffered logging，並降低熱路徑 Information log。
