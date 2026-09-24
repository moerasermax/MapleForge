# 世界 tick（M4-2）設計與消費者清單

> 狀態：活文件。每新增/修改一個世界 tick 消費者就同步更新本檔。建立：2026-09-24（P073）。

## 對照神諭

Java `World.Respawn`（`WorldTimer.register(new Respawn(), 3000)`）每 3 秒巡所有頻道的所有地圖：

- `handleMap(map, numTimes, size)`：掉落物過期／轉 FFA；有玩家的地圖才重生怪物、才跑逐玩家處理。
- `handleCooldowns(chr, numTimes, hurt)`：技能冷卻到期、異常狀態到期、寵物飢餓（每 100 tick）、坐騎疲勞
  （每 18000 tick）、龍騎士 Dragon Blood/Berserk、`doRecovery`、地圖持續扣血（`map.canHurt()`）。

## MapleForge 結構

- 排程器：`Maple.Host.Shared/WorldTickHostedService`（`PeriodicTimer` 3 秒，巡本 channel process 的
  `IFieldInstanceRegistry.All`）。每個消費者各自 try/catch，單一 field 失敗不影響下一個。
- 消費者是 `Maple.Adapters.V113` 的薄封裝 handler：`lock(field)` 內呼叫 Application 用例做領域變更，
  鎖外 best-effort 送封包（`lock` 不能橫跨 `await`）。

| 消費者 | Java 來源 | MapleForge | P |
|---|---|---|---|
| 掉落物過期 | `item.shouldExpire()` / `expire(map)` | `V113DropExpiryHandler` → `DropService.ExpireDrops` | P061-063 |
| 掉落物轉 FFA | `item.shouldFFA()` | 同上 → `DropService.PromoteFfaDrops` | P069 |
| 怪物重生 | `map.respawn(false)` | `V113MobRespawnHandler` → `CombatService.RespawnMonsters` | P064-067 |
| 技能冷卻到期 | `handleCooldowns` 冷卻迴圈 + `skillCooldown(skil, 0)` | `V113PlayerTickHandler` → `SkillService.ExpireSkillCooldowns` | P073 |
| 地圖持續扣血（資料層） | `setHPDec`/`canHurt()` | `MapData.DecHp/DecHpInterval/ProtectItem` + `FieldHpDecay`（field 建立時 `MapService.InitializeFieldEnvironment`） | P074（尚未扣血） |

## 尚未移植（候選）

- 異常狀態（disease）到期、寵物飢餓／限時寵物、坐騎疲勞、Dragon Blood/Berserk、`doRecovery`、地圖持續扣血。
- Java 的 `numTimes % N` 以 tick 次數計時；MapleForge 傾向改用各物件自己的時間戳（可測、不依賴排程器次數）。
