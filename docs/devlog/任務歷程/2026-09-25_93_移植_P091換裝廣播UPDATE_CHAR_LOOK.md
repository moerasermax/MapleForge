---
編號: 2026-09-25_93
標題: P091 — 換裝後廣播 UPDATE_CHAR_LOOK（0xBE）給同圖其他玩家
類型: 移植
狀態: ✅ 完成
建立: 2026-09-25
更新: 2026-09-25
關聯里程碑: P091（/goal 第 21 件，零呼叫者掃描收尾）
關聯記憶: <空>
關聯commit: 見 git log（P091）
---

## 🎯 目標（執行前先寫死，過程不偷改）

零呼叫者候選 `V113RingPackets.MarriageRingLook` 追查：它是 Java `spawnPlayerMapobject` 與 `updateCharLook` 共用的片段；MapleForge 根本沒有
`UPDATE_CHAR_LOOK`，換裝後同圖其他玩家看不到外觀改變（要等重新進圖）。對照 Java `equipChanged` 補上廣播。完成判準：封包版型對照 Java、
裝備/脫裝成功後廣播、測試綠燈、build 0/0、全測試無退化。

## 📋 背景與查證

- Java `MapleCharacter.equipChanged`：`map.broadcastMessage(this, updateCharLook(this), false)`（不含自己）+ `recalcLocalStats` + 更新 Messenger 外觀。
  呼叫點：`MapleInventoryManipulator.equip`/`unequip`（739/809 行）等。
- Java `updateCharLook`：`int id + byte 1 + addCharLook(false) + addRingInfo(戒指清單) + addMarriageRingLook + int 0`；`send.properties` `UPDATE_CHAR_LOOK = 0xBE`。
- Java `MaplePacketCreator.addRingInfo`：`byte (size>0) + int size + 每枚 long ringId/long partnerRingId/int itemId`。
- 零呼叫者掃描收尾：`MerchantBuyError`（Java `Merchant_Buy_Error` 也無人呼叫 = Java 死碼，不做）；`CreateGuildAlliance`（Java 由 NPC 腳本
  `cm.createAlliance` 同步回傳 bool 觸發，MapleForge 腳本橋接只有「pending 旗標 + 腳本後回呼」模式，需先拍板同步 API 設計，列待決策）。
- 查證副產物：`SPAWN_PLAYER` 尾段戒指區塊 MapleForge 4 bytes vs Java 10 bytes，需真客戶端 capture 判定，已記入協定規格待驗證。

## 🪜 計畫步驟

- [x] 1. `V113ChannelSendOp.UpdateCharLook = 0xBE`；`V113MapPackets.UpdateCharLook(player)`（重用 `AddCharLook` 與 `MarriageRingLook`）
- [x] 2. `HandleItemMoveAsync`：Equip/Unequip 成功 → `BroadcastPacketToOthersAsync`
- [x] 3. 測試、build、全測試、三本帳、協定規格、commit

## 📜 執行歷程（邊做邊追加，附時間）

- 依計畫實作；Messenger 外觀更新（`updateMessenger`）不在本 P。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成並 commit。候選：換裝後 Messenger 外觀更新；其他 `equipChanged` 呼叫點（道具到期脫裝等）。

## ✅ 結果與結論

- 達標：換裝/脫裝後同圖其他玩家立即看到新外觀。
- 1157 passed / 1 skipped（P090 基準 1156 +1：Adapters.V113 567→568）；build 0/0；禁區 grep clean。封包 unverified。

## 🔗 產出

- 修改：`src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`、`src/Maple.Adapters.V113/Channel/V113MapPackets.cs`、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- 測試：`tests/Maple.Adapters.V113.Tests/ChannelUpdateCharLookTests.cs`
- 文件：`docs/specs/v113-protocol-spec.md`
