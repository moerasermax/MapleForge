---
編號: 2026-09-24_80
標題: P078 — SPECIAL_MOVE 施放成功送 HP/MP 更新（itemReaction）+ 死亡時 enableActions
類型: 移植
狀態: ✅ 完成
建立: 2026-09-24
更新: 2026-09-24
關聯里程碑: P078（/goal 第 8 件）
關聯記憶: <空>
關聯commit: 見 git log（P078）
---

## 🎯 目標（執行前先寫死，過程不偷改）

對照 Java `MapleStatEffect.applyTo`：技能成功套用後一律送 `updatePlayerStats(HP[, MP], itemReaction=true)`；
`SpecialMove` 開頭玩家死亡送 `enableActions`。MapleForge 施放技能扣了 MP 卻從不送狀態更新（MP 條不同步、非 buff
技能施放後客戶端不解鎖）。完成判準：兩種封包接上、測試綠燈、build 0/0、全測試無退化。

## 📋 背景與查證

- Java `PlayerHandler.SpecialMove`：`!chr.isAlive()` → `enableActions` + return。
- Java `MapleStatEffect.applyTo`（608 行起）：`hpchange != 0` 改 HP；`mpchange != 0` 改 MP 並加入更新清單；HP 一律加入；
  `sendPacket(updatePlayerStats(hpmpupdate, true, job))`，之後才 `applyBuffEffect`（buff 封包在後）。
  MP 不足 `stat.getMp() + mpchange < 0` → return false，不送封包（其下 enableActions 分支條件相同，實際不可達）。
- 封包順序：`skillCooldown`（套用前）→ HP/MP 更新 → buff。MapleForge 原本是 buff → 冷卻，一併調整成 Java 順序。

## 🪜 計畫步驟

- [x] 1. `V113SkillHandleResult.StatsPacket`；`HandleSpecialMove` 成功送 HP/MP（MP 有變動才帶）、死亡送 `EnableActions`
- [x] 2. 連線 handler 依 Java 順序送：冷卻 → 狀態 → buff
- [x] 3. 測試、build、全測試、三本帳、commit

## 📜 執行歷程（邊做邊追加，附時間）

- 依計畫實作；MP 是否帶入以施放前後實際差異判斷（對應 Java `mpchange != 0`）。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成並 commit。候選：武陵/金字塔技能在對應地圖可免學施放（需武陵能量系統）。

## ✅ 結果與結論

- 達標：施放技能後客戶端 HP/MP 與伺服器同步並解鎖；死亡時施放不再卡住客戶端。
- 1113 passed / 1 skipped（P077 基準 1111 +2：Adapters.V113 554→556）；build 0/0；禁區 grep clean。

## 🔗 產出

- 修改：`src/Maple.Adapters.V113/Channel/V113SkillPackets.cs`、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- 測試：`tests/Maple.Adapters.V113.Tests/ChannelSkillPacketTests.cs`
