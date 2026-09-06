---
編號: 2026-09-06_33
標題: P032 — ITEM_UNLOCK 補齊 UNTRADEABLE 分支、任意背包類型、解鎖鑰匙消耗（收官 M4-6「Phase A」註記）
類型: 修補
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P032
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

任務追蹤.md M4-6 條目裡「`ITEM_UNLOCK(0x95)` Phase A 清 LOCK flag」的殘留註記——「Phase A」暗示
只做了一半。查證後確認 `V113ItemUnlockHandler` 只做了「清 LOCK 旗標」且**硬編死裝備欄**，缺
Java 對照行為的三塊：(1) UNTRADEABLE 分支、(2) 任意背包類型（非僅裝備）、(3) 消耗解鎖鑰匙道具。
完成判準：三塊全部對照 Java 補齊。

## 📋 背景與假設

- Java `PlayersHandler.UnlockItem`（`handling/channel/handler/PlayersHandler.java:323-356`）：
  解析封包給的**背包類型**（非固定裝備欄）+ slot，若該道具有 `LOCK` 旗標就清（優先）；否則若有
  `UNTRADEABLE` 旗標就清（其次，`if/else if`，兩者都設時只清一種）；只要清了任一旗標，就無條件
  嘗試從 USE 背包移除一顆「封印之鎖解除鑰匙」（itemId `2051000`，`MapleInventoryManipulator.
  removeById`——這類 `removeById` 實作在道具不存在時通常靜默無效果，不會擋住旗標清除本身）。
- MapleForge 既有 `V113ItemUnlockHandler.Handle` 只處理：`player.Inventory.By(InventoryType.Equip)`
  （封包裡的背包類型欄位被 `Parse` 讀出來又直接丟棄）+ 只檢查 `ItemFlags.Lock`（`ItemFlags.
  Untradeable` 常數其實早就存在於 `ItemFlags.cs`，只是這個 handler 沒用到）+ 完全沒有鑰匙消耗
  邏輯。
- `Item`（`Equip` 的基底類別）本身就有 `Flag` 屬性，不需要窄化成 `Equip` 型別；
  `V113InventoryPackets.ModifyItemUpdate(InventoryType, short, Item)` 早就是泛型簽章；
  `Player.TryConsumeInventoryItem`（P021/P030/P031 都用過的既有方法）可以直接拿來扣鑰匙，只需
  先用 `Inventory.Items` 找出鑰匙所在 slot（Java 的 `removeById` 概念上是「找到第一個符合 id 的
  slot 扣」，MapleForge 沒有現成的「依 id 扣」方法，但既有的「依 slot 扣」方法組合up 就夠用）。

## 🔧 實作內容

- **`Maple.Adapters.V113`**（重寫 `V113ItemUnlockHandler.cs`）：
  - `V113ItemUnlockRequest` 新增 `InventoryType Type`欄位（預設 `Equip`，維持只有 2-byte slot
    的舊呼叫端相容）；`Parse` 的 6-byte 分支改為真正解析類型欄位，不再丟棄。
  - `Handle`：`Enum.IsDefined(request.Type)` 驗證後用 `player.Inventory.By(request.Type)` 取代
    寫死的 `Equip`；`LOCK` 優先、`UNTRADEABLE` 其次的 `if/else if` 判斷；清除成功後找 USE 背包
    裡 `ItemId == UnlockKeyItemId(2051000)` 的第一個 slot，用既有 `TryConsumeInventoryItem` 扣
    1 顆（找不到就跳過，不擋旗標清除）。
  - 刻意跳過的細節：Java 的「已經解鎖！」聊天通知（`dropMessage(5, ...)`）——MapleForge 目前沒有
    對應的 server notice 封包基礎設施，屬於純 UI 提示、不影響遊戲狀態，留給後續有需要時再補（跟
    這次補的三塊比，屬於低優先級的裝飾性缺口，不擴大這次範圍）。

## 🧪 測試

- `ChannelPhaseAOpcodeHandlerTests.cs` 新增 5 組：清 LOCK 時有鑰匙正確消耗、沒鑰匙時仍正確清除
  旗標（不擋清除本身）、UNTRADEABLE 分支獨立驗證、LOCK+UNTRADEABLE 同時設時只清 LOCK（驗證
  `if/else if` 而非兩者都清）、非裝備欄（USE 背包）道具正確依封包欄位選對背包類型。
- 既有 3 組測試（鎖裝備清除、缺道具、已解鎖裝備）維持不動且全部通過，確認沒有破壞舊行為。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 937 passed / 1 skipped（P031 收案基準 932 +5：
  Adapters.V113 +5）；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- 這是本輪第一次從「任務追蹤.md 裡的既有註記」（而非零呼叫者掃描或 FieldLimitType 排查）直接
  找到範圍明確的缺口——「Phase A」這種字眼本身就是很好的排查線索，暗示移植者當時已經知道還有
  沒做完的部分，值得往後系統性搜尋任務追蹤.md 裡類似「Phase A/MVP/部分/僅/暫」這類用詞找更多
  候選。
- `ItemFlags.Untradeable` 常數早就存在但沒被這個 handler 使用，跟 P029/P030「基礎設施已就緒但
  沒被使用」是同一種模式的變形——這次是「同一個檔案裡，某個既有常數沒被某段邏輯用到」，比跨檔案
  的「服務層有方法但呼叫端沒接」更細微，值得往後檢查某個 flag/enum 常數時，順手看看同檔案裡是否
  所有分支都真的用到了它。

## 🔗 產出

- 修改：`src/Maple.Adapters.V113/Channel/V113ItemUnlockHandler.cs`
- 修改（測試）：`tests/Maple.Adapters.V113.Tests/ChannelPhaseAOpcodeHandlerTests.cs`
- commit：待填
