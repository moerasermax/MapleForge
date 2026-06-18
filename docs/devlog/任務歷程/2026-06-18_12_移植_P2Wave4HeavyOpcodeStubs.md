---
編號: 2026-06-18_12
標題: P2 Migration Wave 4 heavy opcode MVP stubs
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 13:42
更新: 2026-06-18 13:48
關聯里程碑: M6-1
關聯記憶:
關聯commit: dd2ce07 (Wave 4 aggregate)
---

## 🎯 目標（執行前先寫死，過程不偷改）

新增 7 個 v113 channel recv opcode 的 adapter-only MVP dispatch stubs：`ENTER_CASH_SHOP(0x20)`、`CP_HiredMerchantRemoteControl(0x34)`、`USE_HIRED_MERCHANT(0x38)`、`MERCH_ITEM_STORE(0x3A)`、`ENTER_MTS(0x99)`、`TOUCHING_MTS(0xFA)`、`MTS_TAB(0xFB)`。完成判準：`V113ChannelRecvOp` 有 7 個常數；`V113ChannelConnectionHandler` 有 7 個 case，只讀最小欄位或 no-op 後送 `EnableActions`；不新增 Core/Application model、service 或 test file；同步 protocol/worklog；`Maple.Host.Shared` build 0 errors；`Maple.Adapters.V113.Tests` 維持 299 passed + 1 skipped。

## 📋 背景與假設

這 7 個 opcode 對應 HiredMerchant、MTS、CashShop 的大型子系統，完整移植需要 player shop / merchant storage / MTS auction / cash shop cross-server mode 設計。本輪只依 Java source map 與使用者範圍做 MVP stub，目標是解除未處理 opcode 與 client action lock，不宣稱功能完成。

Java 行為神諭：

- `handling/channel/handler/HiredMerchantHandler.java`
- `handling/channel/handler/InterServerHandler.java`
- `handling/channel/handler/MTSOperation.java`

## 🪜 計畫步驟

- [x] 1. 檢查既有 opcode 常數與 channel dispatch stub pattern。
- [x] 2. 新增 7 個 recv opcode 常數與 7 個 dispatch case。
- [x] 3. 同步 `v113-protocol-spec.md`、本任務歷程與 `進度日誌.md`。
- [x] 4. 執行指定 build/test 驗證。

## 📜 執行歷程（邊做邊追加，附時間）

- **13:42** 完成 session 規範、架構、protocol、測試策略與現況文件讀取；起始 `git status` 乾淨。
- **13:43** 發現工作區出現既有 Wave 3 未提交修改與 `2026-06-18_11` 任務檔；本任務改用 `2026-06-18_12`，並保留 Wave 3 內容不回退。
- **13:46** 新增 7 個 Wave 4 recv opcode 常數與 dispatch case；只讀任務指定最小欄位或 no-op 後送 `EnableActions`。
- **13:48** 驗證通過：Host.Shared build 0 warning/0 error；Adapters.V113 tests 299 passed + 1 skipped。同步 protocol spec、進度日誌與任務歷程。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

完成：Wave 4 的 7 個 opcode 已接成 adapter-only MVP stub 並通過指定 build/test。若接手後要繼續，先檢查工作區仍含 Wave 3 既有未提交變更，避免把不同批次誤拆或誤回退。

## ✅ 結果與結論

達標。7 個 heavy subsystem opcode 已從未知/未處理狀態接成 MVP stub，所有 v113 byte/opcode 知識只留在 `Maple.Adapters.V113`，未新增 Core/Application 模型、service 或測試檔。完整 CashShop/HiredMerchant/MTS 子系統仍待後續設計與真 client/capture 校準。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/進度日誌.md`
- `docs/devlog/任務歷程/README.md`
- `docs/devlog/任務歷程/2026-06-18_12_移植_P2Wave4HeavyOpcodeStubs.md`
- commit：未提交（工作區含既有 Wave 3 未提交變更，本任務不單獨混合 checkpoint）
