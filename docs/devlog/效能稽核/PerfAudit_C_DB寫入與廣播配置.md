# PerfAudit C - DB 寫入與廣播配置

稽核範圍：`src/Maple.Persistence`、`src/Maple.Core` repository/flush 模型、Application services 的存檔點，以及 v113 channel/map/chat/party/guild broadcast 路徑。

## 結論速覽

MapleForge 目前沒有重現 OdinMS 最嚴重的「移動、聊天、撿物、每次加經驗都直接打 DB」模式；大多數玩家動作只改記憶體，最後在 channel logout 時保存。真正的 DB 風險是另一種：repository 只有整份 `Character`/`Account`/`Guild` 文件替換，沒有 dirty tracking、欄位級更新、批次或週期 flush。任何中途保存點都會序列化整包角色/帳號，而且若 `Player.Inventory` 已改但 `Character.Items` 尚未 flush，整包保存可能把舊 snapshot 寫回 DB。

廣播路徑更急：地圖/公會/聊天等 fanout 會重用同一個 `byte[]`，但 `MapleSession.SendAsync` 會原地加密傳入的 buffer。這不是單純效能問題，而是會讓第二個以後的收件人拿到被前一個 session cipher 改寫過的 payload。

## DB 寫入頻率矩陣

| 動作 | 是否立即 DB 寫 | 寫入粒度 | 證據 |
| --- | --- | --- | --- |
| 移動 | 否 | 僅更新 `Player.Position` | `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:248`、`:1228` |
| 普通地圖聊天 | 否 | 僅建封包與廣播 | `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1247` |
| 撿物/撿錢 | 否 | 物品會 flush 到 `Character.Items` 記憶體 snapshot；DB 等 logout | `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:883`、`src/Maple.Core/World/Player.Drops.cs:31` |
| 打怪加經驗 | 否 | 更新 `Character.Exp`/stats，DB 等 logout | `src/Maple.Application/Drops/DropService.cs:64`、`src/Maple.Core/World/Player.Stats.cs:210` |
| 背包移動/穿脫 | 否 | flush 到 `Character.Items` 記憶體 snapshot | `src/Maple.Adapters.V113/Channel/V113InventoryMoveHandler.cs:50`、`:59`、`:68` |
| NPC 商店買賣 | 否 | flush 到 `Character.Items` 記憶體 snapshot；DB 等 logout | `src/Maple.Application/Shops/ShopService.cs:96`、`:133` |
| 倉庫操作 | 關閉倉庫時寫 Account | 整份 `Account` 文件 | `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:997` |
| Cash Shop 購買 | 是 | 整份 `Account` + 整份 `Character` | `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:426`、`:431` |
| 公會建立/加入/離開/改徽章 | 是 | 整份 `Character`，公會 registry 也整份 `Guild` | `src/Maple.Application/Guilds/GuildService.cs:736`、`:769`、`:784`、`:843` |
| 登出 | 是 | 整份 `Account` + 整份 `Character` | `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:454`、`:461`、`:471` |

## 發現

### DB-01【高】Repository 只有整份文件替換，沒有增量/dirty tracking

位置：
- `src/Maple.Core/Characters/ICharacterRepository.cs:18`
- `src/Maple.Core/Accounts/IAccountRepository.cs:25`
- `src/Maple.Persistence/Characters/MongoCharacterRepository.cs:61`
- `src/Maple.Persistence/Accounts/MongoAccountRepository.cs:63`
- `src/Maple.Persistence/Characters/LiteDbCharacterRepository.cs:45`
- `src/Maple.Persistence/Accounts/LiteDbAccountRepository.cs:59`

為何是雷：
`ICharacterRepository.UpdateAsync(Character)` 和 `IAccountRepository.UpdateAsync(Account)` 沒有欄位級 API。Mongo 使用 `ReplaceOneAsync(..., new ReplaceOptions { IsUpsert = false })`，LiteDB 使用 `_col.Update(character)` / `_collection.Update(account)`。這不是 upsert，但成本仍是「完整根文件序列化 + 完整替換」。當角色文件變大後，任何只改 `Meso`、`GuildRank`、`LastLoginAt` 的操作都會帶上背包、裝備、技能、任務、好友等全部資料。

