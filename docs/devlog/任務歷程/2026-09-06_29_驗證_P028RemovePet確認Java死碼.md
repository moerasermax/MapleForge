---
編號: 2026-09-06_29
標題: P028 — RemovePet 確認為 Java 死碼（不修）
類型: 驗證
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P028
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標

延續 P021 之後的全面重掃，找到新的零呼叫者候選 `V113PetPackets.RemovePet`，查證是否為真缺口。

## 📋 查證過程

- Java `tools/packet/PetPacket.java` 有兩個 `removePet` 多載（`(chr, slot)`／`(cid, index)`）。
- 全域搜尋 `PetPacket.removePet` 呼叫端：**零命中**——這兩個多載在整個 Java 原始碼樹裡都沒有
  任何呼叫端，是 Java 自己的死碼。
- 唯一疑似相關的 `MapleCharacter.removePet(MaplePet pet)`（`CashShopOperation.java:467` 販售/
  刪除寵物時呼叫）純粹是資料模型變動（`pet.setSummoned(0); pets.remove(pet);`），**不會**呼叫
  `PetPacket.removePet` 建構或送出任何封包——client 端寵物移除似乎是透過其他通用封包（如購物/
  背包更新）推斷，不是靠一支專用的「移除寵物」封包。

## ✅ 結果與結論

- `RemovePet` 確認為 Java 死碼（封包方法定義但從未被建構呼叫），歸入死碼清單第七項（接續
  `UpdateAllianceMember`/`GuildMemberLevelJobUpdate`/`FamilyResult`/`UpdateHiredMerchant`/
  `MerchantBuyError`/`MarriageUpdate`）。
- 全面重掃零呼叫者封包候選清單至此清空（`RemoveTownPortal`/`SpawnPortal` 除外，仍需要
  `SPECIAL_MOVE(0x55)` 前置設計）；`TradeChat`/`TradePartnerAdd` 確認仍是既有的同檔案內部
  dispatch 假陽性（P017 已記錄）。下一輪需要改用其他排查手法（對稱性掃描/里程碑殘留 TODO）
  尋找新候選。

## 🔗 產出

- 無程式碼異動（純查證結論）。
- commit：待填
