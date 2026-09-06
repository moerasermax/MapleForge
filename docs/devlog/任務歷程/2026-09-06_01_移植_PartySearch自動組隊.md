---
編號: 2026-09-06_01
標題: PARTY_SEARCH_START/STOP 自動組隊移植
類型: 移植
狀態: ✅ 完成
建立: 2026-09-06 00:00
更新: 2026-09-06 01:30
關聯里程碑: P004（P003 收案後殘餘 🟨 stub 清單之一）
關聯記憶: <空>
關聯commit: 69bd8d5
---

## 🎯 目標（執行前先寫死，過程不偷改）

把 `PARTY_SEARCH_START(0xD9)` / `PARTY_SEARCH_STOP(0xDA)` 從 `EnableActions()` stub 升級為完整邏輯：對照舊 Java `PartyHandler.PartySearchStart/PartySearchStop` + `World.PartySearch`，隊長設定搜尋條件（等級範圍/人數/職業遮罩）後，同地圖現有玩家與後續進場玩家中符合條件者會收到自動組隊邀請。

完成判準：
- Core 有職業分支判斷（`MapleJobFamily`，對照 Java `MapleJob`）。
- Application 有 `PartySearchService`（驗證規則 + 配對邏輯），不依賴 `Adapters.V113`。
- Adapters.V113 接上 0xD9/0xDA dispatch，取代 stub；地圖進場/離場 hook 呼叫配對檢查。
- Core/Application 禁區 grep 無 V113 依賴。
- `dotnet build` 0 warning/0 error；新增單元測試綠；既有測試不退。
- 任務歷程、`docs/design/移植狀態地圖.md`、`docs/devlog/進度日誌.md`、`docs/devlog/任務追蹤.md` 同步。

## 📋 背景與假設

- P003 收案後，`docs/design/移植狀態地圖.md` 把 PARTY_SEARCH_START/STOP 列在殘餘 🟨 清單（P004 候選）。
- Java 來源：`handling/channel/handler/PartyHandler.java`（PartySearchStart/PartySearchStop/PartySearchJob）+ `handling/world/World.java`（PartySearch 內部類，startSearch/stopSearch/checkPartySearch）+ `server/maps/MapleMap.java`（addPlayer 呼叫 checkPartySearch、removePlayer 呼叫 stopSearch）。
- MapleForge 已有乾淨的 Party 基礎設施可重用：`Maple.Application.Parties.PartyService`/`IPartyRegistry`（非 static，DI singleton）、`Maple.Application.OnlinePlayers.IOnlinePlayerRegistry`（線上玩家即時狀態）、`Maple.Application.Maps.IMapSessionRegistry`（同地圖玩家 session 列表）、既有 `V113PartyPackets.PartyInvite(partyId, name, auto)` 封包（`auto=true` 對應 Java `partyInvite(player, true)`）。
- 已知技術債：`PartyMember.MapId/Level/JobId` 在 Adapters 從未透過 `UpdateMember` 刷新（silentPartyUpdate 未接），是 stale snapshot。設計上改用 `IOnlinePlayerRegistry`/`IMapSessionRegistry` 的即時 Character 資料做配對判斷，不依賴 Party 內的 stale 欄位，避開此既有缺口（不在本任務範圍內修，僅繞開）。
- Job 分類：Java `MapleJob` 有大量分支判斷，PartySearchJob.checkJob 只用到其中 24 個判斷式，僅移植這個子集（含唯讀私服自訂職業：聖魂劍士/烈焰巫師/破風使者/暗夜行者/閃雷悍將=Cygnus 五轉、狂狼勇士=Wild Hunter）。
- Java `checkJob` 有一個死 bit（`龍騎士`=0x40 從未被任何分支檢查），以及多處「UI 標籤與實際判斷不符」的怪異之處（如 `格鬥家` bit 實際檢查 `is拳霸`）——這是舊碼本身行為，不修正，原樣保留 bit 值行為。
- `checkPartySearch` 內用 `return` 而非 `continue` 提前結束整個迴圈（找到第一個滿人或第一個相符對象就整個方法返回）——判定為舊碼固有行為，原樣保留（Java 是行為神諭，不修正非要求的怪異之處）。

## 🪜 計畫步驟

