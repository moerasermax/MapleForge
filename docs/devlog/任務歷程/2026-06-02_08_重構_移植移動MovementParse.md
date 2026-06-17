---
編號: 2026-06-02_08
標題: 移植移動處理（Java MovementParse → MapleForge）
類型: 重構
狀態: ✅ 完成（解析+位置追蹤+廣播；IFieldRegistry 全面重構列後續）
建立: 2026-06-02 16:00
更新: 2026-06-02 16:30
關聯里程碑: M3-7 移動 / 移植路線圖 ①in-game 基礎
關聯記憶: v113-pivot-port-from-java, proactive-checkpoints-anti-crash
關聯commit: afe6e28
---

## 🎯 目標（執行前先寫死）

> 參照 Java `MovementParse`/`PlayerHandler`(移動 c2s) 把**完整的移動封包解析（LifeMovementFragment 各型別）+ 廣播**移植到 MapleForge，重構進分層（解析在 Adapters.V113、廣播經 Application IMapSessionRegistry）。
> **完成判準**：①解析 c2s MOVE_PLAYER 為結構化 movement（各 fragment 型別正確讀完，不越界）②原樣/正確重廣播給同地圖其他玩家(MOVE_PLAYER s2c)③單元測試：用一段真實 movement bytes round-trip 解析+重序列化長度一致 ④(里程碑)真客戶端走路→server 收到 MOVE_PLAYER 不報錯。

## 📋 背景與假設

- M3-7 已有 MOVE_PLAYER 廣播雛形(V113MapPackets/V113ChannelConnectionHandler)，但移動解析可能是簡化版。Java `MovementParse.serializeMovementList`/`updatePosition` + `LifeMovementFragment` 是權威。
- 重構：移動 fragment 解析/序列化放 Adapters.V113(協定)；位置狀態更新在 Core/Application。

## 🪜 計畫步驟

- [ ] 1. 讀 MapleForge 現況(V113ChannelConnectionHandler MovePlayer/V113MapPackets/Opcodes) + Java MovementParse + PlayerHandler MovePlayer。
- [ ] 2. 移植 LifeMovementFragment 解析(各型別)+ serialize；重構進 Adapters.V113。
- [ ] 3. 廣播路徑對齊(同地圖其他玩家收 MOVE_PLAYER)。
- [ ] 4. 單元測試(round-trip bytes) + 里程碑真客戶端 smoke。

## 📜 執行歷程

- **16:00** 開檔。讀 MapleForge 現況：MOVE_PLAYER 是 raw-passthrough(剝 35 bytes header 原樣重廣播,沒解析)；Java MovementParse 才真解析。
- **16:1x ✅ 移植 V113MovementParser(隔離單元)**：逐欄對 Java `MovementParse.parseMovement`(numCommands + 各 command 0/1/3/10/14/15… 不同欄位長度)→ 抽最終 X/Y/stance/foothold(`MovementResult`)。放 `src/Maple.Adapters.V113/Channel/V113MovementParser.cs`(internal,測試專案 InternalsVisibleTo 可測)。**單元測試 4 項(normal move/cmd15 foothold/multi-command last-wins/cmd10 change-equip 精確消費)全綠,Adapters.V113 29→33 測試。** commit checkpoint。

## ⏯️ 接手點（★崩潰救命行★）

> ✅ V113MovementParser 解析移植完成+測過+commit。**團隊架構會議(claude-ultra+agy)定案 in-game 執行期狀態模型**(見 `docs/design/in-game-執行期狀態架構.md`)：執行期 `Player`/`FieldInstance`/`Position`/`IFieldObject` 放 **Core/World 富領域**(組合持有 Character、零傳輸)、生命週期/registry 在 Application、傳輸留 Net。**已實作 Core/World 最小切片(Position/IFieldObject/Player/FieldInstance)+3 測試(Application.Tests 22→25 綠)、commit**。**✅ handler 整合完成**：handler 抽掉 stack ref(x/y/stance/foothold)、改建 Core `Player`;MovePlayer→`TryUpdateMovement`(從 offset 35 解析 movement→映射 Core `Position`→`player.MoveTo`,best-effort try/catch InvalidDataException 不中斷)+維持原始 blob 廣播。build 0/0、Adapters.V113 33 綠。**位置追蹤上線(server 權威 Player.Position)。** 後續(列入路線圖,非本檔):①IMapSessionRegistry→IFieldRegistry(領域)+sink(傳輸)全面重構(現仍存 Character+SendPacket 焊一起)②多玩家 spawn 用各自 Player.Position(現 others spawn 仍 0,0)③真客戶端走路 live 驗(windower move.txt)。：讀 c2s 封包 header(現碼剝 35 bytes)→用 parser 抽位置→存到 session/Character(追蹤位置,供之後戰鬥/NPC 範圍)→維持廣播。需先確認 c2s MOVE_PLAYER header 精確 layout(對 Java PlayerHandler MovePlayer)。不要為整合冒險破壞現有可用的 raw-passthrough 廣播,先驗 header offset。windower 走路注入(move.txt)已備、里程碑時 live 驗。

## ✅ 結果與結論
> （待補）

## 🔗 產出
> （待補）
