# P003: 移植清尾 + MVP stub 補完整（Codex 主刀）

> 狀態：🚧 執行中（W3 D6 完成，2026-07-06）
> 任務歷程：`docs/devlog/任務歷程/2026-07-06_01_移植_P003清尾與stub補完整.md`

## 計畫凍結區（批准後不改）

### Context

偵察（Sonnet PID 24116）核實 `docs/design/移植狀態地圖.md` 嚴重過期：標 ❌ 的 44 個 opcode 中，**43 個其實已接 dispatch**（~10 個已是完整實作、~33 個是 EnableActions MVP stub），唯一真沒接的 USE_FISH_MERCHANT 在 Java 原版也是「尚未開放」placeholder。真正缺口集中在 **HiredMerchant（Java ~1024 行）、ItemMaker（~568 行）、MonsterCarnival（實際依賴遠大於 handler 145 行）、MTS**。另挖到 bug：`V113Opcodes.cs` 的 `ClientError=0x0C`/`ClientFeedback=0x0F` 與 Java 對調（Java: CLIENT_FEEDBACK=0x0C, CLIENT_ERROR=0x0F）。

使用者拍板：**P003 = 清殘 ❌ + stub 補完整都做**。

**圓桌紀錄**（鐵則 1）：研究席 agy（PID 44740 ✅）、執行席 Sonnet（PID 42024 ✅，續偵察 session）、Review 席 GPT-5.5（PID 30620 ❌ 額度撞限）——Review 視角由 W4 全 diff 審查補位。圓桌共識：ItemMaker 最省工先做；HiredMerchant 按架構層拆三刀；**MonsterCarnival 延 P004**（agy 估 5-7 天，依賴 CarnivalParty/Factory/地圖動態刷怪，量級不配）；MTS 維持 stub（使用率極低）；**W1 不可三路平行**（11 個 opcode 全改 `V113ChannelConnectionHandler.cs` 同一個 switch，必衝突）→ 收斂為 Codex 單線循序。

**執行前提**：Codex（ChatGPT 帳號）額度撞限至 7/7 09:43，使用者已同意兌換 1 次 usage limit reset（帳上有 4 次）→ 開工前需使用者在 codex TUI 執行 `/usage` 兌換並回報。W0（Sonnet 文件工）不受影響可先跑。

基線：HEAD `f638132`，645 測試綠。工作區有 6/19 GUI smoke 遺留未 commit 變更（任務歷程/追蹤/日誌/diag script）→ W0 由 Sonnet 檢視後先 checkpoint commit。

### 派工表（全走 ai-cli，PM 不改碼；每派工完成即 commit+push）

