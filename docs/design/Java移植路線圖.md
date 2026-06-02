# Java → MapleForge 完整移植路線圖（gap 分析）

> 2026-06-02 轉向（見記憶 `v113-pivot-port-from-java`）：參照舊 Java `TestMapleStoryV113_Server`(OdinMS 系) 完整移植到 MapleForge 乾淨架構、邊做邊重構。
> 本文＝Java 功能面 vs MapleForge 現況的 gap 與建議移植順序。**順序待使用者拍板。**

## 量化現況

- Java c2s opcode：**44 種**；channel handler：**24 個**（見下）。
- MapleForge channel handler 目前只處理 **3 個**（PlayerLoggedIn / MovePlayer / Pong）。
- 登入鏈（login）+ 角色（建/選/進地圖 SetField）+ 真客戶端進圖續留 = **已完成**。in-game 幾乎全空。

## Gap 表（Java handler → MapleForge 狀態 → 移植要點）

| 子系統 | Java 來源 | MapleForge 狀態 | 移植要點 / 依賴 |
|---|---|---|---|
| 登入/帳號/角色 | login/handler (CharLogin, AutoRegister) | ✅ 完成 | M1/M2;PIN 邊角可補 |
| 進場 SetField | InterServerHandler + PacketHelper.addCharacterInfo | ✅ 完成 | task 04/06 已修 EOF |
| **移動** | MovementParse, PlayerHandler | 🟡 partial | server MOVE_PLAYER 廣播(M3-7)在;需完整移植 MovementParse 序列化 + live 驗(windower 走路注入已備) |
| **聊天** | ChatHandler | 🔴 缺 | 一般聊天/指令;簡單、體感高 |
| 地圖物件/玩家 | PlayersHandler, NPCHandler(spawn), field | 🟡 partial | spawn player(M3-5/7)在;NPC/portal/物件 spawn、進場現有物件同步 缺 |
| **NPC 對話/商店** | NPCHandler + scripting(NPCConversationManager) | 🔴 缺 | 需 Jint 腳本引擎(M5);talk/shop |
| **背包/道具** | InventoryHandler, ItemMaker | 🔴 缺 | 移動/使用/丟棄/裝備/合成;依角色裝備模型(已有) |
| **戰鬥** | MobHandler, AttackInfo, AttackType, DamageParse, PlayerHandler(attack) | 🔴 缺 (M4) | 怪生成/AI/攻擊封包/傷害公式/掉寶/撿物;依地圖+怪物 WZ |
| 技能/Buff/狀態 | PlayerHandler, StatsHandling, SummonHandler | 🔴 缺 | 加點/技能施放/buff/召喚獸 |
| 寵物 | PetHandler | 🔴 缺 | |
| 玩家互動 | PlayerInteractionHandler | 🔴 缺 | 交易/商店/小遊戲 |
| 社交 | Party, Guild, Alliance, BuddyList, Family, BBS, Messenger | 🔴 缺 (M6) | 跨頻道/世界伺服器(handling/world) |
| 商城/MTS | CashShopOperation, MTSOperation | 🔴 缺 (M6) | |
| 進階/活動 | Duey, HiredMerchant, MonsterCarnival, BeanGame, UserInterface | 🔴 缺 | 低優先 |

## 建議移植順序（價值＋依賴；呼應使用者原始「行走/對話/攻擊」）

1. **in-game 基礎（走路＋聊天＋地圖物件同步＋keep-alive）** — 角色真正能動、能被看到、不掉線。移植 MovementParse / ChatHandler / 進場物件同步。
2. **NPC 對話 + 背包**（talk + 道具管理）— 移植 Jint 腳本引擎 + NPCConversationManager + InventoryHandler。
3. **戰鬥 M4**（attack）— MobHandler/AttackInfo/DamageParse/掉寶/撿物。
4. 技能/Buff/Pet/Summon。
5. 社交（party/guild/buddy/messenger，含 world server）。
6. 商城/MTS、進階活動。

## 移植方法（每子系統）

讀 Java handler+封包(tools/packet) → 重構進分層（協定/opcode→`Adapters.V113`、領域→`Core`、用例→`Application`，保留零-static）→ 單元測試(Java 預言機/黃金向量) → 里程碑真客戶端 smoke。**非 1:1 照抄 OdinMS。**

> 每子系統開工前依任務歷程紀律建檔定 DoD。
