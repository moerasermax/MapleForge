---
編號: 2026-07-07_01
標題: HiredMerchant Cut3 收尾刀
類型: 移植
狀態: ✅ 完成
建立: 2026-07-07 03:13
更新: 2026-07-07 03:41
關聯里程碑: P003 / M5-5
關聯記憶:
關聯commit: 本提交（最終 hash 見回報）
---

## 🎯 目標（執行前先寫死，過程不偷改）

完成 HiredMerchant Cut3：把 `PLAYER_INTERACTION(0x73)` 的雇用商人子指令接上 D5 `PlayerShopService`，補啟動時未過期商人重載/過期轉 claimable，補 merchant position 持久化與一條 LiteDB 端到端整合測試。完成判準：`dotnet build` 綠；逐專案測試零退化（基線 767 passed / 1 skipped）；Core/Application 禁區 grep clean；commit 並 push `P003-D7: HiredMerchant Cut3 — 0x73子指令+啟動重載+整合測試`。

## 📋 背景與假設

- 基底 HEAD `8c22d6a`，D5 已完成 Core/Application/Persistence，D6 已完成 `0x34/0x38/0x3A` 與 HiredMerchant S2C candidate。
- Java oracle：`handling/channel/handler/PlayerInteractionHandler.java` merchant 分支、`server/shops/HiredMerchant.java`、`server/shops/AbstractPlayerStore.java`、`tools/packet/PlayerShopPacket.java`。
- 本刀仍遵守 byte layout 只放 `Maple.Adapters.V113`；Core/Application 只放領域與 use case 語義；所有新 S2C fixture 若無真 client/capture，標 `unverified`。
- D6 TODO：商人 spawn replay position fallback `(0,0)`，本刀補 position 持久化。

## 🪜 計畫步驟

- [x] 1. 對照 Java oracle 與現有 `V113PlayerInteractionRouter` / `PlayerShopService` / persistence 形狀，定最小落點。
- [x] 2. 補 Core/Application/Persistence position 欄位與 roundtrip 測試。
- [x] 3. 在 `V113PlayerInteractionRouter` 接 merchant CREATE/VISIT/ITEMS/BUY/EXIT/CHAT，呼叫 `PlayerShopService` 並送 unverified S2C。
- [x] 4. 補啟動重載服務：未過期 open merchant 保留可 spawn replay，過期 open merchant 轉 claimable。
- [x] 5. 補 opcode fixture 與 LiteDB E2E 整合測試。
- [x] 6. 更新 protocol/persistence/任務文件，跑 build、逐專案測試、禁區 grep。
- [x] 7. commit + push，回報覆蓋範圍、測試數字、commit hash、殘餘 TODO。

## 📜 執行歷程（邊做邊追加，附時間）

- **03:13** 開工前檢查：`git status --short` 顯示兩個既有 P003 docs 修改；視為前序工作/使用者變更，後續只追加必要內容，不覆蓋。
- **03:34** 完成前置碼：Core `HiredMerchant`/`PlayerShopState` 補 position、Application 下架結果補回傳 item、Persistence document mapper 補 position、D6 handler replay 改用 merchant position、`V113PlayerInteractionRouter` 接 0x73 merchant 主分支、Host 加 startup reload service；`dotnet build --nologo -v quiet` 綠。
- **03:41** 完成驗證：`dotnet build --nologo -v quiet` 0 warning / 0 error；Core 111、Application 151、Persistence 11、Content 21、Net 2、PacketDecoder 22、HeadlessClient 29、Adapters 425 passed / 1 skipped；禁區 grep 無命中。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> D7 已完成並通過驗證；下一步 commit+push，回報 hash 與殘餘 TODO。D7b 需等使用者提供外部審查缺陷清單。

## ✅ 結果與結論

達標。HiredMerchant 0x73 merchant 子指令、啟動重載、position 持久化與 LiteDB E2E 都已落地；S2C layout 仍是 Java-source candidate / unverified，待 W4 review 或真機 capture 校準。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113PlayerInteractionRouter.cs`
- `src/Maple.Host.Shared/HiredMerchantReloadHostedService.cs`
- `src/Maple.Core/PlayerShops/*` position 補強
- `src/Maple.Application/PlayerShops/PlayerShopService.cs`
- `src/Maple.Persistence/PlayerShops/HiredMerchantDocuments.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelPlayerInteractionHiredMerchantTests.cs`
- protocol / persistence / devlog 文件同步
