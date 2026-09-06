---
編號: 2026-09-06_28
標題: P027 — MarriageUpdate 確認為 Java 死碼（不修）
類型: 驗證
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P027
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

查證 P020 收尾列出的「需要前置設計」候選：`MarriageUpdate`（P020 筆記猜測「婚姻系統疑似完全
未移植」）。完成判準：確認這個候選是否真的需要移植婚姻系統，還是像 P021/P025/P026 一樣可以
拆解出獨立可完成的小範圍，或是像 `UpdateAllianceMember` 等既有案例一樣屬於 Java 死碼。

## 📋 背景與查證過程

- `V113RingPackets.MarriageUpdate(characterName, family)` 對應的 opcode 是
  `MarriageUpdateSendOpcode = 0x62`。
- 全域搜尋 Java 原始碼 `marriageUpdate`（不分大小寫）：**零命中**——不只找不到呼叫端，連
  `MaplePacketCreator.java` 裡都沒有任何方法叫這個名字或建構這個 opcode 的封包。
- 進一步查 `handling/SendPacketOpcode.java` 與 `properties/send.properties`，確認
  `MARRIAGE_UPDATE(0x62)` **有定義在 opcode 表裡**，但整個 Java 原始碼樹沒有任何地方使用這個
  opcode 常數去建構或送出封包——這是 Java 自己「定義了但從沒用過」的 dead opcode，不是
  MapleForge 漏移植。
- **推翻 P020「婚姻系統疑似完全未移植」的猜測**：檢查 MapleForge 既有程式碼才發現婚姻/戒指
  系統其實已經有相當完整的實作——`Player.Ring.cs`（Core）、`RingService.cs`（Application）、
  `V113RingHandler.cs`/`V113RingPackets.cs`（Adapters）都存在，涵蓋求婚（`MarriageRequest`）、
  結果（`MarriageResult`）、戒指外觀（`MarriageRingLook`）、`player.HasVisibleMarriageRing`/
  `player.MarriagePartnerCharacterId` 等狀態。P020 當時只看到 `MarriageUpdate` 這一個零呼叫者
  封包，就直接猜測整個婚姻系統未移植，沒有先查 MapleForge 既有程式碼裡實際涵蓋了多少。

## ✅ 結果與結論

- `MarriageUpdate` 確認為 **Java 自身死碼**（定義了 opcode 但從未建構/發送對應封包），維持不修，
  歸入既有的死碼清單（`UpdateAllianceMember`/`GuildMemberLevelJobUpdate`/`FamilyResult`/
  `UpdateHiredMerchant`/`MerchantBuyError` 之後的第六個）。
- 這次的教訓跟 P021（`UpdateBuddyCapacity`）、P025（`StopControllingMonster`）同一類——**範圍
  評估本身要先查證，不能只憑一個零呼叫者候選的名稱去猜測背後系統的完成度**。這裡的差異是：
  查證結果不是「範圍比想像中小、可以做」，而是「這個特定候選根本不需要做」，但同樣的查證動作
  （先看 Java 有沒有真的用、再看 MapleForge 既有程式碼涵蓋多少）才是關鍵，不能省略。
- P020 列出的四個「需要前置設計」候選至此全部查證完畢：`StopControllingMonster`（P025，拆出
  獨立可完成的死亡通知子範圍）、Door 傳送門建立（查證後確認真的需要 `SPECIAL_MOVE(0x55)` 完整
  技能效果引擎，維持前置設計判斷不變）、`EventMiniGame` Beans（P026，拆出獨立可完成的兩個
  reward 分支）、`MarriageUpdate`（本次，確認 Java 死碼不需修）。

## 🔗 產出

- 無程式碼異動（純查證結論）。
- commit：待填
