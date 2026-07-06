# 任務歷程（Task Journal）

> **一個任務一個檔。執行前先把目標寫死，執行中保持一條「崩潰救命行」。**
> 這是專為**非預期重開機 / session 中斷**設計的韌性層——任何時刻斷線，這裡都有一份活的「我在做什麼、下一步是什麼」。

## 為什麼需要這層

`進度日誌.md` 是 session 事後敘事、`任務追蹤.md` 是里程碑狀態真相，兩者都是**粗顆粒 / 事後**。
但崩潰往往發生在**一個任務做到一半**時（本專案已重演 ≥3 次死機）。任務歷程補的就是這個缺口：

| 檔案 | 顆粒 | 時機 |
|---|---|---|
| `任務追蹤.md` | 里程碑 M0–M6 | DoD 狀態真相 |
| `進度日誌.md` | 每個 session | 事後敘事 |
| **本資料夾** | **單一任務** | **執行前定標、執行中即時** |

## 🔑 鐵則（流程）

1. **先定目標再執行**：開任何任務，先 `cp _範本.md` 建新檔、填好 `🎯 目標`（含可驗收的完成判準）、把狀態切 `🚧 執行中`——**目標沒寫死不准動手**。
2. **接手點永遠最新**：每做一步，更新 `⏯️ 接手點` 那一段。它的標準是「如果我下一秒斷線，接手的人照這段就能無縫接續」。
3. **目標不偷改**：執行中發現目標需要變，明確在歷程記一筆「目標調整：原因」，不要默默改掉 `🎯`。
4. **收尾**：達標把狀態切 `✅ 完成`、填 `✅ 結果與結論`；中斷切 `⏸️ 中斷`（接手點寫清楚停在哪）；放棄切 `🗄️ 封存`。
5. **回填三本帳**：完成時順手更新 `任務追蹤.md` 狀態、在 `進度日誌.md` 補一條敘事、必要時更新記憶。

## 檔名格式

```
YYYY-MM-DD_序號_類型_簡述.md
```
- 例：`2026-06-02_01_驗證_AuthSuccess彈回修補.md`
- **日期前綴** → 時序排序；**序號** → 同日多任務（01, 02…）；**類型** → 一眼看出性質。
- 類型取值：`探查` / `研究` / `分析` / `修補` / `重構` / `驗證`。
- 用 `_` 分隔、中文簡述（D:\ 路徑安全）；**勿用** `/` 或把 `/數字` 寫進路徑。

## 狀態圖示

`🎯 規劃` → `🚧 執行中` → `✅ 完成` ／ `⏸️ 中斷`（可續） ／ `🗄️ 封存`（放棄）

## 索引

