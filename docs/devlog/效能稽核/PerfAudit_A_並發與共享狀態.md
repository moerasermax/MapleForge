# MapleForge 效能稽核 A：並發與共享狀態

稽核日期：2026-06-06  
稽核分支：master  
稽核範圍：`tools/MapleForge.Analyzers`、`src/Maple.Core`、`src/Maple.Application`、`src/Maple.Adapters.V113`、`src/Maple.Net`、`src/Maple.Host.Shared`

## 摘要

MapleForge 已經避開 OdinMS 最典型的「到處 static manager」寫法：Core/Application 的 analyzer 已接入，主流程也把 `V113ChannelConnectionHandler` 做成 singleton 但把角色、玩家、Field、NPC 對話等狀態放在每連線區域變數。不過，現有碼在並發體質上還沒達到「重寫架構提升效能」的標準：Field actor/命令佇列尚未落地，registry 多處仍是 process-wide singleton，可變 `Character`/`Player` 物件會跨連線共享，且封包送出會原地改寫 `byte[]`，這會直接破壞 fanout 廣播。

## 發現

### 【嚴重度 高】`SendAsync` 原地加密會污染共用廣播封包

檔案:行：
- `src/Maple.Net/MapleSession.cs:55`
- `src/Maple.Net/MapleSession.cs:62`
- `src/Maple.Net/MapleSession.cs:64`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1276`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1283`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1289`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1291`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1292`
- `src/Maple.Adapters.V113/Channel/V113ChatHandler.cs:78`
- `src/Maple.Adapters.V113/Channel/V113ChatHandler.cs:83`
- `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:517`
- `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:528`

為何是效能/正確性雷：

`MapleSession.SendAsync(byte[] packet, ...)` 在持有 per-session `_sendLock` 後直接 `_send.Crypt(packet)`，也就是把呼叫端傳進來的 plaintext buffer 改成 ciphertext。多個廣播路徑會把同一個 `byte[] packet` 依序送給多個 session，例如地圖廣播、聊天、公會廣播。第一個 session 送出後，第二個 session 看到的已不是原封包，而是被第一個 session cipher 改寫過的 buffer；`BroadcastPacketToMapAsync` 更會先送自己、再用同一個 buffer 廣播給其他人。

這不是單純效能問題，而是 fanout 正確性問題：移動、聊天、攻擊、掉落等高頻路徑都可能讓第一個接收者以後的接收者拿到錯封包。`_sendLock` 只保護同一條連線的 cipher 狀態，不能保護跨 session 共用的 packet buffer。

具體修法：

- 把 `MapleSession.SendAsync` 改成不改呼叫端資料：介面改 `ReadOnlyMemory<byte>` / `ReadOnlySpan<byte>`，在方法內配置或租用 payload buffer，對副本加密後寫入 frame。
- 短期止血：所有 fanout 送出前 `packet.ToArray()`，但這會提高熱路徑配置量；根本修法應在 `SendAsync` 邊界保證 immutability。
- 補一個兩個 fake session 共用同一個 plaintext packet 的測試，確認第一次 `SendAsync` 後原始 buffer 不變。

### 【嚴重度 高】登入/重連的 stale session cleanup 會刪掉新 session 與新 Field 玩家

檔案:行：
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:10`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:17`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:21`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:23`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:14`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:17`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:20`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:24`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:204`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:226`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:448`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:452`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:481`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:483`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:490`

為何是效能/正確性雷：

online registry、map session registry、field object 都以 `characterId` / `objectId` 當唯一鍵，但沒有 session ownership token。若同角色快速重連、雙開、或舊連線 finally 比新連線晚執行，舊 session 的清理會：

- `_onlinePlayers.Deregister(charId)` 移除新 session 的 `OnlinePlayer`。
- `_mapRegistry.Deregister(mapId, charId)` 移除新 session 的 map send delegate。
- `currentField.Remove(player.ObjectId)` 以角色 id 移除 Field 內的新 `Player` 物件。

這會造成玩家明明在線卻從好友/組隊/公會查詢中消失、地圖廣播漏送，甚至被舊連線從 field 移掉。這是典型共享 registry 沒有 ownership/version 的 race。

