---
編號: 2026-09-06_04
標題: RandomRewards 金/銀寶箱權重獎勵表移植
類型: 移植
狀態: ✅ 完成
建立: 2026-09-06 03:00
更新: 2026-09-06 03:40
關聯里程碑: P004（P003 收尾殘餘 TODO：REWARD_ITEM/寶箱 RandomRewards catalog）
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

`USE_TREASUER_CHEST(0x6C)`（金/銀寶箱）目前是 deterministic fallback，永遠只給 Java 獎勵表的
「第一筆」道具（`V113RewardItemHandler.cs` 明確寫著 TODO 註解）。對照 Java `server.RandomRewards`
+ `constants.GameConstants.goldrewards/silverrewards` 補上真正的加權隨機獎勵表，並補齊特殊/超級
藥水的固定給予數量（200/100）。完成判準：金/銀寶箱開箱時從完整權重表隨機抽獎（非固定同一筆）、
藥水數量比照 Java、單元測試涵蓋全表索引、既有 byte-level fixture 測試不因隨機化而變脆弱。

## 📋 背景與假設

- P003 收尾筆記誤把這個缺口記成「`Etc.wz`/`RandomRewards` 權重 catalog」——實際追查 Java 來源後
  發現 `RandomRewards`（`server/RandomRewards.java`）**不是** WZ 資料，也不是 SQL 資料，而是
  **純 Java 硬編常數陣列**（`GameConstants.goldrewards`/`silverrewards`，`[itemId, weight, itemId,
  weight, ...]` 交錯陣列）。這與 `REWARD_ITEM(0x6A)` 用的另一套系統（`MapleItemInformationProvider.
  getRewardItem`，來源是 **SQL** 的 `StructRewardItem`/`initItemRewardData`）是兩個完全不同的東西，
  P003 筆記把兩者混為一談。
- 本任務**只**處理 `USE_TREASUER_CHEST`（金/銀寶箱，`RandomRewards.java` 那套），因為它是純常數、
  無資料庫依賴、可直接忠實移植。`REWARD_ITEM(0x6A)` 的 SQL-based 通用獎勵表**不在此次範圍**——
  那需要先決定「SQL 表資料要怎麼進到 MapleForge」（這專案刻意不綁 MySQL），是更大的獨立設計決策，
  維持既有 deterministic fallback 不動。
- Java `InventoryHandler.UseTreasureChest`：金/銀寶箱各查 `RandomRewards.getGoldBoxReward()`/
  `getSilverBoxReward()`；藥水類獎勵（2000004 特殊藥水/2000005 超級藥水）固定給 200/100 個，其餘
  一律 1 個；消耗 1 個寶箱（ETC）+ 1 把鑰匙（CASH，非整疊）；`addbyId_Gachapon` 與 MapleForge 既有
  `Player.GainItem` 對非裝備/裝備的處理無實質差異，沿用既有 `ConsumeContainerAndGrantReward` 管線。
- 稀有度全服喇叭廣播（`GameConstants.gachaponRareItem`+`getGachaponMega`）**不在此次範圍**：
  MapleForge 目前沒有寶箱專用的全服廣播 hook，屬於獨立的小功能缺口，列為後續 TODO，不擋本次收案
  （核心的「加權隨機抽獎表」缺口已補齊，這是 P004 筆記明確點名的項目）。

## 🪜 計畫步驟

- [x] 1. 讀 Java `RandomRewards.java`/`GameConstants.goldrewards`/`silverrewards`/`InventoryHandler.
      UseTreasureChest`，確認資料來源與抽獎/數量/消耗語意
- [x] 2. Application 新增 `RandomRewardsCatalog`（DI singleton，忠實移植兩張表 + 加權抽獎）
- [x] 3. `V113RewardItemHandler.HandleTreasureChest` 接上真實抽獎 + 藥水數量特例
- [x] 4. DI 註冊（Host.Shared）
- [x] 5. 單元測試：Application 全表索引覆蓋 + 邊界；Adapters 既有 fixture 改用可控隨機源 + 新增藥水數量測試
- [x] 6. `dotnet build` + 全專案測試 + Core/Application 禁區 grep
- [x] 7. 文件同步（任務歷程、進度日誌、移植狀態地圖、任務追蹤殘留 TODO 註記）+ commit