| 派工 | 角色 | 內容 | 波次 | 依賴 |
|------|------|------|------|------|
| D0 | Sonnet | 遺留變更 checkpoint commit + **地圖大修正**：~15 條 ❌→✅（附證據路徑）、USE_FISH_MERCHANT→🚫、引入「🟨 已接 dispatch 邏輯待補」標記統一 stub 分類、統計重算 | W0 | 無（可立即跑） |
| D0b | Sonnet | 唯讀研究：Mob 特殊 4 項（FRIENDLY_DAMAGE/MONSTER_BOMB/HYPNOTIZE_DMG/DISPLAY_NODE）對照 Java `MobHandler` 判定去留，結論回填本計畫派工紀錄 | W0 | 無 |
| D1 | Codex | ClientError/ClientFeedback 0x0C↔0x0F 對調修正（含測試影響面）＋ PlayerHandler 雜項 stub 補完整前半：SHOW_EXP_CHAIR / REWARD_ITEM / USE_TREASUER_CHEST / CP_UserThrowGrenade | W1 | 使用者兌換 reset |
| D2 | Codex | PlayerHandler 雜項後半：**ARAN_COMBO（有真實 combo buff：`SkillFactory.getSkill(21000000).getEffect(combo/10).applyComboBuff`）** / CYGNUS_SUMMON / SNOWBALL / LEFT_KNOCK_BACK | W1 | D1 |
| D3 | Codex | COUPON_CODE 兌換邏輯（掛既有 CashShop 服務）＋ GAME_POLL ＋ MAPLETV ＋ CP_BeansUpdate ＋ Mob 特殊 4 項（依 D0b 結論做/不做） | W1 | D2, D0b |
| D4 | Codex | **ItemMaker 完整**：`Etc.wz/ItemMake.img` 配方 parser（用既有 WzDataProvider，Content 層）＋ Maker 技能等級檢查/扣料扣錢/催化劑成功率/產物入包 | W2 | D3 |
| D4b | Codex | **SkillBook catalog 資料萃取**（P002 遺留）：從 Item.wz 萃取 228x/229x 技能書 → JsonSkillBookCatalog 資料檔（與 D4 不同檔案，可平行） | W2 | D3 |
| D5 | Codex | **HiredMerchant Cut1（Core/App/Persistence）**：`IPlayerShop`/`PlayerShop`/`HiredMerchant` 領域模型＋持久化（merchants+items 兩實體）＋ `PlayerShopService`（上架/購買/離線收益/過期），**不接 dispatch**；完成即跑禁區 grep | W3 | D4 |
| D6 | Codex | HiredMerchant Cut2（Adapters）：0x34/0x38/0x3A 接 dispatch＋封包編解碼＋fixture 測試 | W3 | D5 |
| D7 | Codex | HiredMerchant Cut3：`PLAYER_INTERACTION(0x73)` merchant 子指令＋伺服器啟動重載未過期商人＋LiteDB 整合測試 | W3 | D6 |
| D8 | GPT-5.5 | 全 diff Review＋禁區稽核（Core 無 V113 using）；若額度未恢復→PM 判定改派 agy 或延至額度恢復 | W4 | D0-D7 |
| D9 | Sonnet | 測試補齊＋三本帳歸檔（任務追蹤/進度日誌/記憶）＋結果回寫本計畫「執行結果」＋地圖終版更新 | W5 | D8 |

### 明確不做（P003 範圍外）

- **MonsterCarnival 完整移植** → P004 獨立攻堅（先開設計探索：Core.World 動態刷怪基礎設施）
- **MTS 完整拍賣** → 維持 stub；「極簡保管箱取回」列 P004 候選
- **USE_FISH_MERCHANT** → 不移植，地圖改標 🚫（Java 也是 disabled placeholder）
- **反作弊 3 件（0x61/0x62/0x63）** → 維持 stub（低優先）
- **喇叭 channel/server-wide broadcast 基礎設施、Vega Scroll/旅行商人/占卜卡** → 延後

### DoD（PM 完成判定，分類型）

- **每刀共通**：`dotnet build` 綠＋`dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo`（動到 Core/App/Content 加測對應專案）綠＋既有 645 測試零退化
- **每個補完 opcode**：至少一條 fixture 級測試（request byte 解析＋response 封包斷言）；server→client 無 ground truth 者標 `unverified`、禁升黃金測試
- **禁區**：`grep -rn "Maple.Adapters.V113" src/Maple.Core` 為空（D5 後必跑）
- **D5 額外**：持久化 schema 測試；**D7 額外**：啟動重載整合測試
- **D0 文件工**：地圖標記數與程式碼 grep 命中數一致即可，不需 dotnet test

### 驗證

1. 每波結束：目標測試專案綠＋零退化＋禁區 grep
2. P003 收尾後：完整 solution 各測試專案逐一跑（避免全建累積記憶體）
3. 真機 GUI smoke（HiredMerchant 擺攤/ItemMaker 合成）標**里程碑級後補**，不擋 P003 收案——與既有 pending GUI smoke（CS reconnect、seed 增強）合併安排

## 派工紀錄