具體修法：

- `OnlinePlayer`、`MapPlayerEntry` 加 `SessionId` 或 `Generation`，`Register` 回傳 token；`Deregister` 必須帶 token，只移除目前值仍屬於該 token 的 entry。
- `FieldInstance.Remove` 改成可驗證物件身份，例如 `RemoveIfSame(int objectId, IFieldObject expected)`；或由 field actor 根據 session token 執行離場命令。
- 登入流程要定義 duplicate login 策略：拒絕新連線、踢舊連線、或新連線接管，但 registry cleanup 必須能分辨新舊。

### 【嚴重度 高】`FieldInstance` 宣稱要由 field-actor 序列化，但實際用 scattered `lock(field)` 與無鎖移動

檔案:行：
- `src/Maple.Core/World/FieldInstance.cs:6`
- `src/Maple.Core/World/FieldInstance.cs:11`
- `src/Maple.Core/World/FieldInstance.cs:18`
- `src/Maple.Core/World/FieldInstance.cs:21`
- `src/Maple.Core/World/FieldInstance.cs:26`
- `src/Maple.Core/World/FieldInstance.cs:35`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:537`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:580`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:600`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:901`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1033`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1059`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1114`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1228`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1235`

為何是效能/正確性雷：

`FieldInstance` 註解明確說「領域變更應由上層 field-actor/命令佇列序列化」，但現有 channel handler 是多連線 task 直接對同一個 `FieldInstance` 操作，靠外部 `lock(field)` 保護部分路徑。這有三個問題：

- 同步責任外洩：`CombatService` / `DropService` 等 public 方法本身不要求鎖，未來新呼叫點很容易漏鎖。
- 鎖對象是 domain object 本身，任何外部程式都能 `lock(field)`，之後容易出現鎖順序不明或意外長時間持鎖。
- 移動更新 `player.MoveTo(...)` 完全不進 field lock；目前讀者少所以症狀不明顯，但一旦 AOI、怪物 AI、技能範圍判定開始讀玩家位置，就會變成資料競爭。

具體修法：

- 優先落地 per-field actor / command queue：每張 field 一條序列化命令流，所有進出圖、移動、戰鬥、掉落拾取、AOI 更新都排進該 field。
- 若暫不做 actor，至少把鎖封裝在 `FieldInstance` 內部：提供 `AddPlayer`、`RemoveIfSame`、`SnapshotMobs`、`MutateForAttack` 等方法，禁止外部 `lock(field)`。
- 玩家位置也要納入同一個 field concurrency model；不要讓 owning session 直接無鎖寫共享 `Player`。

### 【嚴重度 高】公會 registry 是全域 semaphore，且在鎖內等待 repository I/O

檔案:行：
- `src/Maple.Application/Guilds/GuildService.cs:93`
- `src/Maple.Application/Guilds/GuildService.cs:94`
- `src/Maple.Application/Guilds/GuildService.cs:95`
- `src/Maple.Application/Guilds/GuildService.cs:96`
- `src/Maple.Application/Guilds/GuildService.cs:140`
- `src/Maple.Application/Guilds/GuildService.cs:141`
- `src/Maple.Application/Guilds/GuildService.cs:170`
- `src/Maple.Application/Guilds/GuildService.cs:218`
- `src/Maple.Application/Guilds/GuildService.cs:254`
- `src/Maple.Application/Guilds/GuildService.cs:351`
- `src/Maple.Application/Guilds/GuildService.cs:389`
- `src/Maple.Application/Guilds/GuildService.cs:423`
- `src/Maple.Application/Guilds/GuildService.cs:457`
- `src/Maple.Application/Guilds/GuildService.cs:500`
- `src/Maple.Application/Guilds/GuildService.cs:586`
- `src/Maple.Application/Guilds/GuildService.cs:594`

為何是效能/正確性雷：

`InMemoryGuildRegistry` 用單一 `_gate = new SemaphoreSlim(1, 1)` 保護所有公會、所有邀請與角色索引，而且多個方法在 `_gate` 內 `await _repository.UpdateAsync(...)` / `AddAsync(...)` / `GetAllAsync(...)`。這會把所有公會讀寫串成一條全域隊列，並且把 DB/LiteDB/Mongo 延遲也算進鎖持有時間。

影響不只公會操作本身：登入/登出會呼叫 `SetMemberOnlineAsync`，公會聊天查收件人會呼叫 `GetGuildForCharacterAsync`。只要一個公會更新正在鎖內等 I/O，其他公會的登入、登出、聊天收件人查詢都會被阻塞。

具體修法：

- `_gate` 只保護 in-memory 索引更新；持久化用 snapshot/version 在鎖外執行，失敗時用補償或 dirty queue。
- 改 per-guild lock/actor：全域索引只做 guildId/name/characterId 對應，具體成員異動進該 guild 的命令序列。
- `EnsureLoadedAsync` 在服務啟動或第一次載入時獨立完成；不要在全域 gate 內做長時間 repository `GetAllAsync`。

### 【嚴重度 中】組隊 registry 是全域大鎖，雖然短小但仍是 OdinMS 式集中瓶頸

檔案:行：
- `src/Maple.Application/Parties/PartyService.cs:73`
- `src/Maple.Application/Parties/PartyService.cs:74`
- `src/Maple.Application/Parties/PartyService.cs:75`
- `src/Maple.Application/Parties/PartyService.cs:86`
- `src/Maple.Application/Parties/PartyService.cs:102`
- `src/Maple.Application/Parties/PartyService.cs:126`
- `src/Maple.Application/Parties/PartyService.cs:161`
- `src/Maple.Application/Parties/PartyService.cs:206`
- `src/Maple.Application/Parties/PartyService.cs:244`
- `src/Maple.Application/Parties/PartyService.cs:276`

為何是效能/正確性雷：

`InMemoryPartyRegistry` 用單一 `_gate` 保護所有 party 與角色索引。好處是目前沒有鎖內 I/O，且 party 成員最多 6 人，所以短期不會像 guild 一樣嚴重；但所有 party 建立、邀請檢查、加入、離開、換隊長、查詢都會互斥，仍是 process-wide 粗粒度鎖。

具體修法：

- 用 `ConcurrentDictionary<int, PartyRuntime>` 管 party，`ConcurrentDictionary<int, int>` 管 character -> party。
- 每個 party 自己一把鎖或 actor；跨 party 的操作極少，無需全域互斥。
- party snapshot 保持 immutable，讀路徑可以拿最近 snapshot，寫路徑才進 per-party lock。

### 【嚴重度 中】`OnlinePlayerRegistry` 雙 `ConcurrentDictionary` 無法提供雙索引原子性

檔案:行：
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:7`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:8`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:12`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:17`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:18`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:28`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:39`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:41`
- `src/Maple.Application/OnlinePlayers/InMemoryOnlinePlayerRegistry.cs:42`

為何是效能/正確性雷：

`_byId` 與 `_idByName` 各自 thread-safe，但「角色 id 與名字索引一致」不是原子操作。`Register` 會先移除舊名字，再更新 `_byId`，最後更新 `_idByName`；`Deregister` 也分多步。並發查詢 `FindByName` 可能讀到短暫缺口或 stale name -> id，再回到 `_byId` 取到已換名/換 session 的 player。

具體修法：

- 若線上 registry 寫入頻率低，直接用一把小鎖保護雙索引更新，讀取回傳 immutable snapshot。
- 若要 lock-free，加入 generation/session token，`FindByName` 取到 id 後要驗證 `OnlinePlayer.Name` 仍等於查詢名稱。
- `Deregister` 不應只帶 `characterId`；要帶 register token，避免舊連線刪掉新連線。

### 【嚴重度 中】戰鬥/掉落在 per-field lock 內做 O(N) 掃描與多段領域工作

檔案:行：
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1033`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1035`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1059`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1061`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1114`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1116`
- `src/Maple.Application/Drops/DropService.cs:128`
- `src/Maple.Application/Drops/DropService.cs:135`
- `src/Maple.Application/Drops/DropService.cs:159`
- `src/Maple.Application/Drops/DropService.cs:177`
- `src/Maple.Application/Drops/DropService.cs:184`
- `src/Maple.Application/Drops/DropService.cs:186`

為何是效能/正確性雷：

攻擊 handler 在 `lock(field)` 內套用傷害；怪物死亡時會同步產生掉落。`DropService.AllocateDropObjectId` 每次配發掉落物件 id 都會掃 `field.Objects` 找 max，再 `field.Get` 檢查碰撞。這代表 field 上物件越多、掉落越多，擊殺時持有 field lock 越久。雖然目前沒有在 field lock 內做 socket I/O，這點是好的，但 per-field lock 內仍有 catalog 查詢、隨機掉落、EXP/背包/掉落生成與 O(N) 掃描。

具體修法：

- 每個 `FieldInstance` 維護 monotonic object id allocator，配發掉落/NPC/mob id 改 O(1)；若 actor 落地，allocator 只在 field actor thread 中更新。
- 先在鎖外解析攻擊封包、查靜態掉落表；鎖內只做「確認怪仍活著、扣血、插入掉落、移除怪」這種必須原子的 field mutation。
- 更完整的做法是把戰鬥也變成 field command，避免 handler 自己決定鎖範圍。

### 【嚴重度 中】移動/AOI fanout 是全地圖 O(players) 且逐一 await，慢接收者會拖住 sender handler

檔案:行：
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:248`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:250`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:251`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1266`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1272`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1278`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1283`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:28`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:31`
- `src/Maple.Net/MapleSession.cs:59`
- `src/Maple.Net/MapleSession.cs:66`
- `src/Maple.Net/MapleSession.cs:67`

