---
編號: 2026-06-18_10
標題: P2 Batch 2B misc medium 5 opcode stubs
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 12:25
更新: 2026-06-18 12:33
關聯里程碑: M4/M5/M6 P2 opcode migration
關聯記憶: 
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植 P2 Batch 2B medium-complexity recv opcode 的 MVP dispatch：`MOB_NODE(0xBD)`、`USE_TELE_ROCK(0x4E)`、`QUEST_ITEM(0x10C)`、`USE_SCRIPTED_NPC_ITEM(0x48)`、`ThrowGrenade(0x67)`。完成判準：5 個 opcode 常數加入 `V113ChannelRecvOp`，5 個 dispatch case 加入 `V113ChannelConnectionHandler` 並只做指定欄位讀取/放行，不新增 service、不修改既有 handler 語義；`dotnet build src/Maple.Host.Shared/Maple.Host.Shared.csproj --nologo -v quiet` 0 errors；`dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` 無 regression；同步任務歷程、進度日誌與 protocol spec。

## 📋 背景與假設

本批是 P2 全量移植的中等複雜度降噪接線。Java source 仍是行為神諭，真 v113 client/capture 是最終協定驗證；但本輪使用者明確要求 MVP stub，不做 escort quest、teleport rock field limit/warp、scripted NPC item binding、quest item semantics 或 grenade relay。v113 opcode/byte layout 只落在 `Maple.Adapters.V113`。

## 🪜 計畫步驟

- [x] 1. 檢查現有 opcode 常數與 channel dispatch 風格。
- [x] 2. 加入 5 個 recv opcode 常數與 5 個 MVP dispatch case。
- [x] 3. 更新 protocol spec、進度日誌與本任務接手點。
- [x] 4. 跑指定 Host.Shared build 與 Adapters.V113 tests。
- [x] 5. 收尾本任務歷程，視工作區狀態決定是否可乾淨 commit/push。

## 📜 執行歷程（邊做邊追加，附時間）

- **12:25** 建立任務歷程，狀態設為執行中；下一步檢查 opcode/dispatch 既有格式。
- **12:28** 已加入 5 個 recv opcode 常數與 dispatch case：mob/script/tele-rock/grenade stubs 送 `EnableActions`，`QUEST_ITEM` no-op。
- **12:30** 同步 `v113-protocol-spec.md`、`進度日誌.md` 與任務歷程索引；因既有 `2026-06-18_08` 已屬 Batch 2A，將本任務檔改編號為 `2026-06-18_10`。
- **12:33** 指定驗證完成：Host.Shared build 0 warning/0 error；Adapters.V113 tests 299 passed + 1 skipped。工作區另有既存/併行 Batch 2A/2C 與 P2 全量任務檔變更，未混入本任務結論。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

本任務已完成並通過指定 build/test。若要 commit，需只選取 Batch 2B hunks；工作區同時存在 Batch 2A/2C untracked journal 與其他 P2 文件變更，不可整包提交。

## ✅ 結果與結論

達標。5 個 opcode 常數與 5 個 dispatch case 已加入 `Maple.Adapters.V113`，MVP stub 只做指定欄位讀取/放行，未新增 service 或修改既有 handler 語義。驗證：Host.Shared build 0 warning/0 error；Adapters.V113 tests 299 passed + 1 skipped。

## 🔗 產出

程式：`src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`。文件：`docs/specs/v113-protocol-spec.md`、`docs/devlog/進度日誌.md`、`docs/devlog/任務歷程/README.md`、本任務檔。Commit：待選取 hunks 後再填。
