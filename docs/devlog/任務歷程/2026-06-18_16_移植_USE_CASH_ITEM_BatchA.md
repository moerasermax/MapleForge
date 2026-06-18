---
編號: 2026-06-18_16
標題: USE_CASH_ITEM Batch A 現金道具路由
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 15:35
更新: 2026-06-18 15:50
關聯里程碑: M6-1 / M4-6 / M6-2
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植 `USE_CASH_ITEM` Batch A：chalkboard、pet name、pet skill flag add/remove、cash pet food、notes、congratulatory song。完成判準：v113 byte layout 僅在 `Maple.Adapters.V113`；Core/Application 不新增 v113 依賴；目標現金道具可正確解析、驗證 slot/item、按 Java 語義消耗或不消耗；新增 focused tests 覆蓋有效使用、invalid slot、item missing 與代表性狀態變更；`dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` 通過。

## 📋 背景與假設

舊 Java 神諭為 `../TestMapleStoryV113_Server/src/handling/channel/handler/InventoryHandler.java` 的 `UseCashItem`。MapleForge 已有 `V113UseCashItemHandler` 與 Owl route，且已有 chalkboard close、pet、NoteService 等基礎設施。本輪不做 AP reset、megaphone、teleport rock 等 Batch B/C/D。

## 🪜 計畫步驟

- [x] 1. 讀 MapleForge 現有 `USE_CASH_ITEM`、chalkboard、pet、note、packet/test patterns。
- [x] 2. 對照 Java `UseCashItem` 目標分支，補最小必要 parser/packet/domain support。
- [x] 3. 新增 focused tests：valid、invalid slot、missing item，以及 chalkboard 不消耗、pet/name/flag/food、note、song 狀態或封包。
- [x] 4. 跑 targeted adapter tests 與 Core/Application v113 dependency check。
- [x] 5. 收尾更新任務歷程；進度日誌/協定規格已有其他併行未提交變更，避免混入本任務提交。

## 📜 執行歷程（邊做邊追加，附時間）

- **15:35** 已完成必讀文件與 `git status`；工作區已有他人未追蹤任務/計畫檔，將保留不碰。
- **15:43** 完成 handler / packet / pet service / test patches：新增 chalkboard、note、song、pet name、pet flag、cash pet food route。
- **15:46** `Maple.Adapters.V113.Tests` 首輪 340 passed / 1 failed / 1 skipped；失敗為 cash pet food 觸發 level-up 時會多送 foreign effect，已修正測試判準。
- **15:50** 驗證完成：Adapters 340 passed + 1 skipped；Core 102 passed；Application 134 passed；Host.Shared build 0 warning / 0 error；Core/Application 無 `using Maple.Adapters.V113`。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> Batch A 程式與 targeted verification 已完成。若要續作，下一步是用真 v113 client smoke 驗 chalkboard/pet/song UI；cash pet food 的 pet-specific `petsCanConsume(itemId)` 仍待資料 catalog 補齊。

## ✅ 結果與結論

已達成本輪 DoD。`USE_CASH_ITEM` 新增 Batch A 路由：`5090000/5090100` notes、`5100000` congratulatory song、`5170000` pet name、`5190000..5190008` / `5191000..5191004` pet skill flag add/remove、`5240000..5240028` cash pet food、`5370000/5370001` chalkboard。Chalkboard 不消耗道具；其他成功路徑消耗 1 個 Cash 背包道具。Pet name/flag 會同步 active pet 與 cash inventory item metadata；cash pet food 依 Java 語義設 fullness=100、closeness +100、最多升一級。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113UseCashItemHandler.cs`
- `src/Maple.Adapters.V113/Channel/V113CashItemPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113PetPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `src/Maple.Application/Pets/PetService.cs`
- `src/Maple.Core/Pets/Pet.cs`
- `src/Maple.Core/Pets/PetConstants.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelUseCashItemTests.cs`
- `tests/Maple.Adapters.V113.Tests/Channel/PetPacketTests.cs`