為何是效能/正確性雷：

`MOVE_PLAYER` 是高頻封包。現有流程每次移動都：

- 解析後無鎖寫 `Player.Position`。
- 建一個移動廣播封包。
- `_mapRegistry.GetOthers` 從整張地圖的 `ConcurrentDictionary.Values` 做 `Where(...).ToList()`。
- 逐一 `await other.SendPacket(packet, ct)`。

這沒有 AOI/cell/視野切分；同地圖玩家越多，每個玩家移動成本越高。逐一 await 也代表任一目標 session 的 send lock 或 socket write 慢，都會拉長發送者的封包處理時間。加上前述 `SendAsync` 會改寫 packet buffer，這條路徑目前同時有正確性與效能風險。

具體修法：

- Field actor 內維護 AOI/cell 訂閱者集合，移動只 fanout 給可見區域玩家。
- 每個 session 建 outbound queue，廣播路徑只 enqueue；socket writer 單獨消化，慢 client 以 bounded queue/backpressure/drop movement update 處理。
- `IMapSessionRegistry.GetOthers` 回傳可重用 snapshot 或讓 field actor 維護 watcher list，避免每個移動封包分配 `List`。

### 【嚴重度 中】per-channel/per-field 隔離尚未真正完成，registry key 只用 `mapId`