| 派工 | PID | wait | exit | commit | 驗收 |
|------|-----|------|------|--------|------|
| D0 | 42912 | ✅ | 0 | 34a7f91 + ff87115 | ✅ 地圖新統計 ✅129/🟨29/🟦1/❌0/🚫5，grep 逐項核對吻合；SOLOMON 等 4 項複查有真實邏輯維持 ✅ |
| D0b | 38652 | ✅ | 0 | —（唯讀） | ✅ 結論：**D3 的 Mob 四項只補 MONSTER_BOMB(0xBB)**（機甲技能，selfDestruction 動畫+無獎勵擊殺路徑）；FRIENDLY_DAMAGE/HYPNOTIZE_DMG/DISPLAY_NODE 維持 stub（共同前置＝Shammos 護送+Node 資料模型不存在，延 P004 評估） |
| D1 | 33980 | ✅ | 0 | f9a7c22 | ✅ `CLIENT_FEEDBACK=0x0C` / `CLIENT_ERROR=0x0F` 修正；SHOW_EXP_CHAIR/ThrowGrenade Java parity parser+EnableActions fixture；REWARD_ITEM/TREASURE_CHEST 升級 deterministic reward path（WZ reward catalog TODO）；Adapters 402+1skip、逐專案總測 714+1skip、`dotnet build` 綠 |
| D2 | 41780→46296（CLI 中斷續跑） | ✅ | 0 | 1bc22a1 | ✅ ARAN_COMBO 補 Core runtime combo + Application skill/buff gate + Adapter `GIVE_BUFF(ARAN_COMBO)` candidate；CYGNUS_SUMMON 接 Java NPC script intent；SNOWBALL/LEFT_KNOCK_BACK 做 handler parity，完整 MapleSnowball event 延 P004；新增 8 測試；逐專案總測 722+1skip、`dotnet build` 綠 |
| D3 | 10448 | ✅ | 0 | 9f7a60d | ✅ COUPON_CODE 兌換流程 + GAME_POLL/MAPLETV Java parity + CP_BeansUpdate reset/exit + MONSTER_BOMB 無獎勵擊殺；新增 11 測試；逐專案總測 733+1skip、`dotnet build` 綠 |
| D4 | 38244→34224→47016→39448（撞限+CLI中斷×2 續跑） | ✅ | 0 | 39f739a | ✅ ItemMaker 完整：Content WZ catalog + Application service + v113 handler；WZ 61+726 配方；新增 13 測試；逐專案總測 747+1skip、`dotnet build` 綠 |
| D4b | 42164→13560 | ✅ | 0 | bc021dc | ✅ SkillBook catalog Item.wz 萃取 165 筆，已 push |
| D5 | 50044(撞限)→45832 | ✅ | 0 | f64bce1 | ✅ HiredMerchant Cut1：Core `IPlayerShop`/`PlayerShop`/`HiredMerchant` + Application `PlayerShopService` + LiteDB/Mongo `hired_merchants`/`hired_merchant_items`；新增 14 測試；逐專案總測 761+1skip、`dotnet build` 綠、禁區 grep clean |
| D6 | 53288 | ✅ | 0 | 8c22d6a | ✅ HiredMerchant Cut2：0x34/0x38/0x3A dispatch 接 `PlayerShopService`/repository + 未驗證 PlayerShop/Fredrick/spawn S2C 封包；新增 6 測試；逐專案總測 767+1skip、`dotnet build` 綠、禁區 grep clean；0x73 商人子指令留 Cut3/後續 |
| D7 | 23468(撞限)→本輪續跑 | ✅ | 0 | 待填 | ✅ HiredMerchant Cut3：0x73 merchant create/visit/add/remove/buy/exit/chat/open/close 接線；startup reload service；position 持久化；新增 5 測試；772 passed / 1 skipped，`dotnet build` 綠，禁區 grep clean |

## 執行結果