具體修法：
建立持久化 dirty layer，例如 `CharacterDirtyFlags` + `AccountDirtyFlags`，提供 `UpdateStatsAsync`、`UpdateInventoryAsync`、`UpdateQuestAsync`、`UpdateGuildStatusAsync`、`UpdateStorageAsync` 等欄位級 repository 方法。Mongo 用 `$set`/`$inc`/array targeted update；LiteDB 若仍只能整文件，至少在 application 層合併 dirty flush、節流寫入，避免每個社交/商城操作直接全文件替換。

### DB-02【高】Cash Shop 成功購買會即時整包保存 Account + Character

位置：
- `src/Maple.Application/CashShop/CashShopService.cs:87`
- `src/Maple.Application/CashShop/CashShopService.cs:88`
- `src/Maple.Adapters.V113/Channel/V113CashShopPackets.cs:156`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:426`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:431`

為何是雷：
Cash shop 購買成功後，handler 依 `AccountMutated` 與 `CharacterMutated` 立刻呼叫 `_accounts.UpdateAsync(account)` 與 `_charService.UpdateAsync(player.Character)`。因 repository 是整份文件替換，一次購買至少序列化帳號現金點數與整份帳號倉庫，再序列化整份角色資料。若玩家連續購買多件現金道具，會變成多次同步的全文件 DB roundtrip。

具體修法：
把 cash shop 改成交易型增量寫：帳號點數用 `$inc` 或欄位更新，新增 cash item 用 inventory delta 寫入。若要保留「購買即時保存」語意，至少以單一 `CashPurchaseCommitAsync(accountId, characterId, currencyDelta, addedItem)` 包成一筆 DB transaction/批次；不要兩個根文件各自 full replace。LiteDB 模式下也應合併為單一批次 commit，並記錄購買流水以便崩潰補償。

### DB-03【中】沒有週期 flush 或批次寫入；登出才保存多數熱動作

