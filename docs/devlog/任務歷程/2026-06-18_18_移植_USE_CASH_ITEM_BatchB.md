---
編號: 2026-06-18_18
標題: USE_CASH_ITEM Batch B 現金道具邏輯
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 16:00
更新: 2026-06-18 16:12
關聯里程碑: M4-6 / M6-1
關聯記憶:
關聯commit: 840168e
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植 `USE_CASH_ITEM` Batch B：在 `Maple.Adapters.V113` 加入 Item Tag、Sealing Lock、Karma、SP Reset、基礎四屬 AP Reset、Vicious Hammer 的可驗證邏輯；Vega 與其餘缺基礎設施的現金道具只做安全 stub 並送 `EnableActions`。完成判準：`tests/Maple.Adapters.V113.Tests/ChannelUseCashItemTests.cs` 新增 10-15 個針對性測試，指定 adapter 測試命令通過，且 `Maple.Core`/`Maple.Application` 沒有 v113 adapter 依賴。

## 📋 背景與假設

舊 Java `InventoryHandler.UseCashItem` 是行為神諭；Batch A 已接 Owl、黑板、寵物、留言等路由。本輪需要新增少量 Core inventory/player skill/stat 欄位或方法，但 opcode、packet layout、read order 與 enable-actions stub 必須留在 `Maple.Adapters.V113`。AP Reset 的 HP/MP job-specific 計算暫不做，只處理 STR/DEX/INT/LUK 互轉。

## 🪜 計畫步驟

- [x] 1. 對照 Java `UseCashItem` 與 `ItemFlag`，確認 item flag 數值、packet 讀取順序與既有 MapleForge API。
- [x] 2. 補齊 Core inventory/player 所需最小欄位與行為，保持無 v113 依賴。
- [x] 3. 在 `V113UseCashItemHandler` 新增 Batch B switch cases 與私有 handler，完整項目消耗道具並回 inventory mutation；未完成項目 log + `EnableActions`。
- [x] 4. 新增/擴充 `ChannelUseCashItemTests` 覆蓋 Item Tag、Lock、Karma、SP Reset、AP Reset、Hammer 與 stub 行為。
- [x] 5. 跑 targeted tests、檢查 Core/Application adapter import，收尾更新任務歷程與進度日誌。

## 📜 執行歷程（邊做邊追加，附時間）

- **16:00** 建立任務歷程；已完成 AGENTS 指定 first reads，下一步開始程式與 Java source map 探查。
- **16:12** 完成 Batch B handler 與測試：Item Tag、Lock、Karma、SP Reset、基礎 AP Reset、Vicious Hammer 已實作；Vega/Peanut/Gas/Predict/Merchant/Contact/Beans/NPC/Incubator 依範圍做 log + `EnableActions` stub。指定 adapter 測試 367 passed + 1 skipped；Core tests 102 passed；Core/Application adapter `using` 檢查無命中。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

已完成。後續若接手，下一步是針對 stubbed Vega/Predict/Merchant/Contact/Beans/Gas/NPC cash items 補完整子系統，或做真 v113 client USE_CASH_ITEM smoke；目前 Batch B 單元證據已綠。

## ✅ 結果與結論

達標。Java source map 對應 `InventoryHandler.UseCashItem` lines 1040-1519、1845-1895、2059-2225 與 `ItemFlag.java`；`LOCK=0x01`、`KARMA_EQ=0x10`、`KARMA_USE=0x02`。新增 Core 的 AP/SP 轉移與 `ViciousHammer` 欄位不含 v113 依賴；所有 c2s read order 與 deferred stub 邏輯留在 `Maple.Adapters.V113`。

## 🔗 產出

程式：`V113UseCashItemHandler.cs`、Core `Item`/`ItemRecord`/`EquipEntry`/`Player.Stats`/`Player.Equip`、`ItemFlags`。測試：`ChannelUseCashItemTests.cs` 新增 Batch B 覆蓋。文件：本任務歷程、`進度日誌.md`、`v113-protocol-spec.md`。Checkpoint commit：`840168e`。
