---
編號: 2026-06-09_05
標題: 移植玩家椅子 USE_CHAIR / CANCEL_CHAIR
類型: 移植
狀態: ✅ 完成
建立: 2026-06-09
更新: 2026-06-09
關聯里程碑: Java→.NET 移植主線 / 玩家體感小功能
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植舊 Java `PlayerHandler.UseChair` / `CancelChair` 的基礎椅子行為，讓 v113 client 能送出 `USE_CHAIR(0x23)` 與 `CANCEL_CHAIR(0x22)`，MapleForge 依 Java 封包格式送出/廣播 `SHOW_CHAIR(0xBD)` 與 `CANCEL_CHAIR(0xC6)`。

完成判準：

1. Core 以版本無關的 runtime 狀態追蹤玩家目前椅子，不把椅子 byte layout 放進 Core/Application。
2. `V113ChannelRecvOp` 新增 `UseChair=0x23`、`CancelChair=0x22`；`V113ChannelSendOp` 新增 `ShowChair=0xBD`、`CancelChair=0xC6`。
3. `V113MapPackets` 新增 Java layout：`showChair(characterId,itemId)` = `[opcode][int charId][int itemId]`；`cancelChair(id)` = `[opcode][byte 0]` when `id == -1` else `[opcode][byte 1][short id]`。
4. Channel handler 接上基礎行為：使用椅子時檢查角色背包持有該椅子，成功後更新 runtime chair 並廣播；取消椅子時同步自身與其他玩家。
5. 有針對性單元測試與 build 驗證。
6. 不修改舊 Java server、真客戶端、WZ 參考或姊妹專案。

## 📋 背景與假設

- 使用者要求自動推進；上一輪已完成 `FACE_EXPRESSION`。
- 舊 Java `UseChair` 牽涉釣魚地圖、飛行椅 mount buff、封包修改封鎖等完整營運邏輯；MapleForge 目前未完整移植釣魚/反作弊/mount buff，本輪只移植基礎坐椅子與取消椅子的 client 視覺同步。
- 椅子是瞬時狀態，不應持久化到 `Character` 文件；本輪放在 `Player` runtime state。

## 🪜 計畫步驟

- [x] 1. 建檔定目標。
- [x] 2. 實作 Player runtime chair 狀態。
- [x] 3. 實作 v113 opcode、packet、handler。
- [x] 4. 新增測試。
- [x] 5. 跑針對性驗證。
- [x] 6. 回填結果與進度日誌。

## 📜 執行歷程（邊做邊追加，附時間）

- 建檔；Java 來源已定位：`recv.properties CANCEL_CHAIR=0x22 / USE_CHAIR=0x23`、`send.properties SHOW_CHAIR=0xBD / CANCEL_CHAIR=0xC6`、`MaplePacketCreator.showChair/cancelChair`。
- 實作完成：`Player.ChairItemId` runtime state + `UseChair/CancelChair/UseMapChair`；v113 opcode + `V113MapPackets.ShowChair/CancelChair`；Channel handler `HandleUseChairAsync/HandleCancelChairAsync`。
- 測試完成：Core `PlayerChairTests` 3 筆；Adapters `ChannelChairPacketTests` 4 筆。
- 驗證完成：Core Chair filter 3/3；Adapters Chair filter 4/4；Core 全測 28/28；Adapters 全測 123/123；`Maple.Host.Login` build 0 警告 0 錯誤。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成 `USE_CHAIR/CANCEL_CHAIR` 基礎移植。下一手可繼續 `USE_ITEMEFFECT/CANCEL_ITEM_EFFECT`、Keymap/SkillMacro，或先做 #12 真機 smoke；本功能仍待真機椅子 UI smoke，釣魚椅/飛行椅 mount buff 特殊語義未補。

## ✅ 結果與結論

- **椅子基礎行為已移植**：client 送 `USE_CHAIR(0x23)` + `int itemId` 後，server 檢查背包持有，更新 `Player.ChairItemId`，廣播 `SHOW_CHAIR(0xBD)` 給同地圖其他玩家；client 送 `CANCEL_CHAIR(0x22)` 後，server 依 `id` 回 `CANCEL_CHAIR(0xC6)` 並在 `id == -1` 時廣播 `SHOW_CHAIR(itemId=0)`。
- **分層維持乾淨**：椅子狀態是 `Player` runtime state，不新增 `Character` 持久欄位；v113 opcode/byte layout 只在 `Adapters.V113`。
- **限制**：舊 Java 的釣魚椅、飛行椅 mount buff、反作弊封鎖分支尚未移植；現階段是基礎視覺同步與基本持有檢查。
- **測試結果**：
  - `dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj --filter Chair --no-restore`：通過 3/3。
  - `dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj --filter Chair --no-restore`：通過 4/4。
  - `dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj --no-restore`：通過 28/28。
  - `dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj --no-restore`：通過 123/123。
  - `dotnet build src\Maple.Host.Login\Maple.Host.Login.csproj --no-restore`：0 warning / 0 error。

## 🔗 產出

- `src/Maple.Core/World/Player.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113MapPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `tests/Maple.Core.Tests/World/PlayerChairTests.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelChairPacketTests.cs`
- `docs/specs/v113-protocol-spec.md`
