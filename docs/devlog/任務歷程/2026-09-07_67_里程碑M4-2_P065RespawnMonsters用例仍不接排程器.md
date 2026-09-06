---
編號: 2026-09-07_67
標題: P065 — M4-2 世界 tick 里程碑：CombatService.RespawnMonsters 用例（仍不接排程器）
類型: 里程碑（M4-2 世界 tick 排程器）第二個切片第二步
狀態: ✅ 完成（不改變現有行為）
建立: 2026-09-07
更新: 2026-09-07
關聯里程碑: M4-2 / P065
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

延續 P064 訂下的怪物重生第二步：在 `CombatService` 新增 `RespawnMonsters(field, now)`
用例——巡覽 field 的重生點，找出該生的怪、生出來、直到達到地圖級生怪上限。**仍然刻意不接
任何排程器**，也刻意不處理「怪物死亡時通知對應重生點」這個掛勾（留給下一個 P-phase），
比照 P062 `ExpireDrops` 的角色分工。

## 📋 背景與查證

- 對照 Java `MapleMap.respawn(force: false)`：`numShouldSpawn = monsterSpawn.size()*3 -
  spawnedMonstersOnMap.get()`，`Collections.shuffle` 打亂重生點候選順序後逐一檢查
  `shouldSpawn()`，生到 `numShouldSpawn` 就停。
- **刻意簡化**：Java 用 `Collections.shuffle` 增加候選點的公平性（避免地圖上限提前打滿時
  永遠是清單前面的點吃虧）；這裡維持固定順序（`field.SpawnPoints` 既有順序）。這只影響「上限
  提前打滿時哪些點被跳過」的隨機性，不影響核心規則本身（該不該生、生幾隻），已在程式碼
  跟此文件明確記下。
- 地圖級生怪計數（Java `spawnedMonstersOnMap`）選擇不另開一個持久計數器欄位，直接用
  `field.Objects.OfType<Mob>().Count()` 即時統計——避免雙重真相（計數器可能因為某個怪物死亡
  路徑忘記遞減而跟實際場上數量脫鉤），跟 P062 `ExpireDrops` 直接掃 `field.Objects` 而非額外
  計數器同一個設計取捨。

## 🔧 實作內容

- **`Maple.Core`**（`FieldInstance.cs`）：新增 `List<MobSpawnPoint> SpawnPoints`——跟場上物件
  一樣，變更要由呼叫端 `lock(field)` 序列化（沿用既有慣例，這裡只是加一個新的可變欄位，沒有
  改變既有並行模型）。
- **`Maple.Application`**（`CombatService.cs`）：
  - 建構式新增 `TimeProvider? timeProvider = null`（沿用 P061/P063 同款 DI-safe 可選參數）。
  - `SpawnMapMonsters`：每建立一隻初始怪物，同時建立對應的 `MobSpawnPoint`（用剛生出這隻怪
    佔用的額度呼叫 `OnSpawned()`，讓重生點的內部計數器從一開始就跟場上真實怪物數量同步——
    這是實作過程中發現並修正的一個真實 bug：一開始漏掉這行，會讓重生點誤以為自己還沒生過
    任何怪，`RespawnMonsters` 可能在還沒殺掉初始怪物前就允許無限重生同一個點，已加進測試
    `SpawnMapMonsters_AlsoRegistersMatchingSpawnPoint`／`RespawnMonsters_MobileZeroMobTimePoint_
    AllowsSecondConcurrentSpawn` 鎖住這個行為）。
  - 新增 `RespawnMonsters(field, now)`：地圖上限算好之後，逐一檢查每個重生點的
    `ShouldSpawn(now)`，通過就用 `_maps.LoadMobStats` 重新查一次模板數值（怪物模板資料不可變、
    共用，跟 `SpawnMapMonsters` 同一份查詢邏輯）建立新 `Mob`，`field.Add` + `point.OnSpawned()`。
  - 新增私有 `AllocateNextMobObjectId`：沿用 `DropService.AllocateDropObjectId` 同款「掃場上
    現有 objectId 取最大值 +1」動態配發手法，避免額外持久計數器。

## 🧪 測試

- `tests/Maple.Application.Tests/Combat/CombatServiceTests.cs`：新增 4 組——
  `SpawnMapMonsters_AlsoRegistersMatchingSpawnPoint`（初始生怪同時建立重生點）、
  `RespawnMonsters_MobileZeroMobTimePoint_AllowsSecondConcurrentSpawn`（`mobTime=0` 且會走動
  的既有測試 fixture，初始生怪後應該還能再生 1 隻達到單點上限 2 隻，第三次呼叫不該再生）、
  `RespawnMonsters_MapAtCapacity_SpawnsNothing`（場上怪物數已達地圖上限，即使重生點理論上
  還能生也不該生）、`RespawnMonsters_NoSpawnPoints_ReturnsEmpty`（field 沒有任何重生點時直接
  回空，不炸例外）。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 1048 passed / 1 skipped（P064 收案基準
  1044 +4：Application 276→280）；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- 跟 P062 一樣，這步刻意不改變任何現有伺服器行為——`RespawnMonsters` 存在且測試通過，但
  沒有任何排程器或 handler 呼叫它，玩家端完全感受不到差異（怪物被殺掉後依然不會重生）。
- 過程中抓到並當場修正一個真實邏輯 bug（重生點計數器沒有跟初始生怪同步），驗證了「先寫
  純函式/用例，再寫測試鎖住行為，最後才接真正的觸發點」這個分階段劇本的價值——如果直接
  一次把排程器也接上，這個計數器不同步的 bug 可能要等到實際跑起來、觀察某個地圖怪物異常
  暴增才會發現，現在則是在寫測試的當下就抓到。
- 下一步（P066）：處理「怪物死亡時通知對應重生點」的掛勾——需要決定怎麼把死掉的 `Mob`
  跟它出生的 `MobSpawnPoint`對應起來（傾向用 `Mob.Definition` 跟 `MobSpawnPoint.Definition`
  的參照相等比對，因為兩者本來就共用同一個 `MapMonster` 物件實例，不需要額外幫 `Mob` 加欄位），
  以及要接在 `CombatService.ApplyAttack`/`KillMobWithoutRewards` 的哪個死亡分支。
- 之後（P067+）：接上排程器（重用 P063 `DropExpiryHostedService` 骨架或屆時評估要不要重構
  成通用 world tick host）。

## 🔗 產出

- 修改：`src/Maple.Core/World/FieldInstance.cs`、`src/Maple.Application/Combat/CombatService.cs`
- 修改（測試）：`tests/Maple.Application.Tests/Combat/CombatServiceTests.cs`
- commit：待填
