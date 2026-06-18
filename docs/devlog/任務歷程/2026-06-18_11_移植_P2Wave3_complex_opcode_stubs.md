---
編號: 2026-06-18_11
標題: P2 Migration Wave 3 complex opcode MVP stubs
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 13:41
更新: 2026-06-18 13:44
關聯里程碑: M6-4 / M6-1 / M4-6
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

將 6 個複雜 v113 recv opcode 接入 `Maple.Adapters.V113` 的 channel dispatch MVP stub：新增 `ITEM_MAKER(0x6B)`、`REWARD_ITEM(0x6A)`、`USE_TREASUER_CHEST(0x6C)`、`CP_UserAntiMacroItemUseRequest(0x61)`、`CP_UserAntiMacroSkillUseRequest(0x62)`、`MONSTER_CARNIVAL(0xD5)` 常數與 dispatch case；每個 case 只讀任務指定最小欄位後送 `EnableActions`，不新增 Core/Application 模型、service 或測試檔，不修改既有 handler 邏輯。完成判準：Host.Shared build 0 errors，Adapters.V113 tests no regression。

## 📋 背景與假設

本輪是 P2 migration wave 3，目標是先讓真客戶端送出的複雜功能 opcode 不落入未知 dispatch 或卡 action lock。Java 行為來源位於舊伺服器 `TestMapleStoryV113_Server/src`：`ItemMakerHandler.java`、`InventoryHandler.java`、`PlayersHandler.java`、`MonsterCarnivalHandler.java`。完整 crafting/reward/anti-macro/monster carnival subsystem 延後；本輪只在 v113 adapter 內保存 opcode byte layout 知識。

## 🪜 計畫步驟

- [x] 1. 檢查現有 `V113ChannelRecvOp` 與 `V113ChannelConnectionHandler` 的 P2 stub pattern。
- [x] 2. 新增 6 個 recv opcode 常數與 6 個 dispatch case，僅讀最小欄位並 `EnableActions`。
- [x] 3. 同步 `v113-protocol-spec.md`、進度日誌與任務歷程接手點。
- [x] 4. 執行指定 build/test，回填結果。

## 📜 執行歷程（邊做邊追加，附時間）

- **13:41** 建立任務歷程，目標與 DoD 已固定，狀態切為執行中。
- **13:43** 已檢查現有 P2 stub pattern 與 Java source map；新增 6 個 `V113ChannelRecvOp` 常數與 6 個 dispatch case。`PlayersHandler.AntiMacro` 在此 Java tree 的 full handler 與任務指定 MVP 欄位不同，本輪依任務範圍只做指定讀欄位 + `EnableActions`。
- **13:44** 同步 protocol spec、進度日誌與任務追蹤；指定 build/test 通過：Host.Shared 0 warning/0 error，Adapters.V113 299 passed + 1 skipped。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 本任務已完成。後續若接續完整移植，優先釐清 anti-macro 真 client layout，並另開 crafting/reward/chest/Monster Carnival subsystem 任務。

## ✅ 結果與結論

6 個 complex opcode 已接入 `Maple.Adapters.V113` channel dispatch MVP stub，所有 v113 byte layout 知識維持在 adapter/spec 文件。未新增 Core/Application 模型、service 或測試檔；完整子系統狀態機仍保留為後續任務。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/進度日誌.md`
- `docs/devlog/任務追蹤.md`
- `docs/devlog/任務歷程/README.md`
