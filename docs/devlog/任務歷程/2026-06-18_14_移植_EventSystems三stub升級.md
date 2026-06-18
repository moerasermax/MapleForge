---
編號: 2026-06-18_14
標題: EventSystems 三個 MVP stub 升級
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 14:04
更新: 2026-06-18 14:42
關聯里程碑: M6-4
關聯記憶: 
關聯commit: e337dee (Phase A+B aggregate)
---

## 🎯 目標（執行前先寫死，過程不偷改）

> 將 `RPS_GAME(0x80)`、`COCONUT(0xCF)`、`CP_BeansGameAction(0xE0)` 從 adapter-only enableActions MVP stub 升級為可測的 Core 狀態/模型 + v113 handler/packet 實作。
> 完成判準：
> 1. Core 新增 RPS、Coconut、Beans 相關模型或角色欄位，且 Core/Application 不依賴 `Maple.Adapters.V113`。
> 2. 三個 dispatch case 不再只是讀欄位後 `EnableActions`，至少處理任務指定的主要成功/失敗路徑。
> 3. v113 S2C packet builder 以 Java source 為候選 layout，未經 live client 驗證者在文件標明 unverified/candidate。
> 4. 新增 10 個以上 focused tests，涵蓋 RPS 5+、Coconut 3+、Beans 2+。
> 5. `dotnet build src/Maple.Host.Shared/Maple.Host.Shared.csproj --nologo -v quiet` 0 error，且測試專案無回歸。
> 6. 更新任務歷程、進度日誌與必要的 protocol spec。

## 📋 背景與假設

- P2 Batch 2A 已把三個 event opcode 接入 dispatch，但目前只讀最小欄位後送 `EnableActions`。
- Java 行為神諭位於 `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\TestMapleStoryV113_Server\src`：
  - `client/RockPaperScissors.java`、`handling/channel/handler/NPCHandler.java`
  - `handling/channel/handler/PlayersHandler.java`、`server/events/MapleCoconut.java`
  - `handling/channel/handler/BeanGame.java`、`tools/packet/BeansPacket.java`
- MVP 不做 Coconut 完整 lifecycle/start/end timer；只要個別 hit 可判斷與廣播。
- RPS/Beans/Coconut S2C layout 若只由 Java source 對齊、沒有真 client smoke，文件需標為 Java-source candidate。

## 🪜 計畫步驟

- [x] 1. 讀 MapleForge 現有 handler/packet/test/DI pattern 與 Java source map。
- [x] 2. 新增 Core 模型與 focused Core tests。
- [x] 3. 新增或擴充 v113 handler/packet builders，接入 dispatch/DI。
- [x] 4. 新增 adapter focused tests，補 protocol spec。
- [x] 5. 跑 Host.Shared build 與測試專案，修到無回歸。
- [x] 6. 更新任務歷程、進度日誌、任務追蹤狀態與 checkpoint。

## 📜 執行歷程（邊做邊追加，附時間）

- **14:04** 建立任務歷程；下一步讀現有 stub/handler/packet/test pattern 與 Java source。
- **14:32** 完成 Core models、Application Coconut service、V113 packet/handler/dispatch/DI、focused tests 與 protocol/worklog/task-tracker 初步同步。已驗 Core 98/98、Adapters.V113 313+1skip、Host.Shared build 0/0；待 full test suite 與最後收尾。
- **14:42** full suite `dotnet test MapleForge.slnx --nologo -v quiet` 通過：Core 98、Application 134、Content 15、Persistence 6、Adapters 313+1skip、Net 2、PacketDecoder 22、HeadlessClient 29。`git diff --check` 只有 CRLF normalization warnings；Core/Application 無 `using Maple.Adapters`。不做 commit，因工作區同時有非本任務 Phase-A 類 edits/untracked files，且中央 handler 同檔混改。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

✅ 已完成。commit e337dee pushed。

## ✅ 結果與結論

> 達標。三個 EventSystems opcode 已由 read+EnableActions stub 升級為 Core model/state + v113 adapter handler 主路徑：RPS 支援 start/answer/tie/retry/win/lose/timeout/continue/cashout；Coconut 支援 map-scoped MVP hit outcome + score broadcast；Beans 支援 `Character.Beans`、start 扣 bean、shoot 扣 count 與主要 packet ack。
>
> 未達完整活動系統：Coconut lifecycle/team assignment、RPS Java item reward/world notice、Beans 完整中獎節奏與真 v113 client event smoke 仍待後續。S2C layout 只標 Java-source candidate/unverified。

## 🔗 產出

> 新增/修改主要檔案：
> - Core: `MiniGames/RpsSession.cs`、`RpsChoice.cs`、`RpsResult.cs`、`BeansGameSession.cs`、`Events/CoconutEvent.cs`、`World/Player.MiniGames.cs`、`Character.Beans`。
> - Application: `Events/CoconutEventService.cs`。
> - Adapter: `V113EventMiniGamePackets.cs`、`V113EventMiniGameHandler.cs`、`V113ChannelConnectionHandler.cs` dispatch、`V113ChannelOpcodes.cs` send constants。
> - Tests: Core RPS/Coconut/Beans/Character beans tests；Adapters packet + Beans handler tests。
> - Docs: `v113-protocol-spec.md`、`進度日誌.md`、`任務追蹤.md`、本任務歷程與 README index。
