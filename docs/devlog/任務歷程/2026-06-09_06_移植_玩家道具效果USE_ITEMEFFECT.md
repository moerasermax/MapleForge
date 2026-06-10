---
編號: 2026-06-09_06
標題: 移植玩家道具效果 USE_ITEMEFFECT
類型: 移植
狀態: ✅ 完成
建立: 2026-06-09
更新: 2026-06-09
關聯里程碑: Java→.NET 移植主線 / 玩家體感小功能
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植舊 Java `PlayerHandler.UseItemEffect` 的基礎道具效果視覺同步，讓 v113 client 送出 `USE_ITEMEFFECT(0x2D)` 後，MapleForge 檢查角色持有該道具並廣播 `SHOW_ITEM_EFFECT(0xBA)`；同時接上 `CANCEL_ITEM_EFFECT(0x43)` 的基礎清除語義。

完成判準：

1. Core 以版本無關 runtime 狀態追蹤目前 item effect，不持久化到 `Character`。
2. `V113ChannelRecvOp` 新增 `UseItemEffect=0x2D`、`CancelItemEffect=0x43`；`V113ChannelSendOp` 新增 `ShowItemEffect=0xBA`。
3. `V113MapPackets.ItemEffect(characterId,itemId)` 對齊 Java layout：`[opcode][int charId][int itemId]`。
4. Channel handler 接上基礎行為：使用時檢查背包持有、更新 runtime effect、廣播；取消時清除 runtime effect 並廣播 itemId=0。
5. 有針對性單元測試與 build 驗證。
6. 不修改舊 Java server、真客戶端、WZ 參考或姊妹專案。

## 📋 背景與假設

- 使用者要求自動推進；上一輪已完成 `USE_CHAIR/CANCEL_CHAIR`。
- 舊 Java `CancelItemEffect` 透過 `cancelEffect(getItemEffect(-id))` 間接取消，本輪先採 `SHOW_ITEM_EFFECT(itemId=0)` 作為基礎清除語義，需真機 UI smoke 確認。
- `itemId == 5510000` 在 Java 不寫入 `chr.setItemEffect`，本輪保留這個特殊分支：會廣播效果但不更新 runtime current effect。

## 🪜 計畫步驟

- [x] 1. 建檔定目標。
- [x] 2. 實作 Player runtime item effect 狀態。
- [x] 3. 實作 v113 opcode、packet、handler。
- [x] 4. 新增測試。
- [x] 5. 跑針對性驗證。
- [x] 6. 回填結果與進度日誌。

## 📜 執行歷程（邊做邊追加，附時間）

- 建檔；Java 來源已定位：`recv.properties USE_ITEMEFFECT=0x2D / CANCEL_ITEM_EFFECT=0x43`、`send.properties SHOW_ITEM_EFFECT=0xBA`、`MaplePacketCreator.itemEffect`。
- 實作完成：`Player.ItemEffectItemId` runtime state + `UseItemEffect/CancelItemEffect`；v113 opcode + `V113MapPackets.ItemEffect`；Channel handler `HandleUseItemEffectAsync/HandleCancelItemEffectAsync`。
- 測試完成：Core `PlayerItemEffectTests` 2 筆；Adapters `ChannelItemEffectPacketTests` 3 筆。
- 驗證完成：Core ItemEffect filter 2/2；Adapters ItemEffect filter 3/3；Core 全測 30/30；Adapters 全測 126/126；`Maple.Host.Login` build 0 警告 0 錯誤。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成 `USE_ITEMEFFECT/CANCEL_ITEM_EFFECT` 基礎視覺同步。下一手可轉 Keymap/SkillMacro，或先做 #12 真機 smoke；`CANCEL_ITEM_EFFECT` 的 itemId=0 清除語義仍待真機 UI 確認。

## ✅ 結果與結論

- **道具效果基礎行為已移植**：client 送 `USE_ITEMEFFECT(0x2D)` + `int itemId` 後，server 檢查背包持有，更新 runtime item effect（保留 Java `5510000` 不寫 current effect 的特殊分支），並廣播 `SHOW_ITEM_EFFECT(0xBA)` 給同地圖其他玩家。
- **取消效果已接基礎清除**：client 送 `CANCEL_ITEM_EFFECT(0x43)` 後，server 清除 runtime effect 並廣播 `SHOW_ITEM_EFFECT(itemId=0)`；此清除語義需真機 UI smoke 確認。
- **分層維持乾淨**：item effect 狀態是 `Player` runtime state，不新增 `Character` 持久欄位；v113 opcode/byte layout 只在 `Adapters.V113`。
- **測試結果**：
  - `dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj --filter ItemEffect --no-restore`：通過 2/2。
  - `dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj --filter ItemEffect --no-restore`：通過 3/3。
  - `dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj --no-restore`：通過 30/30。
  - `dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj --no-restore`：通過 126/126。
  - `dotnet build src\Maple.Host.Login\Maple.Host.Login.csproj --no-restore`：0 warning / 0 error。

## 🔗 產出

- `src/Maple.Core/World/Player.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113MapPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `tests/Maple.Core.Tests/World/PlayerItemEffectTests.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelItemEffectPacketTests.cs`
- `docs/specs/v113-protocol-spec.md`
