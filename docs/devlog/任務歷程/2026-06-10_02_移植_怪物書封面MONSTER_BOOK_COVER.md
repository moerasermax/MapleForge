---
編號: 2026-06-10_02
標題: 移植怪物書封面 MONSTER_BOOK_COVER
類型: 移植
狀態: ✅ 完成
建立: 2026-06-10
更新: 2026-06-10
關聯里程碑: Java→.NET 移植主線 / 玩家體感功能
關聯記憶: v113-pivot-port-from-java, default-collaborate-with-ai-team
關聯commit: 未提交
---

## 目標（執行前先寫死，過程不偷改）

補上怪物書封面變更入口：接收 v113 `MONSTER_BOOK_COVER(0x32)`，更新角色目前封面，並回送 `MONSTERBOOK_CHANGE_COVER(0x4E)`。

完成判準：

1. `Character` 可保存 `MonsterBookCover`，登入 `SET_FIELD` 的 MonsterBookInfo 會寫入該欄位。
2. Channel handler 能解析 cover id，清除封面與設定封面都可落地。
3. 因完整 monster book card collection 尚未移植，本輪只做封面欄位與封包骨架；是否持有卡片的嚴格驗證留給 MonsterBook 系統。
4. 有 Core/Adapters 單元測試與 build/test 驗證。
5. 不修改舊 Java server、真客戶端、WZ 參考或姊妹專案。

## 背景與假設

- 舊 Java `PlayerHandler.ChangeMonsterBookCover`：`bookid == 0 || chr.getMonsterBook().hasCard(bookid)` 時更新 `chr.setMonsterBookCover(bookid)` 並送 `MonsterBookPacket.changeCover(bookid)`。
- MapleForge 尚未有 MonsterBook card collection；此任務不假裝完成卡片蒐集系統，只提供 UI 入口與持久欄位。
- 上一刀已補 `CHAR_INFO_REQUEST` 基礎資訊卡；但資訊卡使用的是 card itemId → mobId 對照，本輪先不誤填，待 ItemInformationProvider/MonsterBook catalog 補齊。

## 計畫步驟

- [x] 1. 建檔定目標。
- [x] 2. 對照舊 Java `MonsterBookPacket.changeCover` 與 `PacketHelper.addMonsterBookInfo`。
- [x] 3. 實作欄位、封包、handler。
- [x] 4. 補測試與文件。
- [x] 5. 執行針對性 build/test。

## 執行歷程（邊做邊追加，附時間）

- 建檔；本輪只做 cover 欄位與封包，不移植完整怪物書卡片收集。
- 對照舊 Java：`MONSTER_BOOK_COVER(0x32)` 讀 `int bookid`，`bookid == 0 || bookid/10000 == 238` 時更新角色並回 `MONSTERBOOK_CHANGE_COVER(0x4E)` + `int cardid`。
- 新增 `Character.MonsterBookCover` 與更新方法；`Player` 提供領域入口。
- `SET_FIELD` 的 MonsterBookInfo 改寫 `Character.MonsterBookCover`；card list 仍為空。
- Channel handler 新增 `MonsterBookCover` case，合法 cover 會持久化角色文件並回 change-cover packet。
- 驗證：Core 40/40、Adapters.V113 145/145、Host build 0 警告 0 錯誤。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

本輪已完成；下一步若沿資訊卡補完整度，應先補 MonsterBook card catalog / card itemId → mobId 對照，否則可切到 Chalkboard 或 UPDATE_CHAR_INFO 這類低耦合 UI 欄位。

## ✅ 結果與結論

完成 `MONSTER_BOOK_COVER(0x32)` 基礎移植。角色可保存怪物書封面，登入封包會帶出 cover itemId；客戶端變更封面時，server 會驗 monster card itemId 範圍、持久化並回 `MONSTERBOOK_CHANGE_COVER(0x4E)`。

未完成：完整 MonsterBook cards、是否持有卡片驗證、card→mobId 對照、角色資訊卡封面 mobId 顯示。

## 產出

- `src/Maple.Core/Characters/Character.cs`
- `src/Maple.Core/World/Player.cs`
- `src/Maple.Adapters.V113/Channel/V113MonsterBookPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `tests/Maple.Core.Tests/Characters/CharacterMonsterBookCoverTests.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelMonsterBookPacketTests.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelQuestPacketTests.cs`