檔案:行：
- `src/Maple.Host.Shared/MapleServerHost.cs:90`
- `src/Maple.Host.Shared/MapleServerHost.cs:91`
- `src/Maple.Host.Shared/MapleServerHost.cs:115`
- `src/Maple.Host.Shared/MapleServerHost.cs:162`
- `src/Maple.Application/Maps/InMemoryFieldInstanceRegistry.cs:8`
- `src/Maple.Application/Maps/InMemoryFieldInstanceRegistry.cs:10`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:12`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:14`

為何是效能/正確性雷：

目前 host 只註冊一個 `V113ChannelOptions(ChannelIndex: 0)`，Field registry 與 MapSession registry 也只以 `mapId` 分組。這在單 process 單 channel 時能運作；但若未來同 process 放多 channel、多 world、或多 ServerInstance，`mapId` 相同的地圖會共用同一個 `FieldInstance` 與廣播 registry，跨頻道玩家會互相看到與互相競爭同一份怪物/掉落狀態。

具體修法：

- registry key 改成 `(serverInstanceId/worldId, channelId, mapId)`，或更乾淨地把 field/session registry 放到 per-channel DI scope。
- `IOnlinePlayerRegistry` 可以是跨 channel 中央 registry，但 entry 必須帶 channel/server instance；Field 與 MapSession 不應是純 process-global mapId key。
- 若目前設計刻意單 channel，文件與設定要明確寫死，避免未來擴 channel 時踩同一份 registry。

