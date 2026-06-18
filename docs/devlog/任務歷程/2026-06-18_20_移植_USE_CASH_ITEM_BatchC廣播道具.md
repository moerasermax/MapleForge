---
編號: 2026-06-18_20
標題: USE_CASH_ITEM Batch C 廣播現金道具
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 16:19
更新: 2026-06-18 16:30
關聯里程碑: M6-1
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

把 `USE_CASH_ITEM` 的 megaphone/broadcast 現金道具 Batch C 接進 `Maple.Adapters.V113`：支援 map/super/high-performance/heart/skull/item/triple/avatar megaphone 與 MapleTV stub，新增 result broadcast packets 與 channel dispatch 廣播接線，補 8-10 個以上 targeted tests；完成判準為指定 adapter test command 通過，且 `Maple.Core` / `Maple.Application` 不新增 `using Maple.Adapters.V113`。

## 📋 背景與假設

- Java 行為來源：`../TestMapleStoryV113_Server/src/handling/channel/handler/InventoryHandler.java` 的 `UseCashItem` 分支。
- S2C packet builder 已於 D4 新增 `V113BroadcastPackets`，layout 目前是 Java-source candidate，真 v113 client UI smoke 待後續。
- 本批依任務邊界先使用 map-level broadcast infrastructure；不實作真正 world/channel broadcast transport。
- Avatar megaphone MVP 先使用 SuperMegaphone fallback，因完整 avatar mega 需要 character look encoding。

## 🪜 計畫步驟

- [x] 1. 讀現有 `V113UseCashItemHandler`、result type、channel dispatch 與 D4 broadcast tests。
- [x] 2. 新增 broadcast result 欄位與 dispatch 廣播接線。
- [x] 3. 實作 5070000/5071000/5072000/5073000/5074000/5076000/5077000/539xxxx/507500x 分支。
- [x] 4. 補 megaphone cash-item targeted tests。
- [x] 5. 跑指定 adapter tests 與 Core/Application adapter using 檢查。

## 📜 執行歷程（邊做邊追加，附時間）

- **16:19** 建立任務歷程，先鎖定只做 USE_CASH_ITEM Batch C 與必要 broadcast dispatch，不做 channel/server-wide transport。
- **16:24** 確認 `V113UseCashItemResult` 已有 `BroadcastPackets` / `MapPackets`，channel dispatch 已能送 map packets；本批沿用 `MapPackets` 做 map-level MVP。
- **16:27** 實作 megaphone/item-mega/triple/avatar fallback/MapleTV stub，並讓 channel handler 傳入 `_options.ChannelIndex + 1` 供 SERVERMESSAGE channel byte。
- **16:29** 補 `ChannelUseCashItemTests` Batch C cases：map/super/heart/skull/item/triple/avatar/MapleTV/level gate/message length。
- **16:30** 驗證通過：focused `ChannelUseCashItemTests` 56 passed；指定 adapter suite 391 passed + 1 skipped；Core/Application adapter using 檢查無命中。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

已完成。下一步若續做：用真 v113 client 或 capture 驗證 SERVERMESSAGE UI 顯示，並在有 channel/server broadcast infrastructure 時把目前 map-level MVP 升級為 channel/world broadcast transport。

## ✅ 結果與結論

達標。Batch C 現金廣播道具已接入 `USE_CASH_ITEM`，成功路徑會消耗 Cash 背包道具、回 inventory mutation + `EnableActions`，並透過既有 `MapPackets` 廣播 SERVERMESSAGE；MapleTV 5075000-5075002 保守 `EnableActions` 且不消耗。Avatar megaphone 5390000-5390006/5390029 依任務 MVP 使用 SuperMegaphone fallback。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113UseCashItemHandler.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelUseCashItemTests.cs`
- `docs/devlog/任務歷程/2026-06-18_20_移植_USE_CASH_ITEM_BatchC廣播道具.md`
- 驗證：`dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` → 391 passed + 1 skipped。
