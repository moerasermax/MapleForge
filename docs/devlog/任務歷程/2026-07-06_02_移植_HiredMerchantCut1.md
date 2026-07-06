---
編號: 2026-07-06_02
標題: HiredMerchant Cut1 Core App Persistence
類型: 移植
狀態: ✅ 完成
建立: 2026-07-06 00:00
更新: 2026-07-06 22:28
關聯里程碑: P003-D5
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

完成 P003 D5 HiredMerchant Cut1：只在 Core/Application/Persistence 建立雇用商人與玩家商店的領域模型、repository contract、LiteDB/Mongo 持久化與 PlayerShopService 用例；不接任何 v113 opcode dispatch、不修改 Adapters。完成判準：`dotnet build` 綠；Core/Application/Persistence 測試專案各自綠；既有測試零退化；`rg -n "Maple.Adapters.V113" src/Maple.Core src/Maple.Application` 無命中；文件與本任務歷程更新，commit+push。

## 📋 背景與假設

- Java 行為神諭：`handling/channel/handler/HiredMerchantHandler.java`、`server/shops/HiredMerchant.java`、`server/shops/AbstractPlayerStore.java`、`server/shops/IMaplePlayerShop.java`。
- MapleForge 架構鐵律：Core/Application 不得知道 v113 byte/opcode；不搬 OdinMS static/global store registry；商店狀態用領域模型和 service 編排。
- 本刀只交付 domain/use case/persistence，Cut2 才處理 packet parser/serializer 與 channel dispatch。

## 🪜 計畫步驟

- [x] 1. 探查 MapleForge 既有 Character/Inventory/CashCoupon repository 與 LiteDB/Mongo 模式。
- [x] 2. 讀 Java HiredMerchant/AbstractPlayerStore 行為來源，整理本刀需要的 domain 規則。
- [x] 3. 新增 Core player shop 模型與 repository contract。
- [x] 4. 新增 Application PlayerShopService 上架/購買/下架/關店/過期用例。
- [x] 5. 新增 LiteDB/Mongo hired merchant persistence roundtrip。
- [x] 6. 補 Core/Application/Persistence 測試與文件同步。
- [x] 7. 跑 targeted/full verification、禁區 grep、commit+push。

## 📜 執行歷程（邊做邊追加，附時間）

- **00:00** 已讀必讀規範、P003 計畫與 protocol/test 補充文件；工作區起點有 P003 計畫與主任務歷程兩個既有未提交文件變更，先不覆蓋。
- **00:00** 探查 MapleForge：沿用 `Player` 富背包、`ItemRecord` 持久快照、`ShopInventoryMutation`、LiteDB/Mongo repository 雙 provider 與 `ServiceCollectionExtensions` provider switch 模式。
- **00:00** Java source map：`HiredMerchantHandler` 處理 Fredrick/離線領回；`PlayerInteractionHandler` 處理建立、上架、購買、下架、關店、黑名單；`HiredMerchant.buy` 處理購買後 bundle 減少、累計收益與 `GameConstants.EntrustedStoreTax`；`AbstractPlayerStore.saveItems` 將剩餘 bundles 與 meso 暫存。
- **22:28** 完成 Core `Maple.Core.PlayerShops`：`IPlayerShop`、`PlayerShop`、`HiredMerchant`、listing/visitor/settlement model、`IHiredMerchantRepository` contract；上架限制、購買售罄、黑名單、過期、meso overflow 與每商店 lock 序列化購買。
- **22:28** 完成 Application `PlayerShopService`：建立/開店、上架扣背包、購買扣錢給物、下架、收益領取、關店暫存、Fredrick-style claim、過期轉 claimable。
- **22:28** 完成 Persistence：LiteDB/Mongo `hired_merchants` + `hired_merchant_items` provider、DI provider switch、LiteDB roundtrip tests；新增 `docs/specs/persistence-model.md`。
- **22:28** 驗證：build 0/0；Core 110、Application 150、Persistence 10、Content 21、Net 2、PacketDecoder 22、HeadlessClient 29、Adapters 417+1skip；總計 761 passed / 1 skipped；禁區 grep 無命中。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> D5 Cut1 已完成並驗證。下一步交給 Cut2：在 `Maple.Adapters.V113` 接 `CP_HiredMerchantRemoteControl(0x34)`、`USE_HIRED_MERCHANT(0x38)`、`MERCH_ITEM_STORE(0x3A)` parser/handler/packet fixtures，呼叫 `PlayerShopService`，不得把 byte layout 回流 Core/Application。

## ✅ 結果與結論

達標。HiredMerchant 的語義已落在 Core/Application，持久化已落在 LiteDB/Mongo provider，未接任何 v113 opcode dispatch，也未修改 `src/Maple.Adapters.V113`。Java 行為來源已對照並記錄；server-to-client UI 封包與 dispatch 留給 Cut2。

## 🔗 產出

- Core：`src/Maple.Core/PlayerShops/`、`ItemFlags.Untradeable`
- Application：`src/Maple.Application/PlayerShops/PlayerShopService.cs`
- Persistence：`src/Maple.Persistence/PlayerShops/`、DI 註冊
- Tests：Core +6、Application +5、Persistence +3
- Docs：`docs/specs/persistence-model.md`、`docs/specs/v113-protocol-spec.md`、`docs/devlog/進度日誌.md`、P003 計畫與任務追蹤
- Commit：待填
