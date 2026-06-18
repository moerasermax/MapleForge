---
編號: 2026-06-18_08
標題: P2 Batch 2A Event Systems MVP stubs
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 12:25
更新: 2026-06-18 12:30
關聯里程碑: M6-4 / P2 opcode migration
關聯記憶:
關聯commit: 未 commit（工作區有同檔 Batch 2B/2C 未歸屬變更）
---

## 🎯 目標（執行前先寫死，過程不偷改）

完成 P2 Migration Batch 2A 的 3 個 event-system recv opcode MVP 接線：

- `COCONUT(0xCF)`：新增 `V113ChannelRecvOp.Coconut`，dispatch 讀 `short coconutId` 後送 `EnableActions`。
- `RPS_GAME(0x80)`：新增 `V113ChannelRecvOp.RpsGame`，dispatch 讀 `byte mode` 後送 `EnableActions`。
- `CP_BeansGameAction(0xE0)`：新增 `V113ChannelRecvOp.BeansGameAction`，dispatch 讀 `byte subType` 後送 `EnableActions`。

完成判準：

- 只修改 v113 adapter dispatch/opcode 與必要活文件；不新增 Core models/services。
- `dotnet build src/Maple.Host.Shared/Maple.Host.Shared.csproj --nologo -v quiet` 0 error。
- `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` 維持 299+1skip 或更多通過、無退化。

## 📋 背景與假設

Java 行為來源：

- `handling/channel/handler/PlayersHandler.java` `hitCoconut()` 與 `server/events/MapleCoconut.java`
- `handling/channel/handler/NPCHandler.java` `RPSGame()` 與 `client/RockPaperScissors.java`
- `handling/channel/handler/BeanGame.java` `BeansGameAction()`

本批目標是把已知 event opcode 從未處理狀態降級成安全 MVP stub，避免客戶端 action lock；完整 Coconut/RPS/Beans event state machine 後續另案。

## 🪜 計畫步驟

- [x] 1. 檢查現有 channel opcode 與 dispatch switch 命名/排序。
- [x] 2. 新增 3 個 `V113ChannelRecvOp` 常數。
- [x] 3. 新增 3 個 dispatch case，讀最小欄位並送 `V113StatsPackets.EnableActions()`。
- [x] 4. 更新 protocol spec、進度日誌與本任務接手點。
- [x] 5. 跑 Host.Shared build 與 Adapters.V113 tests。

## 📜 執行歷程（邊做邊追加，附時間）

- **12:25** 建立任務歷程；已確認工作區有既有修改 `2026-06-18_05_移植_P2全量移植44opcode.md`，本任務避免碰該檔。
- **12:26** 完成 adapter MVP 接線：3 個 recv opcode 常數，3 個 dispatch case；同步 `v113-protocol-spec.md` Batch 2A 註記。
- **12:28** 驗證通過：Host.Shared build 0 warning/0 error；Adapters.V113.Tests 299 passed + 1 skipped。同步進度日誌與任務追蹤。
- **12:30** 發現同工作區有未歸屬本批的 Batch 2B/2C code/journal 變更；不回退、不混入 commit。重新跑 Host.Shared build 與 Adapters.V113.Tests，仍 0 error / 299+1skip。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

本任務完成。後續若要做完整 event 系統，從 `COCONUT/RPS_GAME/CP_BeansGameAction` 的 Java state machine 另開任務，新增 Core/Application model 前先定完整 DoD 與測試策略。

## ✅ 結果與結論

達標。MapleForge 目前已能識別 3 個 event-system recv opcode，消耗最小欄位後放行 client action；完整 Coconut/RPS/Beans event state machine 未在本批建立，符合 MVP stub 邊界。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/進度日誌.md`
- `docs/devlog/任務追蹤.md`
- `docs/devlog/任務歷程/README.md`
