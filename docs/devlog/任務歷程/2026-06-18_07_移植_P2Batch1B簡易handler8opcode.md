---
編號: 2026-06-18_07
標題: P2 Batch 1B 簡易 handler 8 opcode
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 11:41
更新: 2026-06-18 11:47
關聯里程碑: M4/M6+
關聯記憶: current-state-resume, pm-dispatch-only-workflow
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

> 在 `Maple.Adapters.V113` 補上 P2 Batch 1B 的 8 個 C2S opcode 常數與中央 dispatch：
> `DISPLAY_NODE(0xBE)`、`MONSTER_BOMB(0xBB)`、`FRIENDLY_DAMAGE(0xBA)`、`HYPNOTIZE_DMG(0xBC)`、`WHEEL_OF_FORTUNE(0x2E)`、`PASSIVE_ENERGY(0x28)`、`ARAN_COMBO(0x92)`、`CS_UPDATE(0xE5)`。
>
> 完成判準：
> 1. 8 個常數加入 `V113ChannelRecvOp`。
> 2. 8 個 dispatch case 加入 `V113ChannelConnectionHandler`。
> 3. `WHEEL_OF_FORTUNE` 共用既有 `USE_ITEMEFFECT` handler；`PASSIVE_ENERGY` 共用既有 close-range attack handler。
> 4. 其餘 MVP handler 只讀取任務指定欄位並送 `EnableActions`，不修改既有 handler 邏輯。
> 5. `dotnet build src/Maple.Host.Shared/Maple.Host.Shared.csproj --nologo -v quiet` 0 error。
> 6. `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` 通過。

## 📋 背景與假設

- Java 行為來源：
  - `MobHandler.java`：`handleDisplayNode`、`handleMonsterBomb`、`handleFriendlyDamage`、`HypnotizeDmg`
  - `PlayerHandler.java`：`UseItemEffect` dispatch sibling、`closeRangeAttack(..., true)`、`AranCombo`
  - `CashShopOperation.java`：`sendCashShopUpdate`
- 本批是 low-risk adapter dispatch slice；Core/Application 不應新增 v113 opcode/byte layout。
- `CS_UPDATE` 若現有 CashShop 封包方法不足，依任務範圍先 `EnableActions`。

## 🪜 計畫步驟

- [x] 1. 檢查現有 opcode 常數、dispatch switch、既有 UseItemEffect/CloseRangeAttack/CashShop patterns。
- [x] 2. 只新增 opcode constants 與 switch case，不改既有 handler bodies。
- [x] 3. 補 protocol/worklog 任務紀錄。
- [x] 4. 跑 Host.Shared build 與 Adapters.V113 targeted test。

## 📜 執行歷程（邊做邊追加，附時間）

- **11:41** 建立任務歷程；已讀 session/protocol/design/test/capture 方法論文件，確認本批只落在 `Maple.Adapters.V113`。
- **11:44** 新增 8 個 `V113ChannelRecvOp` 常數；`PASSIVE_ENERGY` 併入既有 close-range attack handler，`WHEEL_OF_FORTUNE` 併入既有 item-effect handler。
- **11:45** 新增 `FRIENDLY_DAMAGE`、`MONSTER_BOMB`、`HYPNOTIZE_DMG`、`DISPLAY_NODE`、`ARAN_COMBO` MVP 讀取+`EnableActions` dispatch；`CS_UPDATE` 使用既有 cash balance/cash inventory packet。
- **11:47** 驗證完成：Host.Shared build 0 warning/0 error；Adapters.V113 tests 299 passed / 1 skipped。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> Batch 1B 已完成且通過指定 build/test。若要 commit，需先處理同一工作區中的 Batch 1A / Login 檔案併行修改，避免把非本批變更混入單一 checkpoint。

## ✅ 結果與結論

> 達標。8 個 opcode 常數與 dispatch case 已加入；`WHEEL_OF_FORTUNE` / `PASSIVE_ENERGY` 確認走既有 handler path；其餘為讀取指定欄位後放行的 MVP。未修改既有 handler method body。`CS_UPDATE` 目前送 cash balances 與 Cash inventory snapshot，gifts/wishlist 尚無模型與 encoder。

## 🔗 產出

> 修改：
> - `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
> - `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
> - `docs/specs/v113-protocol-spec.md`
> - `docs/devlog/進度日誌.md`
> - `docs/devlog/任務歷程/README.md`
>
> 驗證：
> - `dotnet build src/Maple.Host.Shared/Maple.Host.Shared.csproj --nologo -v quiet`：0 warning / 0 error
> - `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo`：299 passed / 1 skipped
>
> commit：未建立；工作區已有 Batch 1A 併行修改與未追蹤任務檔，需避免混入。
