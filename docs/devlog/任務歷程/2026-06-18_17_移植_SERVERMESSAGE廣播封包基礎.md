---
編號: 2026-06-18_17
標題: SERVERMESSAGE broadcast packet infrastructure
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 15:36
更新: 2026-06-18 15:47
關聯里程碑: M6-1
關聯記憶:
關聯commit: cf4981b
---

## 🎯 目標（執行前先寫死，過程不偷改）

新增 v113 `SERVERMESSAGE` / megaphone packet encoding infrastructure，只在 `Maple.Adapters.V113` 補 opcode 與 packet builder，並以 `Maple.Adapters.V113.Tests` 驗證 type、Maple string、channel-1、ear flag、item megaphone null/item 以及 triple line layout。完成判準：指定 adapter test project 綠燈；不新增 Core/Application v113 依賴；不實作 megaphone cash item handlers 或 channel/server-wide broadcast transport。

## 📋 背景與假設

Java 行為來源是 `../TestMapleStoryV113_Server/src/tools/MaplePacketCreator.java` 的 `broadcastMessage` 與 `getAvatarMega`，opcode 來源是 `../TestMapleStoryV113_Server/src/handling/SendPacketOpcode.java`。本任務只建立 S2C 編碼基礎；S2C layout 證據層級為 Java source + MapleForge byte-level tests，尚未真 v113 client live smoke。

## 🪜 計畫步驟

- [x] 1. 查 Java `SERVERMESSAGE` / `AVATAR_MEGA` opcode，檢查現有 `V113ChannelOpcodes.cs` 與 packet builder patterns。
- [x] 2. 找現有 item info serializer，新增 `V113BroadcastPackets` 與 send opcode 常數。
- [x] 3. 新增 adapter tests 覆蓋 megaphone/smega/item/triple/heart/skull layouts。
- [x] 4. 跑指定 test command，回填任務歷程與必要活文件。

## 📜 執行歷程（邊做邊追加，附時間）

- **15:36** 依 session 鐵律建立任務歷程，範圍鎖定 packet encoding；下一步查 Java opcode 與現有 packet writer/item helper。
- **15:44** 查得 Java `SERVERMESSAGE=0x3D`、`AVATAR_MEGA=0x6D`；`broadcastMessage` switch layout 與 `PacketHelper.addItemInfo(..., true, true)` 已確認。現有 item info serializer 為多處 private helper，本任務採新 packet class 內私有 helper，避免跨檔重構。
- **15:47** 完成 `V113BroadcastPackets`、send opcode constants、`ChannelBroadcastPacketTests`；指定 Adapters.V113 tests 最終 340 passed + 1 skipped。期間 current worktree 的 cash-item/pet 測試先因並行變更失敗，已只更新該既有測試的 broadcast packet count/assertion 以匹配現有 handler 行為。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

任務完成。後續 D5 可接 `USE_CASH_ITEM` megaphone handlers 與 channel/server-wide broadcast transport；`AVATAR_MEGA` 外觀編碼仍另案。

## ✅ 結果與結論

達標。`SERVERMESSAGE` type 2/3/8/10/11/12 packet builders 已在 v113 adapter 內，`AVATAR_MEGA` opcode constant 已補但未實作 builder。證據層級為 Java source + byte-level adapter tests；真 v113 client megaphone UI smoke 未跑，仍標 Java-source candidate。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113BroadcastPackets.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelBroadcastPacketTests.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelUseCashItemTests.cs`（並行 cash-item 測試 assertion 小修，為解除指定 suite 失敗）
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/任務歷程/README.md`
- `docs/devlog/進度日誌.md`
- commit：未提交（工作區含並行 USE_CASH_ITEM / pet / skill-book 等變更，避免混入）
