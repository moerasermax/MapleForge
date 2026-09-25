---
編號: 2026-09-25_98
標題: P096 — 怪物模板旗標 explosiveReward / publicReward → 掉落類型 3 / 2
類型: 移植
狀態: ✅ 完成
建立: 2026-09-25
更新: 2026-09-25
關聯里程碑: P096（/goal 第 26 件，P070 遺留缺口）
關聯記憶: <空>
關聯commit: 見 git log（P096）
---

## 🎯 目標（執行前先寫死，過程不偷改）

P070 刻意留下的缺口：Java `dropFromMonster` 的 droptype 公式前兩個分支（`isExplosiveReward ? 3 : isFfaLoot ? 2`）需要怪物模板旗標，
MapleForge `MobStats` 沒有。補上 WZ 解析與公式。完成判準：旗標解析、優先序有測試，build 0/0，全測試無退化。

## 📋 背景與查證

- Java `MapleLifeFactory`（122-123 行）：`setExplosiveReward(getIntConvert("explosiveReward", info, 0) > 0)`、`setFfaLoot(getIntConvert("publicReward", info, 0) > 0)`。
- Java `MapleMap.dropFromMonster`（496 行）：`droptype = explosiveReward ? 3 : ffaLoot ? 2 : party != null ? 1 : 0`；dropType 3 掉落間距 40（其他 25），且允許多袋楓幣。
- Java 撿取（`InventoryHandler` 2337-2341 行）：怪物掉落只限制 dropType 0（主人）與 1（隊伍），2/3 任何人可撿；`shouldFFA` 只對 dropType < 2。
- MapleForge 查證：`DropService.GetDropPosition` 已有 dropType 3 → 40 間距、`CanPickUp` 已對 ≥2 放行、`MapDrop.ShouldBecomeFfa` 已限 < 2——下游全部就緒，只缺資料與公式。
- 「dropType 3 允許多袋楓幣」：MapleForge 楓幣掉落本來就固定一袋（另一套簡化），不在本 P。

## 🪜 計畫步驟

- [x] 1. Core `MobStats.ExplosiveReward` / `FfaLoot`
- [x] 2. `MapService.LoadMobStats` 讀 `info/explosiveReward`、`info/publicReward`
- [x] 3. `DropService.SpawnDropsFromMonster` 套 Java 優先序；測試、build、全測試、三本帳、commit

## 📜 執行歷程（邊做邊追加，附時間）

- 依計畫實作；`DropServiceTests.MakeMob` 加可選旗標參數，Theory 覆蓋 5 種組合的優先序。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成並 commit。候選：Messenger 換裝外觀更新；藥水回血的隊伍血條同步。

## ✅ 結果與結論

- 達標：爆炸獎勵怪/公共獎勵怪的掉落物一開始就是任何人可撿，爆炸獎勵怪掉落散得比較開。
- 1180 passed / 1 skipped（P095 基準 1174 +6：Application 338→344）；build 0/0；禁區 grep clean。
- 證據層級：Java source；真 WZ 內哪些怪物帶這兩個旗標未抽樣。

## 🔗 產出

- 修改：`src/Maple.Core/World/Mob.cs`、`src/Maple.Application/Maps/MapService.cs`、`src/Maple.Application/Drops/DropService.cs`
- 測試：`tests/Maple.Application.Tests/Drops/DropServiceTests.cs`、`tests/Maple.Application.Tests/Maps/MapServiceMobStatsTests.cs`
