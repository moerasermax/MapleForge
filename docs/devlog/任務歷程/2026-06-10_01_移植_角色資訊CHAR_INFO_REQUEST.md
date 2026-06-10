---
編號: 2026-06-10_01
標題: 移植角色資訊 CHAR_INFO_REQUEST
類型: 移植
狀態: ✅ 完成
建立: 2026-06-10
更新: 2026-06-10
關聯里程碑: Java→.NET 移植主線 / 玩家體感功能
關聯記憶: v113-pivot-port-from-java, default-collaborate-with-ai-team
關聯commit: 未提交
---

## 目標（執行前先寫死，過程不偷改）

補上玩家右鍵查看角色資訊入口：接收 v113 `CHAR_INFO_REQUEST(0x5B)`，回送 `CHAR_INFO(0x36)` 的基礎角色資訊封包。

完成判準：

1. Channel handler 能解析目標 object/character id，優先回應同地圖線上玩家。
2. v113 byte layout 留在 `Maple.Adapters.V113`，Core/Application 不放 opcode。
3. 初版只使用目前已存在或已移植的角色資料；Monster Book、婚姻、寵物、家族等尚未移植資訊以 Java 相容的空值/預設值保留欄位。
4. 有 packet layout 單元測試與至少一次 build/test 驗證。
5. 不修改舊 Java server、真客戶端、WZ 參考或姊妹專案。

## 背景與假設

- 使用者指示繼續自動推進移植，真機整體測試等依賴鏈更完整後再跑。
- 前一批已完成 Keymap/SkillMacro 等低耦合玩家體感功能，本輪延續玩家互動入口。
- 舊 Java `CHAR_INFO_REQUEST = 0x5B`，`CHAR_INFO = 0x36`；完整資訊卡依賴 MonsterBook/Family/Pet/Marriage 等未移植系統，本輪先落地基礎可顯示資料。

## 計畫步驟

- [x] 1. 建檔定目標。
- [x] 2. 對照舊 Java `PlayerHandler.CharInfoRequest` 與 `MaplePacketCreator.charInfo`。
- [x] 3. 實作 v113 封包與 channel 接線。
- [x] 4. 補單元測試與文件。
- [x] 5. 執行針對性 build/test。

## 執行歷程（邊做邊追加，附時間）

- 建檔；本輪只處理基礎角色資訊卡，不把 MonsterBook/Pet/Family 一次塞進來。
- 對照舊 Java：`CHAR_INFO_REQUEST(0x5B)` 讀 `int targetId`，先送 `enableActions()`，再對同地圖目標送 `CHAR_INFO(0x36)`。
- 新增 `V113CharacterInfoPackets`，封包先填等級/職業/人氣/公會名稱；Pet/Mount/Wishlist/MonsterBook/Medal 等尚未移植段保留空值/預設值。
- Channel handler 新增 `CharInfoRequest` case，同地圖查找目標角色，避免跨圖或不存在目標回資訊。
- 驗證：Adapters.V113 測試 138/138 通過；Host build 0 警告 0 錯誤。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

本輪已完成；下一刀可從 MonsterBook cover / medal list / pet char-info 段擇一補完整度，或轉向更高價值的 Mob MOVE_LIFE / portal script。

## ✅ 結果與結論

完成 `CHAR_INFO_REQUEST(0x5B)` 基礎移植。客戶端請求同地圖角色資訊時，MapleForge 會依 Java 流程先送 `EnableActions`，再送 `CHAR_INFO(0x36)` 基礎資訊卡。完整寵物、怪物書、座騎、勳章、婚姻、家族等資料仍待對應系統移植後補欄位。

## 產出

- `src/Maple.Adapters.V113/Channel/V113CharacterInfoPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelCharacterInfoPacketTests.cs`
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/任務追蹤.md`
- `docs/devlog/進度日誌.md`