- [x] 1. 讀 Java 來源，確認 `PartySearchStart`/`PartySearchStop`/`checkPartySearch`/`MapleJob` 需要的判斷子集
- [x] 2. Core：`MapleJobFamily` 職業分支判斷
- [x] 3. Application：`PartySearchJobFilter`/`PartySearchCriteria`/`IPartySearchRegistry`/`InMemoryPartySearchRegistry`/`PartySearchService`
- [x] 4. Adapters.V113：`V113PartySearchPackets`（解析封包）+ `V113PartySearchHandler`（Start/Stop + 地圖進出場 hook）
- [x] 5. 接上 dispatch（取代 0xD9/0xDA stub）+ 4 個地圖進出場 hook 點（PLAYER_LOGGEDIN 進場、WarpAsync 進/離場、登出離場、CashShop 轉場離場）
- [x] 6. DI 註冊（Host.Shared）
- [x] 7. 單元測試（Core 職業分類、Application 驗證規則+配對、Adapters 封包）
- [x] 8. `dotnet build` + 逐專案測試 + Core/Application 禁區 grep
- [x] 9. 文件同步（任務歷程、移植狀態地圖、進度日誌、任務追蹤）+ commit
- [x] 10.（範圍外意外發現，順手修）`PacketWriter.WriteMapleString`/`PacketReader.ReadMapleString` 硬編 ASCII 逐字元截斷，任何非 Latin-1 文字（含本私服全部繁體中文文案）經封包傳輸必產生亂碼——改用對照 Java `ServerConstants.MapleType.台灣="BIG5-HKSCS"` 的 code page 950（Big5）

## 📜 執行歷程（邊做邊追加，附時間）

- **00:00** 讀完 Java `PartyHandler.java`/`World.java`/`MapleMap.java`/`MapleJob.java`，確認職業判斷子集與 checkPartySearch 的 early-return 語意；確認 MapleForge 既有 Party/OnlinePlayer/MapSession 基礎設施可直接重用，不需新建 registry 型態之外的東西。
- **00:30** 完成 Core `MapleJobFamily`（`is初心者` 逐控制流忠實移植 + 分支判斷）與 Application `PartySearch.cs`（`PartySearchJobFilter` flags enum、`PartySearchCriteria.AllowsJob`、`IPartySearchRegistry`/`InMemoryPartySearchRegistry`、`PartySearchService.TryStartSearch`/`CheckOnMapEntry`，配對邏輯改用 `IOnlinePlayerRegistry` 即時狀態而非 `PartyMember` 內從未刷新的 stale MapId/Level/JobId）。
- **01:00** 完成 Adapters `V113PartySearchHandler`/`V113PartySearchPackets`，接上 0xD9/0xDA dispatch 與四個地圖進出場 hook；`V113BroadcastPackets` 新增 `PopupMessage`（對照 Java `dropMessage(1,...)`/`getPopupMsg`）；DI 註冊完成；`dotnet build` 0 warning/0 error。
- **01:15** 寫單元測試時，Adapters 測試 `HandleStartAsync_RejectsNonLeader_SendsPopupToSelf` 讀回的中文拒絕訊息變成亂碼——追查發現 `PacketWriter.WriteMapleString`/`PacketReader.ReadMapleString` 對每個字元做 `(byte)s[i]` 截斷（等同硬編 Latin-1/ASCII），任何超出 0xFF 的字元必壞；查 Java `MaplePacketLittleEndianWriter` 實際用 `ServerConstants.MAPLE_TYPE.getANSI()`（本服設定為 `台灣` → `"BIG5-HKSCS"`），確認這是既有的、影響全站繁體中文文案（聊天/NPC對話/系統訊息/寵物講話）的潛在亂碼缺陷，非本任務引入。判定影響範圍夠大、修法夠小（且對純 ASCII fixture 是 byte-相容不破壞既有黃金測試），順手修正：新增 `Maple.Core.IO.MapleTextEncoding`（code page 950 + `CodePagesEncodingProvider` 註冊），改 `WriteMapleString`/`WriteFixedAsciiString`/`ReadMapleString` 與 `V113PetPackets.ParsePetChat`（寵物講話文字）改用此編碼；長度前綴同時修正為「編碼後 byte 數」而非「char 數」（多位元組中文字若沿用 char 數會截斷/多讀）。
- **01:25** 全專案測試：Core 126/126、Application 165/178（13 個失敗為既有環境問題——WZ 測試 hardcode 到別台機器的路徑 `D:\WorkSpace\AI_Lab\研究中\...\v113_Client`，與本次改動無關，已用 `git blame`/獨立執行確認非本次引入）、Adapters 430/431（1 skip，含本次新增 12 個 PartySearch 測試全綠，含中文拒絕訊息 round-trip）、Persistence 11/11、Net 2/2、Content 9/21（12 個失敗同樣是 WZ 路徑問題）、Tools.PacketDecoder 22/22、Tools.HeadlessClient 29/29。Core/Application 禁區 grep 無 V113 依賴。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 本任務已完成並可收案。若要延續：P004 剩餘候選為 FRIENDLY_DAMAGE/HYPNOTIZE_DMG/DISPLAY_NODE（Shammos/Node 前置不存在）、ENTER_MTS/TOUCHING_MTS/MTS_TAB、MONSTER_CARNIVAL（估 5-7 天，需獨立立項）、反作弊 3 件（低優先）；另有 RandomRewards 權重 catalog、562x 技能書萃取待補（見 P003 收尾 TODO）。PartySearch 真機 GUI smoke（自動組隊邀請彈窗）尚未做，列 unverified。BIG5 編碼修正後，建議找時間對既有已完成功能（NPC 對話/聊天/系統訊息等）補一輪真機中文顯示 smoke，因為這是舊 bug 首次被修正，過去的「真機驗證通過」記錄多半只測過英文角色名，未必涵蓋中文文案路徑。