## 📜 執行歷程（邊做邊追加，附時間）

- **03:00** 讀 Java 來源，發現 P003 筆記對 RandomRewards 資料來源的描述有誤（以為是 WZ，實際是 Java 硬編常數），且與 REWARD_ITEM 的 SQL-based 系統是兩回事；確認本任務範圍收斂到金/銀寶箱。
- **03:15** 新增 `Maple.Application.Items.RandomRewardsCatalog`：兩張 `(itemId, weight)[]` 常數表（`static readonly` 陣列，MF0001 分析器對 readonly 豁免，逐項保留 Java 原始中文品名註解方便比對）、`Compile()` 攤平成重複清單、`GetGoldBoxReward`/`GetSilverBoxReward` 用 injected `Random`（非 `Random.Shared` 靜態依賴，支援測試注入固定索引）均勻抽取；省略 Java 原本多餘的 `Collections.shuffle`（均勻抽取不受清單順序影響，純簡化非行為變更，已加註解說明）。
- **03:20** 改 `V113RewardItemHandler.HandleTreasureChest` 簽章加 `RandomRewardsCatalog randomRewards` 參數，接上真實抽獎 + 藥水數量 switch；DI 註冊；接線到 `V113ChannelConnectionHandler`。
- **03:30** 手算驗證 gold/silver 攤平表精確長度（127/119）與特定索引對應的 itemId（89→2000005 超級藥水、99→2000004 特殊藥水），寫 `RandomRewardsCatalogTests`（含「每個可能索引都落在宣告表內」全覆蓋測試 + 邊界索引 `IndexOutOfRangeException` 反向驗證長度算對）與更新 `ChannelRewardItemHandlerTests`（既有 byte-fixture 改用 `FixedIndexRandom(0)` 保留原斷言不變、新增超級藥水數量測試）。
- **03:38** `dotnet build` 0/0 → 全 8 個測試專案跑過：828 passed / 1 skipped（Core 126、Application 186、Adapters 431、Persistence 11、Net 2、Content 21、Tools.PacketDecoder 22、Tools.HeadlessClient 29）；Core/Application 禁區 grep 乾淨。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成並可收案。後續延伸：①稀有度全服喇叭廣播（`gachaponRareItem`/`getGachaponMega`）需要先有寶箱專用廣播 hook，列 P004 後續候選 ②`REWARD_ITEM(0x6A)` 的 SQL-based `StructRewardItem` 通用獎勵表維持 deterministic fallback，需要先決定 SQL 資料遷移策略才能動，這是獨立的較大設計決策 ③如果之後要精確重現 Java 的多輪重抽語意（`while(!rewarded)` 迴圈，理論上機率為 0 的道具會被跳過重抽），目前 MapleForge 版本改成「均勻抽取攤平表」已經是等價簡化（weight=0 的道具因為攤平長度為 0 天然不會出現），無需額外處理。

## ✅ 結果與結論

- 補上 P004 明確點名的「RandomRewards catalog」缺口，但過程發現這個缺口比預期簡單很多——因為 P003 筆記把資料來源記錯了（誤植成 WZ，實際是 Java 常數）。**可轉移心法**：backlog 筆記裡對「資料在哪裡」的描述不能盡信，撿起來做之前先回頭核對一次 Java 來源，可能比想像中好做（本例）或做不到（562x 那次）。
- 刻意省略 Java 的 `Collections.shuffle`（純粹對雜湊集合迭代順序洗牌，均勻隨機抽取不受影響）——這是一個安全的簡化範例：省掉不影響輸出分佈的步驟，同時用「全索引覆蓋測試」證明沒有引入偏差或越界。

## 🔗 產出

- 新增：`src/Maple.Application/Items/RandomRewardsCatalog.cs`、`tests/Maple.Application.Tests/Items/RandomRewardsCatalogTests.cs`
- 修改：`src/Maple.Adapters.V113/Channel/V113RewardItemHandler.cs`、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`、`src/Maple.Host.Shared/MapleServerHost.cs`、`tests/Maple.Adapters.V113.Tests/ChannelRewardItemHandlerTests.cs`
- commit：待填
