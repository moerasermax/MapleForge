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
| 地圖持續扣血 | `setHPDec`/`canHurt()` + `handleCooldowns` hurt 分支 | 資料：`MapData.DecHp/DecHpInterval/ProtectItem` + `FieldHpDecay`（field 建立時 `MapService.InitializeFieldEnvironment`）；扣血：`V113PlayerTickHandler` → `FieldHazardService.ApplyHpDecay`，送 HP 更新，扣到 0 先送 `enableActions` | P074-075 |
| buff 到期（含召喚獸/時空門副作用） | 每個 buff 各自的 `BuffTimer` 排程 `cancelEffect`（非 `handleCooldowns`） | `V113PlayerTickHandler` → `SkillService.CancelExpiredBuffs` + `V113BuffCancellationEffects`（3 秒 tick 近似；玩家送封包時的既有檢查保留） | P089 |
| 回復術週期回血 | `handleCooldowns` 的 `canRecover(now)` + `doRecovery`（每 5 秒，滿血取消 buff） | `V113PlayerTickHandler` → `SkillService.TryRecover` → `Player.TryRecover`（計時起點 = buff 套用時間） | P092 |
| 龍之魂週期扣血 | `handleCooldowns` 的 job 131/132 + `canBlood(now)` + `doDragonBlood`（每 4 秒，`hp - x <= 1` 取消 buff） | `V113PlayerTickHandler` → `SkillService.TryDragonBlood` → `Player.TryDragonBlood`；`showOwnBuffEffect`/`showBuffeffect`(effect 5) | P093 |
| 狂戰士狀態特效 | `handleCooldowns` 的 job 132 + `canBerserk()` + `doBerserk`（每 10 秒，`hp <= maxHp * x%`） | `V113PlayerTickHandler` → `SkillService.TryCheckBerserk`；`showOwnBuffEffect(1320006, 1, dir)` | P094 |

## 尚未移植（候選）

- 異常狀態（disease）到期、寵物飢餓／限時寵物、坐騎疲勞。
- 死亡懲罰：P076 已移植 `playerDead` 經驗值區塊（護身符/經驗值損失），怪物打死與地圖扣血兩條路徑都接上；靈魂之石、取消 buff、事件副本、裝備耐久尚未移植。
- Java 的 `numTimes % N` 以 tick 次數計時；MapleForge 傾向改用各物件自己的時間戳（可測、不依賴排程器次數）。
