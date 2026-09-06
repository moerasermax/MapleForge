---
編號: 2026-09-06_32
標題: P031 — 一般消耗道具（USE_ITEM）接上 FieldLimitType.PotionUse 場地限制
類型: 修補
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P031
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

延續 P029/P030「基礎設施已就緒但沒被使用」排查方向，找 Java `FieldLimitType.PotionUse` 剩下
最主要的呼叫點：`InventoryHandler.UseItem`（一般消耗道具，含 HP/MP 補藥，`USE_ITEM(0x42)`）。
與 P030 的召喚袋/回城卷軸不同，這裡 MapleForge 端**完全沒有**任何場地限制的判斷結構（連
context 物件都沒有），要新增。完成判準：`USE_ITEM` 對照 Java 補上場地限制檢查，含 Java 自己的
兩個硬編例外地圖。

## 📋 背景與假設

- Java `InventoryHandler.java:270-292`（`UseItem`）：解析 slot/itemId、驗證背包道具存在後，
  `if (!FieldLimitType.PotionUse.check(chr.getMap().getFieldLimit()) || chr.getMapId()==610030600
  || chr.getMapId()==105100300) { 套用效果+消耗道具 } else { enableActions }`——**整個「套用+
  消耗」都包在檢查裡**，被擋時道具完全不消耗（跟 P030 的回城卷軸同一種行為，跟召喚袋的「先扣後
  查」不同）。`610030600`/`105100300` 是原始碼註解標註的「cwk quick hack」硬編例外地圖，即使
  場地限制生效仍允許使用。
- MapleForge `V113UseConsumableHandler.Handle(reader, player)` 呼叫 `UseItemService.Use`（純
  Application 層服務，無場地限制概念）直接處理，完全沒有場地限制的判斷點——這裡不是「已經有
  context 物件但沒餵值」（P030 那種），是「連判斷結構都要新增」，但範圍依然單純：一個 bool 閘門，
  跟 P030 相同模式（Adapter 層算好 bool，傳給 handler 方法）。

## 🔧 實作內容

- **`Maple.Adapters.V113`**：
  - `V113UseConsumableHandler.Handle` 新增可選參數 `bool canUsePotion = true`（預設值維持既有
    呼叫端相容，測試不需要全部改動）：`false` 時直接回 `EnableActionsOnly()`，跳過
    `_service.Use` 呼叫（道具不消耗，效果不套用）。
  - `V113ChannelConnectionHandler.cs` 的 `UseItem` case：算出
    `canUsePotion = !FieldLimitType.PotionUse.Check(map.FieldLimit) || mapId is 610030600 or 105100300`
    傳入。

## 🧪 測試

- `ChannelUseConsumableHandlerTests.cs` 新增 1 組：場地限制擋住時道具不消耗、HP 不變、只回
  `EnableActions`。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 932 passed / 1 skipped（P030 收案基準 931 +1：
  Adapters.V113 +1）；Core/Application 禁區 grep clean。

## ⏯️ 接手點

`PetHandler.java:75`（寵物自動補藥）用的是**同一個** `FieldLimitType.PotionUse` 旗標，但寵物
自動補藥依賴完整的寵物系統（M5 里程碑仍在進行中，任務追蹤「寵物 auto-pot 短分支已於
`2026-06-10_05` 補上資料保存，實際施放待寵物系統」），需要先確認 MapleForge 現有寵物 auto-pot
施放流程的完成度，留給後續 P-phase 個別查證，不在這次擴大範圍。

## ✅ 結果與結論

- 這次跟 P029/P030 不完全一樣：P029/P030 是「判斷結構/資料來源已存在，只缺接線」，這次是
  「判斷結構本身要新增」，但因為 `FieldLimitType`/`MapService.LoadMap` 兩個基礎元件已經在
  P029/P030 就緒，新增這個判斷點的成本依然很低（一個 bool 參數+一行條件式）——證明先把
  基礎設施建好（P029），後續同類缺口（P030/P031）的邊際成本會遞減。
- `PotionUse` 旗標同時管一般補藥（本次）與回城卷軸（P030）兩種不同的 c2s 封包/handler，這種
  「一個 Java 旗標橫跨多個 handler」的情況這次是第二次遇到（`VipRock` 也橫跨 TrockAddMap/
  CashTeleportRock/AnyDoorTicket/InventoryHandler 換道具等多處），往後遇到某個 `FieldLimitType`
  旗標時，應該先全域搜尋這個旗標的所有呼叫點再決定要不要一次處理完，而非看到第一個呼叫點就
  以為範圍到此為止。

## 🔗 產出

- 修改：`src/Maple.Adapters.V113/Channel/V113UseConsumableHandler.cs`、
  `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- 修改（測試）：`tests/Maple.Adapters.V113.Tests/ChannelUseConsumableHandlerTests.cs`
- commit：待填
