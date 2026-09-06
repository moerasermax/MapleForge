---
編號: 2026-09-06_30
標題: P029 — 傳送石新增受 FieldLimitType.VipRock 地圖限制（TROCK_ADD_MAP）
類型: 移植
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P029
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

任務追蹤.md M3 區塊「傳送石新增刪除 TROCK_ADD_MAP」條目留的殘留 TODO：「`FieldLimitType.VipRock`
尚未移植，特殊 field limit 待後續地圖限制系統」。查證後發現「地圖限制系統」的核心基礎設施
（`FieldLimitType` 列舉 + `Check` 擴充方法）其實**早就存在**於 Core，只是沒有任何地方把地圖的
`fieldLimit` 從 WZ 讀出來、也沒有任何 handler 真的呼叫 `Check`。完成判準：`MapData` 補上
`FieldLimit`（從 WZ `info/fieldLimit` 讀出）、`TROCK_ADD_MAP` 的新增分支對照 Java 補上
`FieldLimitType.VipRock` 檢查。

## 📋 背景與假設

- Java `PlayerHandler.TrockAddMap`（`handling/channel/handler/PlayerHandler.java:225-251`）：
  VIP 與一般傳送石「新增目前地圖」兩個分支都先包在
  `if (!FieldLimitType.VipRock.check(chr.getMap().getFieldLimit()))` 裡——地圖設了這個旗標，
  新增動作**整段跳過**（連錯誤訊息 `dropMessage` 都不送，靜默略過），但**刪除**分支完全不受
  此限制。
- `chr.getMap().getFieldLimit()` 的資料來源：`MapleMapFactory.java` 讀 WZ `info/fieldLimit`
  （`MapleDataTool.getInt(..., 0)`，預設 0＝無限制）——單一 int 屬性，跟地圖的 `returnMap`/
  `town` 屬性同一層級、同樣的讀取方式。
- MapleForge 現況：`FieldLimitType`（`src/Maple.Core/Maps/FieldLimitType.cs`）與 `Check` 擴充
  方法**已經存在**且欄位值與 Java 逐位元組對照（`VipRock = 0x40` 等），但 `MapData` 完全沒有
  `FieldLimit` 屬性，`MapService.LoadMap` 也沒有讀這個 WZ 欄位——地圖限制系統的「檢查邏輯」早就
  有，缺的是「資料來源」與「接線」，不是整套系統都要重新設計。
- `V113ChannelConnectionHandler.HandleTrockAddMapAsync`（既有 M3 移植成果）已經有 VIP/一般兩個
  新增分支的地圖 id 範圍檢查（`mapId != 180000000`／`mapId <= 197010000`），唯獨缺
  `FieldLimitType.VipRock` 這一道。

## 🔧 實作內容（依分層）

- **`Maple.Core`**（`src/Maple.Core/Maps/MapData.cs`）：新增 `long FieldLimit` 屬性（預設 0）。
- **`Maple.Application`**（`src/Maple.Application/Maps/MapService.cs`）：`LoadMap` 用既有的
  `GetLong(info, key, default)` helper（原本就用於 `maxHP` 等欄位）讀 `fieldLimit`，寫入
  `MapData.FieldLimit`。
- **`Maple.Adapters.V113`**（`V113ChannelConnectionHandler.HandleTrockAddMapAsync`）：新增
  `canAddRock = !FieldLimitType.VipRock.Check(_mapService.LoadMap(mapId).FieldLimit)`，VIP 與
  一般新增分支都加上這個條件（刪除分支不受影響，對照 Java）。

## 🧪 測試

- 新增 `tests/Maple.Application.Tests/Maps/MapServiceFieldLimitTests.cs`（合成 `IDataProvider`，
  不吃真 WZ，跟既有吃真 Henesys WZ 資料的 `MapServiceTests.cs` 分開）：`LoadMap` 正確讀出
  `fieldLimit`、缺該欄位時預設 0、`FieldLimitType.VipRock.Check` 位元運算正確（含與其他旗標並存
  情境）。
- `HandleTrockAddMapAsync` 本身因為是 `V113ChannelConnectionHandler`（巨大 singleton handler，
  目前全專案無任何直接單元測試覆蓋這個類別，P021/P023 已有相同先例）的 private 方法，沒有獨立
  整合測試——這次改動是把兩個已測試過的建構元件（`MapService.LoadMap`+`FieldLimitType.Check`）
  接成一個條件判斷式，風險低，比照既有先例只靠底層測試+人工審閱。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 928 passed / 1 skipped（P026 收案基準 925 +3：
  Application +3）；Core/Application 禁區 grep clean。

## ⏯️ 接手點

`V113UseCashItemHandler.cs` 裡另外兩個提到 `FieldLimitType.VipRock` 的 TODO（`HandleCashTeleportRock`/
`HandleAnyDoorTicket`，現金道具傳送石/任意門票的「使用」流程，不是這次處理的「新增儲存地圖」
流程）維持不動——那兩處的 TODO 註解列了一整串驗證需求（MapleLand/saved-rock/continent/
event-instance/VipRock），範圍比這次的單一 boolean 檢查大得多，且需要先確認 Java 對應原始碼的
完整驗證邏輯，留給後續獨立 P-phase 查證。

## ✅ 結果與結論

- 這是本輪第五次「拆解看似需要前置設計的 TODO，發現核心基礎設施早就存在，只缺資料來源/接線」
  的案例——地圖限制系統的「檢查」半套（`FieldLimitType`）跟「資料」半套（WZ `fieldLimit` 讀取）
  分別由不同批次移植完成，沒人把兩者接起來，這類「基礎設施已就緒但沒被使用」的缺口值得往後
  繼續當作排查方向：先查 Core 有沒有現成的列舉/檢查方法，再查有沒有實際資料來源餵給它。

## 🔗 產出

- 修改：`src/Maple.Core/Maps/MapData.cs`、`src/Maple.Application/Maps/MapService.cs`、
  `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- 新增：`tests/Maple.Application.Tests/Maps/MapServiceFieldLimitTests.cs`
- commit：待填
