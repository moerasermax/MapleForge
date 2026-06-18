---
編號: 2026-06-18_09
標題: P2 Batch 2C CashShop + AntiMacro simple opcode stubs
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 12:26
更新: 2026-06-18 12:32
關聯里程碑: M6-1 / P2 opcode migration
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植 P2 Batch 2C 三個 v113 channel recv opcode 的 MVP stub：`COUPON_CODE(0xE7)`、`OldAntiMacroQuestion(0x63)`、`ITEM_UNLOCK(0x95)`。

完成判準：

- `V113ChannelRecvOp` 新增三個 opcode 常數。
- `V113ChannelConnectionHandler` 新增三個 dispatch case，只讀 Java 對應的最小欄位並送 `EnableActions`。
- 不新增 Core/Application v113 byte layout knowledge，不修改舊 Java server、client binaries、WZ 或 sibling projects。
- `dotnet build src/Maple.Host.Shared/Maple.Host.Shared.csproj --nologo -v quiet` 0 errors。
- `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` 無回歸。

## 📋 背景與假設

Java source map：

- `handling/channel/handler/CashShopOperation.java`：`CouponCode` 讀 `skip(2)` + coupon code string，完整 DB/獎勵系統暫不移植。
- `handling/channel/handler/PlayersHandler.java`：`OldAntiMacroQuestion()` 讀 answer string 並驗證 anti-macro state，完整 reward/reduce 暫不移植。
- `handling/channel/handler/PlayersHandler.java`：`UnlockItem()` 是此 Java tree 的 `ITEM_UNLOCK` 入口，full handler 讀三個 short 並移除 lock/untradeable flag；本批依使用者指定 MVP 只讀一個 short。

本輪只把 client action lock 解除與未處理 opcode 降噪；完整 coupon DB、anti-macro state、item lock service 之後另拆。

## 🪜 計畫步驟

- [x] 1. 檢查既有 opcode/dispatch 命名與相鄰 stub pattern。
- [x] 2. 新增三個 opcode 常數與 dispatch case。
- [x] 3. 同步 protocol spec、progress log 與任務歷程。
- [x] 4. 跑指定 Host.Shared build 與 Adapters.V113 tests。

## 📜 執行歷程（邊做邊追加，附時間）

- **12:26** 已完成 session invariants、journal README、conventions、architecture、task tracking、progress log、protocol/test/capture/methodology docs 首讀；工作區既有 `2026-06-18_05_移植_P2全量移植44opcode.md` modified，非本任務檔。
- **12:31** 完成 adapter 接線：`OldAntiMacroQuestion(0x63)`、`ItemUnlock(0x95)`、`CouponCode(0xE7)` 三常數與三個 MVP dispatch case；同步 `v113-protocol-spec.md` Batch 2C 註記。工作區另有既存 P2 Batch 2A/2B 變更，保留不回退。
- **12:32** 驗證通過：Host.Shared build 0 warning/0 error；Adapters.V113.Tests 299 passed + 1 skipped。同步進度日誌、任務追蹤與 README index。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

本任務完成。後續若要做完整系統，分別從 coupon DB/reward、anti-macro state/reward-reduce、item lock flag update + 2051000 consumption 另開任務。

## ✅ 結果與結論

達標。三個 opcode 已接入 v113 adapter：讀取 MVP 欄位後送 `EnableActions`，未新增 Core/Application v113 byte layout 或新服務。完整 CashShop coupon、anti-macro 驗證狀態與 item unlock inventory mutation 仍為後續任務。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/進度日誌.md`
- `docs/devlog/任務追蹤.md`
- `docs/devlog/任務歷程/README.md`