### 【嚴重度 中】跨玩家服務直接改遠端 `Character` / `BuddyList`，缺少角色層同步模型

檔案:行：
- `src/Maple.Application/OnlinePlayers/IOnlinePlayerRegistry.cs:5`
- `src/Maple.Application/OnlinePlayers/IOnlinePlayerRegistry.cs:9`
- `src/Maple.Application/Buddies/BuddyService.cs:137`
- `src/Maple.Application/Buddies/BuddyService.cs:147`
- `src/Maple.Application/Buddies/BuddyService.cs:166`
- `src/Maple.Application/Buddies/BuddyService.cs:207`
- `src/Maple.Application/Buddies/BuddyService.cs:226`
- `src/Maple.Application/Buddies/BuddyService.cs:229`
- `src/Maple.Application/Buddies/BuddyService.cs:314`
- `src/Maple.Adapters.V113/Channel/CentralGuildSessionHook.cs:50`
- `src/Maple.Adapters.V113/Channel/CentralGuildSessionHook.cs:56`
- `src/Maple.Adapters.V113/Channel/CentralGuildSessionHook.cs:57`
- `src/Maple.Adapters.V113/Channel/CentralGuildSessionHook.cs:58`

為何是效能/正確性雷：

`OnlinePlayer` 直接暴露 live `Character` 物件。BuddyService 在 A 玩家封包處理中會找 B 的 `OnlinePlayer`，然後直接修改 B 的 `Character.BuddyList`；Guild session hook 也直接改線上角色的 `GuildId/GuildRank/AllianceRank`。這些 mutation 不是送到 B 的 session/角色 actor，而是在任意連線 task 上發生。

目前因角色封包處理是單 session receive loop，所以「自己改自己」大多是序列化的；但「別人改我」會與我的 session 同時讀寫 `Character` 的 List/欄位，尤其好友、聊天、登入登出 presence 高峰會有資料競爭。

具體修法：

- 不要在 `OnlinePlayer` 暴露 mutable `Character`；改存 immutable presence snapshot + `SendPacket` + `CharacterActor`/command mailbox。
- 跨玩家狀態變更送到目標角色 actor 執行，或至少 per-character lock 保護 `Character` 聚合。
- repository 更新與線上 runtime 更新要有一致流程：先角色 actor mutation，再產生封包與 persistence dirty event。

### 【嚴重度 低】static analyzer 守住了 Core/Application 的明顯 static mutable，但仍有規則洞

檔案:行：
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:10`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:12`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:41`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:49`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:50`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:64`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:71`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:79`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:103`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:113`
- `tools/MapleForge.Analyzers/StaticMutableFieldAnalyzer.cs:114`
- `src/Maple.Core/Maple.Core.csproj:11`
- `src/Maple.Application/Maple.Application.csproj:19`
- `src/Maple.Adapters.V113/Crypto/V113CryptoConstants.cs:12`
- `src/Maple.Adapters.V113/Crypto/V113CryptoConstants.cs:21`

為何是效能/正確性雷：

Core/Application 目前沒有找到非 readonly 的 static mutable 欄位；analyzer 也確實以 Analyzer project reference 接入 Core/Application。這點是正面結果。

但 MF0001 仍有幾個洞：

- 只守 `Maple.Core` / `Maple.Application` namespace，Adapter/Content/Net 不在規則內。
- `readonly` 欄位直接豁免；`static readonly byte[]`、`static readonly List<T>` 這類「參照 readonly、內容可變」不會被擋。
- static get-only property 直接豁免；若回傳 singleton mutable object 也不會被擋。
- `V113CryptoConstants.AesKey` 與 `FunnyBytes` 是 `public static readonly byte[]`，目前未看到寫入者，但 API 形狀允許 assembly 內任意程式碼改內容。

具體修法：

