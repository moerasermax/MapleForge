---
編號: 2026-06-18_19
標題: USE_CASH_ITEM Batch D 傳送石現金道具
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 16:19
更新: 2026-06-18 16:24
關聯里程碑: M4-6 / M6+
關聯記憶:
關聯commit: 7b0906b
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植 `USE_CASH_ITEM` Batch D 傳送石：`5042000/5042001` 固定目的地、`5040000/5040001/5041000/2320000` map-mode teleport rocks、`5560000/5561000` any-door map-mode ticket。完成判準：`V113UseCashItemHandler` 回傳 warp intent 而不直接換圖，channel connection handler 接 intent 後走既有 `WarpAsync`；player-name mode 安全 `EnableActions` 不消耗；新增至少 5-6 個 focused tests；指定 `Maple.Adapters.V113.Tests` 命令通過；`Maple.Core` 不引入 `Maple.Adapters.V113`。

## 📋 背景與假設

舊 Java 神諭為 `../TestMapleStoryV113_Server/src/handling/channel/handler/InventoryHandler.java` 的 `UseCashItem` 與 `UseTeleRock`。MapleForge 已有 `FieldLimitType.VipRock` domain enum，但目前 map field-limit 資料尚未載入，因此本任務只留下 TODO，不做 field-limit 判斷。MapleForge 換圖由 channel handler 的 `WarpAsync` 統一處理，cash-item handler 只能回傳 warp intent。

## 🪜 計畫步驟

- [x] 1. 讀現有 `V113UseCashItemHandler` / result / channel dispatch / warp 測試形狀與 Java source map。
- [x] 2. 擴充 `V113UseCashItemResult`，新增 optional `WarpToMapId`。
- [x] 3. 在 `USE_CASH_ITEM` Batch D items 解析 payload、驗證 cash item slot、成功消耗後回 warp intent；player-name mode 回 `EnableActions`。
- [x] 4. 在 `V113ChannelConnectionHandler` 對 cash-item result 執行 `WarpAsync`。
- [x] 5. 新增 focused tests 並跑指定測試命令與 Core/Application adapter using 檢查。

## 📜 執行歷程（邊做邊追加，附時間）

- **16:19** 建立任務歷程，確認本任務不提交、不新增一般文件，只做必要任務帳。
- **16:21** 對照 Java：固定目的地無額外 payload；cash teleport rock payload 為 mode byte 後接 mapId/name，與既有 `USE_TELE_ROCK(0x4E)` 的 leading rockType 不同。
- **16:22** 完成 handler/result/connection handler 修改：success path 消耗 Cash 道具並回 `WarpToMapId`，channel dispatch 送封包與保存後呼叫 `WarpAsync`。
- **16:23** 新增 focused tests：固定目的地 2 筆、teleport rock map mode 4 item ids、teleport rock player mode fallback、any-door 2 item ids、any-door non-map fallback。
- **16:24** 驗證通過：`dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` = 377 passed / 1 skipped；`^using Maple.Adapters.V113` import check 對 Core/Application 無命中。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> Batch D 已完成且指定 adapter test 綠。下一步若接續：補真 v113 client smoke，或在 map field-limit 資料載入後把 TODO 的 MapleLand / saved-rock / continent / `FieldLimitType.VipRock` / event-instance checks 收斂。

## ✅ 結果與結論

達標。`USE_CASH_ITEM` 現在支援 `5042000→701000200`、`5042001→741000000`、`5040000/5040001/5041000/2320000` map-mode warp、`5560000/5561000` map-mode any-door ticket。成功路徑消耗一個 Cash 道具並回 warp intent；player-name mode 與 non-map any-door mode 不消耗並送 `EnableActions`。FieldLimit 與完整傳送石資格檢查保留 TODO，等待 map field-limit / rock-map 資料基礎。

## 🔗 產出

程式：`V113UseCashItemHandler.cs`、`V113ChannelConnectionHandler.cs`。測試：`ChannelUseCashItemTests.cs` 新增 Batch D 覆蓋。文件：本任務歷程、`進度日誌.md`、`v113-protocol-spec.md`。Commit：依使用者要求不提交。