| 編號 | 類型 | 標題 | 狀態 |
|---|---|---|---|
| 2026-07-07_03 | 修補 | P003 D8b 終審缺陷修補 | ✅ 完成 |
| 2026-07-07_02 | 修補 | P003 D7b 外部審查缺陷修補 | ✅ 完成 |
| 2026-07-07_01 | 移植 | HiredMerchant Cut3 收尾刀 | ✅ 完成 |
| 2026-07-06_03 | 移植 | HiredMerchant Cut2 Adapters | ✅ 完成 |
| 2026-07-06_02 | 移植 | HiredMerchant Cut1 Core App Persistence | ✅ 完成 |
| 2026-07-06_02 | 移植 | SkillBook catalog 資料萃取（D4b） | ✅ 完成 |
| 2026-07-06_01 | 移植 | P003 移植清尾 + MVP stub 補完整（D0-D9 總覽） | ✅ 完成（775 passed / 1 skipped，agy 複核可收案） |
| 2026-06-18_26 | 修補 | P002 三輪稽核最終修補 | ✅ 完成 |
| 2026-06-18_25 | 驗證 | P002 三輪稽核 | ✅ 完成 |
| 2026-06-18_24 | 修補 | P002 二輪稽核殘留修補 | ✅ 完成 |
| 2026-06-18_23 | 修補 | P002 稽核缺陷修補 | ✅ 完成 |
| 2026-06-18_22 | 驗證 | P002 鐵律合規稽核 | ✅ 完成 |
| 2026-06-18_21 | 驗證 | D9 P002 技術債批次 code review | ✅ 完成（禁區 clean；Adapters/Content/Core/Host build 綠；2 blocker + 1 warning） |
| 2026-06-18_20 | 移植 | USE_CASH_ITEM Batch C 廣播現金道具 | ✅ 完成（Adapters.V113 391+1skip；map-level broadcast MVP） |
| 2026-06-18_19 | 移植 | USE_CASH_ITEM Batch D 傳送石現金道具 | ✅ 完成 |
| 2026-06-18_18 | 移植 | USE_CASH_ITEM Batch B 現金道具邏輯 | ✅ 完成 |
| 2026-06-18_18 | 移植 | FieldLimitType enum | ✅ 完成 |
| 2026-06-18_17 | 移植 | SERVERMESSAGE broadcast packet infrastructure | ✅ 完成（Adapters.V113 340+1skip；S2C Java-source candidate） |
| 2026-06-18_16 | 移植 | USE_CASH_ITEM Batch A 現金道具路由 | ✅ 完成 |
| 2026-06-18_16 | 移植 | USE_SKILL_BOOK 技能書 catalog 全鏈 | ✅ 完成 |
| 2026-06-18_15 | 驗證 | 鐵律與最高流程稽核 | ✅ 完成 |
| 2026-06-18_14 | 重構 | P002 技術債清償：ENTER_CASH_SHOP / USE_SKILL_BOOK / USE_CASH_ITEM 完整路由 | ✅ 完成（645 綠；cb66272+） |
| 2026-06-18_12 | 移植 | P2 Migration Wave 4 heavy opcode MVP stubs | ✅ 完成（Host.Shared build；Adapters.V113 299+1skip） |
| 2026-06-18_14 | 移植 | EventSystems 三個 MVP stub 升級 | ✅ 完成（Host.Shared build；full suite 綠，S2C candidate） |
| 2026-06-18_13 | 移植 | P2 MVP stub 補完整實作 + 技術債清償 | ✅ 完成 |
| 2026-06-18_11 | 移植 | P2 Migration Wave 3 complex opcode MVP stubs | ✅ 完成（Host.Shared build；Adapters.V113 299+1skip） |
| 2026-06-18_10 | 移植 | P2 Batch 2B misc medium 5 opcode stubs | ✅ 完成（Host.Shared build；Adapters.V113 299+1skip） |
| 2026-06-18_09 | 移植 | P2 Batch 2C CashShop + AntiMacro simple opcode stubs | ✅ 完成（Host.Shared build；Adapters.V113 299+1skip） |
| 2026-06-18_08 | 移植 | P2 Batch 2A Event Systems MVP stubs | ✅ 完成（Host.Shared build；Adapters.V113 299+1skip） |
| 2026-06-18_07 | 移植 | P2 Batch 1B 簡易 handler 8 opcode | ✅ 完成（Host.Shared build；Adapters.V113 299+1skip） |
| 2026-06-18_06 | 移植 | P2 Batch 1A no-op/stub/log opcode handlers | ✅ 完成（Host.Shared build；Adapters 299+1skip） |
| 2026-06-18_04 | 流程 | 新協作流程落地（七條鐵則+Hook+自動推進） | ✅ 完成（GPT-5.5 稽核通過；進度日誌補檔） |
| 2026-06-18_04 | 移植 | Family 家族系統 | ✅ 完成（Core/Application/Adapters 單元；dispatch/live 待後續） |
| 2026-06-18_03 | 移植 | USE_DOOR 傳送門 | ✅ 完成（Core/Application/Adapters 單元；dispatch/live 待後續） |
| 2026-06-18_02 | 移植 | NOTE_ACTION 留言系統 | ✅ 完成（Core/Application/Persistence/Adapters 單元；dispatch/DI 待後續） |
| 2026-06-17_05 | 移植 | CHANGE_CHANNEL 單進程 MVP | ✅ 完成（單元/Host build；真機 GUI smoke 待做） |
| 2026-06-17_04 | 移植 | USE_ITEM 消耗補藥 | ✅ 完成（單元/建置；真機補藥 UI smoke 待做） |
| 2026-06-17_03 | 移植 | 升級卷軸 USE_UPGRADE_SCROLL | ✅ 完成（單元/adapter；真機 smoke 待做） |
| 2026-06-17_01 | 移植 | 整合 port/item-use 分支到 master（batch-5 第 8 路收尾） | ✅ 完成（commit/push；worktree 清理） |
| 2026-06-12_01 | 移植 | 道具使用四缺口 UseMountFood/UseSummonBag/UseReturnScroll/UseCatchItem | ✅ 完成（單元；中央整合於 2026-06-17 完成） |
| 2026-06-12_01 | 重構 | batch-5 中央整合：反應堆/交易/宅配/公會板/戒指跟隨/NPC物品服務/增益道具 | ✅ 完成（7 系統整合 + 加固；item-use 後續已收官） |
| 2026-06-10_13 | 驗證 | 存檔與流程鐵律稽核 | ✅ 完成（本地 checkpoint commit） |
| 2026-06-10_12 | 移植 | 背包排序 ITEM_SORT | ✅ 完成（單元；真機背包 UI smoke 待做） |
| 2026-06-10_11 | 移植 | 背包聚集 ITEM_GATHER | ✅ 完成（單元；真機背包 UI smoke 待做） |
| 2026-06-10_10 | 移植 | 同圖內部傳點 USE_INNER_PORTAL | ✅ 完成（單元；真機 portal smoke 待做） |
| 2026-06-10_09 | 移植 | 丟楓幣 MESO_DROP | ✅ 完成（單元；真機 drop smoke 待做） |
| 2026-06-10_08 | 移植 | 玩家受傷 TAKE_DAMAGE | ✅ 完成（單元；真機受傷 smoke 待做） |
| 2026-06-10_07 | 移植 | 傳送石新增刪除 TROCK_ADD_MAP | ✅ 完成（單元；真機 UI smoke 待做） |
| 2026-06-10_06 | 移植 | 傳送石地圖清單 SET_FIELD AddRocksInfo | ✅ 完成（單元；真機 UI smoke 待做） |
| 2026-06-10_05 | 移植 | 寵物自動補藥 KEYMAP short branch | ✅ 完成（單元；寵物實際施放待後續） |
| 2026-06-10_04 | 移植 | 黑板關閉 CLOSE_CHALKBOARD | ✅ 完成（單元；開黑板待 USE_CASH_ITEM） |
| 2026-06-10_03 | 移植 | 角色資訊更新 UPDATE_CHAR_INFO | ✅ 完成（單元；真機 UI smoke 待做） |
| 2026-06-10_02 | 移植 | 怪物書封面 MONSTER_BOOK_COVER | ✅ 完成（單元/建置；真機 UI smoke 待做） |
| 2026-06-10_01 | 移植 | 角色資訊 CHAR_INFO_REQUEST | ✅ 完成（單元/建置；真機 UI smoke 待做） |
| 2026-06-09_08 | 移植 | 玩家技能宏 SKILL_MACRO | ✅ 完成（單元/建置；真機 UI smoke 待做） |
| 2026-06-09_07 | 移植 | 玩家鍵位 CHANGE_KEYMAP / KEYMAP | ✅ 完成（單元/建置；真機 UI smoke 待做） |
| 2026-06-09_06 | 移植 | 玩家道具效果 USE_ITEMEFFECT | ✅ 完成（單元/建置；真機 UI smoke 待做） |
| 2026-06-09_05 | 移植 | 玩家椅子 USE_CHAIR / CANCEL_CHAIR | ✅ 完成（單元/建置；真機 UI smoke 待做） |
| 2026-06-09_04 | 移植 | 玩家表情 FACE_EXPRESSION | ✅ 完成（單元/建置；真機 UI smoke 待做） |
| 2026-06-09_03 | 移植 | 玩家體感小功能（Give Fame）與 .NET+Mongo 效能分析 | ✅ 完成 |
| 2026-06-09_02 | 修補 | 同步 6/6~6/9 進度文件狀態 | ✅ 完成 |
| 2026-06-09_01 | 分析 | 姊妹專案方法論融合與新 session 鐵律載入 | ✅ 完成 |
| 2026-06-06_08 | 移植 | 腳走傳送點換圖 CHANGE_MAP(0x1E) | 🚧 程式碼+單元✅，待真機 GUI 驗 |
| 2026-06-06_07 | 修補 | P0 送包加密共用 buffer + per-connection queue + session token | ✅ 完成 |
| 2026-06-06_06 | 分析 | 效能稽核（三方報告 + P0 修補清單） | ✅ 完成 |
| 2026-06-06_05 | 修補 | WZ parser 長字串修復 + 統一線上玩家 registry | ✅ 完成 |
| 2026-06-06_04 | 重構 | batch-2~4 平行移植（Party/Buddy/Quest/Stats/Skills/Drops/Guild/CashShop/Chat） | ✅ 完成（headless+單元；真機 smoke 待做） |
| 2026-06-06_03 | 重構 | 5 Codex 平行移植：穿脫/商店/戰鬥/倉庫/Mongo | ✅ 完成（headless+單元；真機 smoke 待做） |
| 2026-06-06_02 | 整理 | V113 專案檔案分類與腳本搬移 | ✅ 完成 |
| 2026-06-06_01 | 研究 | 姊妹專案開發取經 | ✅ 完成 |
| 2026-06-02_14 | 重構 | 背包 MVP-1 穿脫裝規劃 | ⏸️ 暫停（後由 6/6 批次移植接續整合，仍待 GUI smoke） |
| 2026-06-02_13 | 重構 | 背包/道具系統 MVP-0 | ✅ 完成（headless；GUI smoke 待批量驗） |
| 2026-06-02_12 | 重構 | NPC 對話腳本引擎（Jint + cm API + NPC_TALK） | ✅ 完成（headless；GUI smoke 待批量驗） |
| 2026-06-02_11 | 驗證 | 真客戶端進圖 NPC 顯示 smoke | ✅ 完成 |
| 2026-06-02_10 | 重構 | 地圖物件同步 NPC | ✅ 完成 |
| 2026-06-02_09 | 重構 | 移植一般地圖聊天 ChatHandler | ✅ 完成 |
| 2026-06-02_08 | 重構 | 移植 MovementParse → V113MovementParser | ✅ 完成 |
| 2026-06-02_07 | 重構 | 參照 Java 完整移植路線圖（gap 分析） | 🎯 規劃完成（順序待拍板/陸續移植） |
| 2026-06-02_06 | 探查 | 進圖後續留→行走→對話→攻擊 | 🚧 續留已解；行走/對話/攻擊拆後續任務 |
| 2026-06-02_05 | 修補 | windower 注入疊字硬化（委派 Codex） | ✅ 完成 |
| 2026-06-02_04 | 驗證 | 真客戶端全自動進地圖（login→world→channel→char→map） | ✅ 完成（server 確認進圖，可重現；render截圖列後續） |
| 2026-06-02_03 | 修補 | 客戶端斷線優雅處理（消除 fail+stacktrace 噪音） | ✅ 完成 |
| 2026-06-02_02 | 驗證 | 世界選擇 → 選角 → 進地圖（blocker#3） | ✅ 完成（協定層；視覺層另案） |
| 2026-06-02_01 | 修補 | AuthSuccess 彈回修補（→ 前進世界選擇「雪吉拉」） | ✅ 完成 |

> 新增任務檔後，在此表補一行。