- analyzer 改用 semantic type 判斷：static readonly 若型別是 array、`ICollection<T>`、`IDictionary<K,V>`、mutable collection 類型，仍報錯，除非型別是 `ImmutableArray<T>` / `FrozenDictionary` / `ReadOnlyMemory<T>` 等不可變表示。
- 擴大保護範圍到 `Maple.Adapters.V113` 的 crypto/packet 常數，至少擋 public/internal mutable static。
- `V113CryptoConstants` 改成 `ReadOnlySpan<byte>` property、`ReadOnlyMemory<byte>`、或 `ImmutableArray<byte>`；傳給 AES 時複製。

### 【嚴重度 低】`WzDataNode` 手寫 lazy cache 沒同步，會在並發讀取時重複建構

檔案:行：
- `src/Maple.Content/Wz/WzDataNode.cs:13`
- `src/Maple.Content/Wz/WzDataNode.cs:28`
- `src/Maple.Content/Wz/WzDataNode.cs:32`
- `src/Maple.Content/Wz/WzDataNode.cs:37`
- `src/Maple.Content/Wz/WzDataNode.cs:45`
- `src/Maple.Content/Wz/WzDataProvider.cs:16`
- `src/Maple.Content/Wz/WzDataProvider.cs:74`

為何是效能/正確性雷：

`WzDataProvider` 是 process 級快取，會被多連線共用。`WzDataNode.Children` 用 `_children is not null` 判斷後直接建字典並賦值，沒有 lock/volatile/Lazy。因為建出的字典是 read-only wrapper，通常只會造成重複建構與 benign race；但這仍是共享 singleton 讀路徑上的未定義同步模型。

具體修法：

- 改成 `Lazy<IReadOnlyDictionary<string, IDataNode>>`，`LazyThreadSafetyMode.ExecutionAndPublication` 或 `PublicationOnly`。
- 或用 private lock + double-check，並讓 `_children` 以 `Volatile.Read/Write` 發布。
- Content 層讀取不是封包熱路徑中的每 tick 操作，優先度低於 Field/session 問題。

## 熱路徑鎖觀察

- `MapleSession.SendAsync` 使用 per-session `_sendLock`，這是必要的，因為 send cipher 有 IV 狀態；它不是 OdinMS 式全域鎖。不過目前鎖內做加密、socket write、flush，fanout 路徑逐一 await 會被慢 session 反壓。
- `V113ChannelConnectionHandler` 的 field locks 多數沒有包住 socket I/O，這點是正確方向；`SendFieldMonstersAsync` / `SendFieldDropsAsync` 也先在鎖內 snapshot 再送封包。
- 最大熱點不是「持全域鎖做 I/O」，而是「Field actor 未落地 + fanout 同步發送 + packet buffer 可變」。

## 零-static 結論

Core/Application 的「非 readonly static mutable」目前看起來守住了；`MF0001` 已接入 `Maple.Core.csproj` 與 `Maple.Application.csproj`。但「零 static」不是等於「零共享可變狀態」：DI singleton 內的 registry、live `Character` 物件、`FieldInstance`、packet byte buffer 都是共享可變狀態，而且目前有幾個高嚴重度 race。

## 並發體質總評

相較 OdinMS，MapleForge 已有明顯改善：沒有看到大量 global static manager，封包 handler 的 per-connection 狀態也沒有放在 singleton 欄位上；MapSession/Online registry 至少使用了 `ConcurrentDictionary`，Field 操作也嘗試縮到 per-field lock，而不是全伺服器大鎖。

但目前還不能說已經擺脫 OdinMS 的並發問題。缺口主要是四個：

1. Field actor/命令佇列還停在註解，實際同步是外部 scattered `lock(field)`。
2. registry 缺 session ownership token，重連/舊連線清理會刪新狀態。
3. guild/party 仍是 process-wide 粗粒度同步，其中 guild 還在鎖內做 repository I/O。
4. fanout 封包 buffer 被 `SendAsync` 原地加密，這是目前最急的正確性缺陷。

建議修復順序：先修 `SendAsync` 不改 caller buffer；再替 Online/MapSession/Field cleanup 加 session token；接著落地 per-field actor 或封裝 FieldInstance 鎖；最後拆 guild/party registry 的全域鎖與導入 AOI/outbound queue。