- **D1 完成（2026-07-06）**：修正 Login opcode 對調 bug；新增 `V113RewardItemHandler`，統一處理 `SHOW_EXP_CHAIR`、`REWARD_ITEM`、`USE_TREASUER_CHEST`、`CP_UserThrowGrenade` 的解析與回應。`REWARD_ITEM` / `USE_TREASUER_CHEST` 依任務允許先走 deterministic reward path，完整 `Etc.wz` reward / Java `StructRewardItem` / `RandomRewards` 權重資料源留 TODO。`CP_UserThrowGrenade` 以本 Java oracle 為準：`PlayerHandler.ThrowGrenade` 是空 handler，故不猜測 S2C 廣播。
- 驗證：`dotnet build --nologo -v quiet` 0 warning / 0 error；`dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` 402 passed / 1 skipped；逐專案測試合計 714 passed / 1 skipped；Core/Application 禁區 grep 無 V113 using。
- **D2 完成（2026-07-06）**：`ARAN_COMBO(0x92)` 對照 Java `PlayerHandler.AranCombo` / `MapleStatEffect.applyComboBuff`，在 Core `Player` 保存 runtime combo count/last time，Application 對齊 Aran job gate、4 秒 reset、30000 cap、10..100 門檻與 `21000000` skill level gate，Adapter 達門檻時送 Java-source candidate `GIVE_BUFF(ARAN_COMBO)`；`CYGNUS_SUMMON(0x91)` 對照 `UserInterfaceHandler.CygnusSummonNPCRequest` 啟 NPC 1202000/1101008；`SNOWBALL(0xCD)` / `LEFT_KNOCK_BACK(0xCE)` 依 PM 接受的深度判定做 handler parity，完整 `MapleSnowball` event 子系統延 P004。
- 驗證：`dotnet build --nologo -v quiet` 0 warning / 0 error；Adapters 406 passed / 1 skipped；Core 104 passed；Application 136 passed；Content 17 passed；Persistence 6 passed；Net 2 passed；Tools.PacketDecoder 22 passed；Tools.HeadlessClient 29 passed；逐專案測試合計 722 passed / 1 skipped；Core/Application 禁區 grep 無 V113 using。
- **D3 完成（2026-07-06）**：`COUPON_CODE(0xE7)` 對照 Java `CashShopOperation.CouponCode` 建 Core coupon model/repository、LiteDB/Mongo persistence 與 CashShopService 兌換流程，支援 Cash/GASH、MaplePoints、item、meso，失敗回 Java cash-shop fail `0xB3`；`GAME_POLL(0xA3)` 對照 Java `PollEnabled=false` 做 parser + `EnableActions` parity；`MAPLETV(0x10A)` 因 Java 無 dispatch/無 broadcaster 做 payload parser + parity；`CP_BeansUpdate(0xE1)` 對照 `BeanGame.BeansUpdate` reset runtime session 並送 exit；`MONSTER_BOMB(0xBB)` 對照 `MobHandler.handleMonsterBomb` 補 selfD WZ stat、無獎勵擊殺路徑與 kill animation 廣播。
- 驗證：`dotnet build --nologo -v quiet` 0 warning / 0 error；Core 104 passed；Application 140 passed；Content 17 passed；Persistence 7 passed；Net 2 passed；Tools.PacketDecoder 22 passed；Tools.HeadlessClient 29 passed；Adapters 412 passed / 1 skipped；逐專案測試合計 733 passed / 1 skipped；Core/Application 禁區 grep 無 V113 using。
- **D4 完成（2026-07-06）**：`ITEM_MAKER(0x6B)` 對照 Java `ItemMakerHandler.ItemMaker` / `ItemMakerFactory`，新增 Core `IItemMakeCatalog` 中性 contract、Content `WzItemMakeCatalog`、Application `ItemMakerService` 與 v113 `V113ItemMakerHandler`。支援 makerType 1 寶石/裝備製作、makerType 3 結晶製作、makerType 4 裝備分解；處理 Maker 技能等級 gate、材料/楓幣驗證與扣除、催化劑 90% 成功率、寶石 stat 加成、裝備 randomize stats、分解產物 RNG。WZ 實讀 `Etc.wz/ItemMake.img`：寶石配方 61 筆、職業裝備配方 726 筆，合計 787 筆。
- 驗證：`dotnet build --nologo -v quiet` 0 warning / 0 error；Core 104 passed；Content 21 passed；Application 145 passed；Persistence 7 passed；Net 2 passed；Tools.PacketDecoder 22 passed；Tools.HeadlessClient 29 passed；Adapters 417 passed / 1 skipped；逐專案測試合計 747 passed / 1 skipped；Core/Application 禁區 `rg` 無 V113 依賴。ItemMaker success S2C fixture 為 Java-source candidate / unverified，真 v113 client GUI smoke 未跑。
- **D5 完成（2026-07-06）**：HiredMerchant Cut1 嚴格停在 Core/Application/Persistence。Core 新增 `Maple.Core.PlayerShops`：`IPlayerShop`、`PlayerShop`、`HiredMerchant`、listing/visitor/blacklist/settlement model、`IHiredMerchantRepository`；Application 新增 `PlayerShopService`：建立/開店、上架扣背包、購買扣錢給物、下架、收益領取、關店暫存、Fredrick-style claim、過期轉 claimable；Persistence 新增 LiteDB/Mongo `hired_merchants` + `hired_merchant_items` provider 並接 DI。對照 Java `HiredMerchantHandler` / `HiredMerchant` / `AbstractPlayerStore.saveItems` / `PlayerInteractionHandler` 商店分支，購買規則涵蓋 bundle、售罄、楓幣上限、黑名單、過期與 `GameConstants.EntrustedStoreTax`。新增 `docs/specs/persistence-model.md` 記錄 schema。
- 驗證：`dotnet build --nologo -v quiet` 0 warning / 0 error；Core 110 passed；Application 150 passed；Persistence 10 passed；Content 21 passed；Net 2 passed；Tools.PacketDecoder 22 passed；Tools.HeadlessClient 29 passed；Adapters 417 passed / 1 skipped；逐專案測試合計 761 passed / 1 skipped；Core/Application 禁區 `rg -n "Maple.Adapters.V113" src/Maple.Core src/Maple.Application` 無命中。本刀未修改 `src/Maple.Adapters.V113`，Cut2 接 opcode dispatch/packet。
- **D6 完成（2026-07-06）**：HiredMerchant Cut2 接 `CP_HiredMerchantRemoteControl(0x34)`、`USE_HIRED_MERCHANT(0x38)`、`MERCH_ITEM_STORE(0x3A)`。新增 `V113HiredMerchantHandler` 與 `V113HiredMerchantPackets`：遠端商人管理、商人 permit/title box、Fredrick pending package list/claim、claim 後背包/楓幣更新、進場/換圖 replay open merchant spawn；S2C 對照 Java `PlayerShopPacket`，全部標 unverified。`V113PlayerInteractionRouter` 仍只支援 trade，Java merchant 進店/上架/購買/移除等 0x73 子指令列後續。
- 驗證：`dotnet build --nologo -v quiet` 0 warning / 0 error；Core 110 passed；Application 150 passed；Persistence 10 passed；Content 21 passed；Net 2 passed；Tools.PacketDecoder 22 passed；Tools.HeadlessClient 29 passed；Adapters 423 passed / 1 skipped；逐專案測試合計 767 passed / 1 skipped；Core/Application 禁區 `rg -n "Maple.Adapters.V113" src/Maple.Core src/Maple.Application` 無命中。
- **D7 完成（2026-07-07，待最終 commit 回填）**：HiredMerchant Cut3 對照 Java `PlayerInteractionHandler.java` merchant 分支，將 `PLAYER_INTERACTION(0x73)` 的 createType 5、visit、add/remove item、buy、exit、chat、open/maintenance off、close merchant 接到 `PlayerShopService`；補 Host startup reload service，啟動時過期 open/maintenance merchant 轉 claimable，未過期 open merchant 由 map-entry replay spawn；補 merchant position 持久化，去除新資料 spawn fallback `(0,0)`。
- 驗證：`dotnet build --nologo -v quiet` 0 warning / 0 error；Core 111 passed；Application 151 passed；Persistence 11 passed；Content 21 passed；Net 2 passed；Tools.PacketDecoder 22 passed；Tools.HeadlessClient 29 passed；Adapters 425 passed / 1 skipped；逐專案測試合計 772 passed / 1 skipped；Core/Application 禁區 `rg -n "Maple.Adapters.V113" src/Maple.Core src/Maple.Application` 無命中。