位置：
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:454`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:461`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:471`
- `src/Maple.Core/World/Player.cs:65`

為何是雷：
移動、撿物、打怪加經驗、背包移動、商店買賣通常不打 DB，這對效能有利，但目前沒有看到背景 checkpoint、dirty queue 或 debounced flush。玩家斷線前 process 崩潰時，除了少數已即時保存的 cash/guild/storage close 狀態，大量進度會丟失。反過來，一旦為了可靠性補上「動作後立即保存」，又會因 DB-01 變成 full replace 風暴。

具體修法：
建立 per-player dirty accumulator，將 stats/inventory/quest/storage 等 dirty bit 加入 write-behind queue。建議 5-30 秒週期 flush、地圖切換/重要交易強制 flush、logout final flush。每次 flush 只提交 dirty 欄位；同一玩家同一欄位在週期內合併，並限制同時 DB 寫入數。

### DB-04【高】`Player.Inventory` 與 `Character.Items` 分離，部分路徑會留下 stale snapshot；中途整包保存可能覆寫資料

位置：
- `src/Maple.Core/World/Player.cs:24`
- `src/Maple.Core/World/Player.cs:66`
- `src/Maple.Application/Npc/NpcContext.cs:76`
- `src/Maple.Application/Guilds/GuildService.cs:736`
- `src/Maple.Application/Guilds/GuildService.cs:843`
- `src/Maple.Persistence/Characters/MongoCharacterRepository.cs:63`

為何是雷：
`Player` 的執行期背包是由 `Character.Items` hydrate 出來，只有呼叫 `FlushInventory()` 才把資料折回 `Character.Items`。多數物品路徑有 flush，例如撿物與商店；但 `NpcContext.GainItem` 只呼叫 `_player.GainItem(...)`，沒有 flush。若玩家拿了 NPC 腳本道具後立刻建立公會或改公會徽章，`GuildService` 會 `_characters.UpdateAsync(player.Character)`，repository 又是整份文件替換，可能把尚未 flush 的舊 `Character.Items` 寫回 DB。這是資料正確性與效能耦合在一起的問題。

具體修法：
讓 `Player` 持有單一權威狀態，repository commit 前由 `PlayerSnapshotBuilder` 統一產生最新 snapshot；不要讓 service 直接保存裸 `Character`。短期修補是所有可能改背包的 API 立即標 dirty 並更新 snapshot，尤其 `NpcContext.GainItem`。中期修法是 `CharacterRepository.UpdateGuildStatusAsync` 只更新 guild 欄位，不 full replace，避免 stale inventory 被帶出去。

### DB-05【中】整物件序列化成本會隨角色內容快速放大

位置：
- `src/Maple.Core/Characters/Character.cs:47`
- `src/Maple.Core/Characters/Character.cs:58`
- `src/Maple.Core/Characters/Character.cs:61`
- `src/Maple.Core/Characters/Character.cs:64`
- `src/Maple.Core/Characters/Character.cs:67`
- `src/Maple.Core/Characters/Character.cs:70`
- `src/Maple.Core/Accounts/Account.cs:38`
- `src/Maple.Core/Accounts/Account.cs:45`
- `src/Maple.Core/Inventory/ItemRecord.cs:8`

為何是雷：
一份 `Character` 文件包含技能、裝備、背包、好友、任務、quest info、公會欄位；一份 `Account` 包含 cash points、maple points、整份 storage。`ItemRecord` 對裝備還帶大量 stats 欄位。現在每次 `UpdateAsync` 都會序列化這些集合。角色越接近正式服資料量，`Character` full replace 的成本越接近 OdinMS「整物件寫回」問題。

具體修法：
拆 persistence aggregate：`characters` 保存基本 stats/map/guild，`character_items` 或 Mongo 子文件增量保存背包，`character_quests` 保存任務，`account_storage` 保存倉庫。若維持文件模型，至少引入欄位級更新與版本欄位，並測量 BSON/LiteDB 實際文件大小，對超過門檻的欄位拆表/拆 collection。

### DB-06【中】LiteDB repository 是同步 I/O 包成 Task，熱路徑會阻塞 caller

位置：
- `src/Maple.Persistence/Characters/LiteDbCharacterRepository.cs:45`
- `src/Maple.Persistence/Accounts/LiteDbAccountRepository.cs:59`
- `src/Maple.Persistence/Guilds/LiteDbGuildRepository.cs:40`

為何是雷：
LiteDB 寫入是同步 `_col.Update(...)` 後回 `Task.CompletedTask`。在 channel handler 中 await 這些方法時，其實是在當前 request 流程做同步磁碟/DB 工作。若 cash shop、公會、倉庫 close、logout 同時集中，會拉長 packet handling latency。

具體修法：
LiteDB 模式應加單獨 persistence worker queue，把同步寫入移出 packet handling call stack；同玩家寫入合併，並用 backpressure 限制佇列。正式壓測應以 Mongo 或其他可 async/批次的 DB 為主。

### BC-01【嚴重】廣播重用同一 `byte[]`，但 `SendAsync` 會原地加密，會破壞第二個以後收件人封包

位置：
- `src/Maple.Net/MapleSession.cs:55`
- `src/Maple.Net/MapleSession.cs:62`
- `src/Maple.Net/MapleSession.cs:64`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1260`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1276`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1289`
- `src/Maple.Adapters.V113/Channel/V113ChatHandler.cs:78`
- `src/Maple.Adapters.V113/Channel/V113GuildOperationHandler.cs:517`

為何是雷：
地圖聊天先 `session.SendAsync(packet)` 給自己，再 `BroadcastPacketToOthersAsync(..., packet)`；地圖攻擊、掉落、移動、公會廣播、群聊也會把同一 `byte[]` fanout。可是 `MapleSession.SendAsync` 會 `_send.Crypt(packet)`，直接修改 caller 傳入的 payload。第一個 session 送出後，原始封包已變成該 session 的 encrypted bytes；下一個 session 再拿這個 buffer 加密，payload 就不再是原始 plaintext。這會造成廣播封包錯亂，並且讓「封包建一次重用」的效能策略不可用。

