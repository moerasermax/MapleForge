---
編號: 2026-10-26_224
標題: P218 NPC gainItem 放不下時送 enableActions
類型: 移植
狀態: ✅ 完成
建立: 2026-10-26
更新: 2026-10-26
關聯里程碑: Java 行為對齊
關聯記憶: 無
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

1. NpcUiEffectKind 新增 EnableActions
2. NpcContext.GainItem 在 quantity > 0 且 CanHold 失敗時排入 EnableActions（Java gainItem !checkSpace 分支）
3. adapter 對 EnableActions 送 MaplePacketCreator.enableActions
4. 補測試

**不在範圍**：Java 扣除分支 haveItem(id, 負數) 恆真、removeById 會扣到剩多少算多少；我方不足時整筆不扣（差異未處理）

## 📋 背景與假設

- Java 神諭：AbstractPlayerInteraction.gainItem:511-515

## 🪜 計畫步驟

- [x] 1. NpcUiEffectKind 新增 EnableActions
- [x] 2. NpcContext.GainItem 在 quantity > 0 且 CanHold 失敗時排入 EnableActions（Java gainItem !checkSpace 分支）
- [x] 3. adapter 對 EnableActions 送 MaplePacketCreator.enableActions
- [x] 4. 補測試

## 📜 執行歷程（邊做邊追加，附時間）

- 全 8 專案 0 failed（build 0/0；8 專案全綠 1759 passed）；build 0/0。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

P218 已完成。

## ✅ 結果與結論

NPC 給道具失敗時客戶端不會卡住

## 🔗 產出

- 修改：src/Maple.Application/Npc/NpcDialog.cs;src/Maple.Application/Npc/NpcContext.cs;src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs;tests/Maple.Application.Tests/Npcs/NpcContextTests.cs
- commit：見 git log（P218）
