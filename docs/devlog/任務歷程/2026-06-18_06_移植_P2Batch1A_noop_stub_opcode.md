---
編號: 2026-06-18_06
標題: P2 Migration Batch 1A trivial no-op/stub/log opcode handlers
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 11:41
更新: 2026-06-18 11:48
關聯里程碑: P2 opcode migration
關聯記憶:
關聯commit: 7e587e8 (Wave 1 aggregate)
---

## 🎯 目標（執行前先寫死，過程不偷改）

在 `Maple.Adapters.V113` 補上 P2 Batch 1A 的 12 個低風險 recv opcode：9 個 Channel no-op/stub/EnableActions case、3 個 Login log-only case。

完成判準：

- `V113ChannelOpcodes.cs` 新增 `StrangeData`、`CalcDamageStatSetRequest`、`ShowExpChair`、`CygnusSummon`、`Snowball`、`LeftKnockBack`、`GamePoll`、`MapleTV`、`BeansUpdate`。
- `V113ChannelConnectionHandler.cs` 只新增上述 dispatch case，不改既有 handler 語義。
- `V113Opcodes.cs` 新增 `ClientError`、`ClientFeedback`、`ClientLogout`。
- `V113LoginConnectionHandler.cs` 新增上述 log-only dispatch case。
- `dotnet build src/Maple.Host.Shared/Maple.Host.Shared.csproj --nologo -v quiet` 0 errors。
- `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` 維持既有測試通過。
- 不新增測試檔；不修改舊 Java server、client binaries、WZ references 或 sibling projects。

## 📋 背景與假設

使用者已提供 Java 參考行為與 opcode 數值。本批 opcode 都屬於 Java 端忽略、stub、或目前 MapleForge 尚無完整子系統的低風險入口；協定數值與 byte-layout 知識只放在 `Maple.Adapters.V113`。

## 🪜 計畫步驟

- [x] 1. 檢查既有 opcode 常數與 dispatch 排序/命名慣例。
- [x] 2. 新增 Channel 9 個常數與 dispatch case。
- [x] 3. 新增 Login 3 個常數與 log-only dispatch case。
- [x] 4. 跑 Host.Shared build 與 Adapters.V113 targeted tests。
- [x] 5. 回填任務歷程、進度日誌與必要協定註記。

## 📜 執行歷程（邊做邊追加，附時間）

- **11:41** 已完成 session 規範、devlog、架構、protocol/test/capture 方法論文件讀取；開始建立本批任務歷程。
- **11:47** 已新增 Batch 1A 的 12 個 opcode 常數與 dispatch case；偵測到同檔存在非本批的並行 P2 handler/opcode 變更，先保留不回退。
- **11:48** `Maple.Host.Shared` build 0 warning / 0 error；`Maple.Adapters.V113.Tests` 299 passed + 1 skipped。同步 `v113-protocol-spec.md` 與 `進度日誌.md`。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

本批已完成。若接手 checkpoint，需注意工作區同時有非本批 P2 untracked journal / handler-opcode 變更；不要誤回退。

## ✅ 結果與結論

達標。12 個 P2 Batch 1A recv opcode 已接入 `Maple.Adapters.V113`：Login 3 個 log-only case、Channel 2 個 no-op case、7 個 release-client stub case。未新增測試檔；以 Java source + targeted build/test 作為證據。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `src/Maple.Adapters.V113/Login/V113Opcodes.cs`
- `src/Maple.Adapters.V113/Login/V113LoginConnectionHandler.cs`
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/進度日誌.md`
- `docs/devlog/任務歷程/README.md`
- commit：未建立（同檔存在非本批 Batch 1B 變更，避免混合 checkpoint）