具體修法：
先修正 `SendAsync` 的契約：接受 `ReadOnlyMemory<byte>`/`ReadOnlySpan<byte>`，在 session 內部租用或配置 frame buffer，先 copy plaintext 到 frame body，再只加密 frame body，不修改 caller buffer。高效版本用 `ArrayPool<byte>.Shared.Rent(packet.Length + 4)`，送完歸還；或使用 per-session reusable send buffer/`IBufferWriter<byte>`。修完後，地圖廣播才能安全地「packet 建一次，多收件人重用 plaintext」。

### BC-02【高】地圖 fanout 每次都 materialize 全地圖其他玩家，沒有 AOI；多人同時動會退化成總體 O(N^2)

位置：
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:28`
- `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs:31`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1276`
- `src/Maple.Core/World/FieldInstance.cs:35`

為何是雷：
`GetOthers` 每次從 `ConcurrentDictionary.Values` 做 `Where(...).ToList()`，沒有距離/畫面 AOI 篩選。單次移動是 O(N) fanout 與一次 list 配置；如果地圖內 N 人都在移動，一輪就是 O(N^2) send attempts。`FieldInstance.ObjectsInRange` 有範圍查詢 API，但目前廣播沒有使用，而且它本身也是線性掃描。

具體修法：
先把 `GetOthers` 改成可枚舉快照或 callback，避免每次 `ToList()`。接著建立 AOI grid / cell-based interest management，依視窗範圍或距離取收件人；移動時只通知鄰近玩家，玩家跨 cell 時才更新可見集合。壓測地圖應量測 N=30/100/300 時的 movement broadcast 成本。

### BC-03【中】進圖 spawn 對既有玩家的封包重複建構，且 guild lookup 在 fanout 迴圈內

位置：
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:229`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:232`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:235`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:551`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:562`

為何是雷：
新玩家進圖時，`spawnForOther` 是「同一個新角色給所有既有玩家看」的封包，理論上可建一次；目前在 `foreach (others)` 內每次 `BuildSpawnPlayerPacketAsync(chr, ...)`，而該方法可能查 guild service。`spawnForNew` 因每個 existing player 不同，需要逐人建，但 guild info 也可先 cache。

具體修法：
把新玩家給其他人的 spawn packet 移出迴圈建一次；`BuildSpawnPlayerPacketAsync` 回傳不可變 plaintext 後可安全重用，但前提是先修 BC-01。對既有玩家 spawn，批次取得 guild info 或在 online player entry 中快取 spawn guild info，避免每人 await。

### GC-01【高】每個封包都檢查過期 buff，內部 LINQ/GroupBy/ToArray 會在高頻 input 下製造 GC 壓力

位置：
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:182`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:167`
- `src/Maple.Core/World/Player.Skills.cs:158`
- `src/Maple.Core/World/Player.Skills.cs:162`
- `src/Maple.Adapters.V113/Channel/V113SkillPackets.cs:180`

為何是雷：
每個收到的 client packet 都會呼叫 `SendExpiredBuffCancelsAsync`。即使沒有 buff 過期，`CancelExpiredBuffs` 也會掃 `_activeBuffs.Values`，用 `Where`、`GroupBy`、`Select`、`ToArray` 建短命物件；若有結果，adapter 又 `.Select(...).ToArray()` 建封包陣列。移動封包本身很高頻，這個檢查會跟著放大。

具體修法：
在 `Player` 保存 `NextBuffExpiryAt`。只有 `now >= NextBuffExpiryAt` 才掃描；掃描時用手寫 loop 收集過期項，避免 LINQ/GroupBy。更好的方案是 per-player min-heap 或 timing wheel，由 channel tick/actor 定時推送取消封包，而不是掛在每個 client input 前。

### GC-02【中】封包產生與送出每次至少兩段配置，熱廣播會放大

位置：
- `src/Maple.Core/IO/PacketWriter.cs:14`
- `src/Maple.Core/IO/PacketWriter.cs:23`
- `src/Maple.Core/IO/PacketWriter.cs:97`
- `src/Maple.Net/MapleSession.cs:62`
- `src/Maple.Net/MapleSession.cs:100`
- `src/Maple.Adapters.V113/Channel/V113MapPackets.cs:111`
- `src/Maple.Adapters.V113/Channel/V113CombatPackets.cs:138`

為何是雷：
每個 outbound packet 先 `new PacketWriter` 分配內部 buffer，`ToArray()` 再 copy 成新的 `byte[]`；送出時 `new byte[packet.Length + 4]` 再 copy/encrypt。接收端每包 `new byte[length]`。在移動、攻擊、怪物傷害、掉落 spawn 等廣播熱路徑，這些短命 byte array 會直接轉成 Gen0 壓力。

具體修法：
把 packet encode 改成寫入 `IBufferWriter<byte>` 或租用 buffer，讓封包 builder 回傳 `IMemoryOwner<byte>`/`ReadOnlyMemory<byte>`。`SendAsync` 使用 pooled frame buffer，且不可修改 caller payload。短期可先讓 `PacketWriter` 支援 `ArrayPool<byte>` 與 `Dispose/Return`；長期把常用固定封包改為 stackalloc/Span 或預編碼模板。

### GC-03【中】背包 flush 與 ranged 消耗彈藥會重建清單/LINQ，攻擊頻率上來會抖

位置：
- `src/Maple.Core/Inventory/Inventory.cs:167`
- `src/Maple.Core/World/Player.RangedCombat.cs:53`
- `src/Maple.Core/World/Player.RangedCombat.cs:81`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs:1196`