## ✅ 結果與結論

- PARTY_SEARCH_START/STOP 從 `EnableActions()` stub 升級為完整移植：隊長設定搜尋條件（等級範圍/人數/職業遮罩）後，同地圖現有玩家與後續進場玩家中符合條件者會收到 Java `partyInvite(auto=true)` 自動組隊邀請；驗證規則、early-return 配對語意（含 Java 原有的「找到第一筆即整個方法返回」怪異行為）與死 bit（0x40「龍騎士」從未被檢查）皆逐一對照 Java 原樣保留，不修正非要求的舊碼行為。
- 意外發現並修正一個影響全站繁體中文文案的既有編碼缺陷（`WriteMapleString`/`ReadMapleString` 硬編 Latin-1 截斷 vs Java 實際用 Big5-HKSCS）。這類「網路層字串編碼」的缺陷很隱蔽——純 ASCII 的既有黃金測試全部通過掩蓋了問題，只有寫新測試時剛好用了中文字面值才會暴露；具遷移可轉移性：**任何用 Java 私服當行為神諭移植時，字串編碼要單獨核對 `ServerConstants` 設定值，不能只信任「golden test 全綠」。**
- 職業分類（`MapleJobFamily`）以 IsBeginner 為代表，控制流複雜到必須逐行忠實搬字轉譯而非重寫語意，才能保證跟 Java oracle bit-for-bit 一致；已用涵蓋各分支的單元測試手算驗證過真值表。

## 🔗 產出

- 新增：`src/Maple.Core/Characters/MapleJobFamily.cs`、`src/Maple.Core/IO/MapleTextEncoding.cs`、`src/Maple.Application/Parties/PartySearch.cs`、`src/Maple.Adapters.V113/Channel/V113PartySearchHandler.cs`
- 修改：`src/Maple.Core/IO/PacketWriter.cs`、`src/Maple.Core/IO/PacketReader.cs`、`src/Maple.Core/Maple.Core.csproj`（新增 `System.Text.Encoding.CodePages`）、`src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`（dispatch + 4 個地圖進出場 hook）、`src/Maple.Adapters.V113/Channel/V113BroadcastPackets.cs`（新增 `PopupMessage`）、`src/Maple.Adapters.V113/Channel/V113PetPackets.cs`（寵物講話文字改用 `MapleTextEncoding`）、`src/Maple.Host.Shared/MapleServerHost.cs`（DI 註冊）
- 新增測試：`tests/Maple.Core.Tests/Characters/MapleJobFamilyTests.cs`、`tests/Maple.Application.Tests/Parties/PartySearchServiceTests.cs`、`tests/Maple.Application.Tests/Parties/PartySearchCriteriaTests.cs`、`tests/Maple.Adapters.V113.Tests/ChannelPartySearchTests.cs`
- 文件：本檔、`docs/design/移植狀態地圖.md`（PARTY_SEARCH×2 🟨→✅，統計更新）、`docs/devlog/進度日誌.md`（新條目）
- commit：`69bd8d5`
