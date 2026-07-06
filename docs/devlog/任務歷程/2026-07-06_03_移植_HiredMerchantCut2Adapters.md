---
編號: 2026-07-06_03
標題: HiredMerchant Cut2 Adapters
類型: 移植
狀態: ✅ 完成
建立: 2026-07-06 22:34
更新: 2026-07-06 22:54
關聯里程碑: P003 W3
關聯記憶:
關聯commit: 8c22d6a
---

## 🎯 目標（執行前先寫死，過程不偷改）

完成 HiredMerchant Cut2：把 V113 `CP_HiredMerchantRemoteControl(0x34)`、`USE_HIRED_MERCHANT(0x38)`、`MERCH_ITEM_STORE(0x3A)` 從 EnableActions stub 接到 D5 的 `PlayerShopService`，新增必要的未驗證 s2c 封包與 opcode fixture 測試；`dotnet build`、逐專案測試、既有 761+1skip 基線、禁區 grep 全部通過後 commit+push。

## 📋 背景與假設

D5 已在 `f64bce1` 建好 Core/Application/Persistence 的 HiredMerchant 領域、`PlayerShopService`、LiteDB/Mongo repository。D6 只做 Adapters 接線，byte layout 必須留在 `Maple.Adapters.V113`，Core/Application 仍不得引用 V113。行為 oracle 為舊 Java OdinMS server：

- `handling/channel/handler/HiredMerchantHandler.java`
- `handling/channel/handler/PlayerInteractionHandler.java`
- `tools/packet/PlayerShopPacket.java`
- `properties/recv.properties` / `send.properties`

s2c 封包沒有真機 ground truth，一律標記 `unverified`。若商人進店/購買仍在 `PLAYER_INTERACTION(0x73)` 子指令且缺口過大，本刀只列 TODO，不硬塞完整互動。

## 🪜 計畫步驟

- [x] 1. 盤點現有 Adapters stub、DI、測試工具與 Java oracle layout。
- [x] 2. 實作 0x34/0x38/0x3A handler、service 接線與未驗證 s2c 封包。
- [x] 3. 補每個 opcode fixture 測試與必要的 handler 單元測試。
- [x] 4. 跑 build、逐專案測試、全基線、禁區 grep。
- [x] 5. 更新 devlog/spec/計畫文件，commit 並 push。

## 📜 執行歷程（邊做邊追加，附時間）

- **22:34** 建立 D6 任務紀錄；HEAD 為 `f64bce1`，工作樹乾淨。
- **22:49** 完成 `V113HiredMerchantHandler` / `V113HiredMerchantPackets` / DI / channel dispatch；0x34、0x38、0x3A targeted Adapters 測試綠（423 passed / 1 skipped）。
- **22:54** 全部逐專案測試綠：Core 110、Application 150、Persistence 10、Content 21、Net 2、PacketDecoder 22、HeadlessClient 29、Adapters 423+1skip，總計 767+1skip；`dotnet build` 綠；Core/Application 禁區 grep clean。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> D6 已達標並 commit/push `8c22d6a`；後續由 D7 `2026-07-07_01_移植_HiredMerchantCut3.md` 接續 0x73 merchant 子指令與 startup reload。

## ✅ 結果與結論

達標。0x34 遠端操控、0x38 商人 permit/title/open candidate、0x3A Fredrick package list/claim 已接到 D5 `PlayerShopService` / repository；新增 Java-source candidate / unverified 的 hired merchant UI、Fredrick store、spawn/update/destroy/helper 封包。0x73 子指令仍只支援 trade，商人進店/上架/購買/移除列後續。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113HiredMerchantHandler.cs`
- `src/Maple.Adapters.V113/Channel/V113HiredMerchantPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelHiredMerchantHandlerTests.cs`
- `tests/Maple.Persistence.Tests/AssemblyInfo.cs`
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/進度日誌.md`
- `docs/devlog/任務追蹤.md`
- `docs/devlog/執行計畫/P003_移植清尾與stub補完整.md`
- commit `8c22d6a`