為何是雷：
`Inventory.Flush()` 每次建立新的 `List<ItemRecord>` 並為每個 item 建 `ItemRecord`。背包移動、穿脫、撿物、商店、ranged projectile consumption 都會觸發 flush。ranged 消耗彈藥還會 `Where(...).OrderBy(...).ToList()`，handler 取得 active buff 也會 `ActiveBuffs.Select(...).ToHashSet()`。遠攻/魔攻頻率高時，這些配置會集中在戰鬥熱路徑。

具體修法：
用 inventory dirty delta 取代每次 full flush。彈藥消耗改用 slot index 或手寫 loop 找最小 slot，不要每次排序/ToList。Active buffs 可提供 `HasBuff(MapleBuffStat)` API，在 lock 內查 dictionary，避免 `ActiveBuffs` array + hashset。

### TICK-01【低】目前沒有全地圖/全怪物重 tick，但未來補 AI/respawn 時要避免掃全世界

位置：
- `src/Maple.Application/Maps/InMemoryFieldInstanceRegistry.cs:8`
- `src/Maple.Core/World/Mob.cs:85`
- `src/Maple.Application/Combat/CombatService.cs:45`
- `src/Maple.Application/Combat/CombatService.cs:75`

為何是雷：
目前搜尋未見 `PeriodicTimer`、monster AI、respawn loop 或每 tick 掃所有 field 的 background worker。怪物在玩家進圖建立，攻擊時被扣血/移除。這代表現階段沒有「每 tick 掃全地圖/全怪」的重活；但 registry 目前只有 dictionary，未來若直接加全域 loop 很容易變成世界掃描。

具體修法：
未來 AI/respawn 用 field actor + active field set；只有有玩家的地圖進 scheduler。怪物移動/技能/掉落過期用 timing wheel/min-heap，避免每 tick 全量掃描所有 objects。

## DB+廣播體質總評

DB 體質：中等偏風險。好消息是常見熱動作沒有每次打 DB；壞消息是所有持久化仍是整份根文件替換，而且沒有 dirty tracking/週期 flush。正式化前最該先做的是「dirty write-behind + 欄位級更新」，並把 cash shop、公會、倉庫 close 這些中途保存點改成增量 commit。

廣播體質：目前需要優先修。封包建一次重用的方向是對的，但 `SendAsync` 原地加密讓重用同一 `byte[]` 不安全，會造成 fanout 正確性問題。修正 send buffer ownership 後，再處理 AOI、`GetOthers().ToList()`、PacketWriter/ArrayPool，才能真正降低移動/攻擊廣播的 GC 與 CPU 成本。
