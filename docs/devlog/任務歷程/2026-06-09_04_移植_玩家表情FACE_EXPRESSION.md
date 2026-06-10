---
編號: 2026-06-09_04
標題: 移植玩家表情 FACE_EXPRESSION
類型: 移植
狀態: ✅ 完成
建立: 2026-06-09
更新: 2026-06-09
關聯里程碑: Java→.NET 移植主線 / 玩家體感小功能
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植舊 Java `PlayerHandler.ChangeEmotion` 對應的玩家表情功能，讓 v113 client 送出 `FACE_EXPRESSION(0x2C)` 後，MapleForge 能依 Java 封包格式廣播 `FACIAL_EXPRESSION(0xB9)` 給同地圖其他玩家。

完成判準：

1. `V113ChannelRecvOp` 新增 `FaceExpression = 0x2C`，handler 能解析 `int emote`。
2. `V113MapPackets` 或同層封包類新增 `FACIAL_EXPRESSION(0xB9)` 序列化，格式對齊 Java `MaplePacketCreator.facialExpression`：`[opcode][int charId][int expression]`。
3. `V113ChannelConnectionHandler` 接上 case；行為保持低耦合，不新增 Core/Application v113 byte layout。
4. 有針對性封包測試與 handler 相關可編譯檢查。
5. 不修改舊 Java server、真客戶端、WZ 參考或姊妹專案。

## 📋 背景與假設

- 使用者要求繼續移植；上一輪 `GiveFame` 已完成但尚未提交，本任務需避開其變更、不回退任何 dirty worktree。
- 本輪選擇 `FACE_EXPRESSION`，因為它比 Keymap/SkillMacro 少持久化模型，比 Chair 少角色座椅狀態，比 CharacterInfo 少大型封包風險。
- 舊 Java 對 `emote > 7` 會檢查現金表情道具持有；MapleForge 目前現金道具/道具使用仍不完整，本輪先移植基礎廣播語義，將現金表情持有檢查列為後續完整道具使用依賴。

## 🪜 計畫步驟

- [x] 1. 建檔定目標。
- [x] 2. 對照 Java opcode 與 packet creator。
- [x] 3. 實作 opcode、packet、handler case。
- [x] 4. 新增/更新測試。
- [x] 5. 跑針對性驗證。
- [x] 6. 回填結果、接手點與進度日誌。

## 📜 執行歷程（邊做邊追加，附時間）

- 建檔；Java 來源已定位：`recv.properties FACE_EXPRESSION=0x2C`、`send.properties FACIAL_EXPRESSION=0xB9`、`MapleServerHandler` 轉呼叫 `PlayerHandler.ChangeEmotion(slea.readInt(), chr)`、`MaplePacketCreator.facialExpression` 寫 `[0xB9][charId][expression]`。
- 實作完成：`V113ChannelRecvOp.FaceExpression`、`V113ChannelSendOp.FacialExpression`、`V113MapPackets.FacialExpression`、Channel handler case + `HandleFaceExpressionAsync`。
- 測試完成：新增 `ChannelFaceExpressionPacketTests`，鎖住 c2s/s2c opcode 與 Java layout。
- 驗證完成：`dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj --filter FaceExpression --no-restore` 通過 2/2；完整 Adapters.V113 測試 119/119；`dotnet build src\Maple.Host.Login\Maple.Host.Login.csproj --no-restore` 0 警告 0 錯誤。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成 `FACE_EXPRESSION` 基礎廣播移植。下一手可繼續低耦合體感功能（椅子/道具效果/Keymap/SkillMacro），或先做 #12 真機 smoke；本功能仍待真機 UI 確認表情動畫顯示。

## ✅ 結果與結論

- **FACE_EXPRESSION 基礎行為已移植**：client 送 `FACE_EXPRESSION(0x2C)` + `int emote` 後，server 依 Java `FACIAL_EXPRESSION(0xB9)` layout 廣播 `[charId][expression]` 給同地圖其他玩家。
- **分層維持乾淨**：Core/Application 未新增 v113 opcode 或 byte layout；本輪只改 `Adapters.V113`。
- **限制**：舊 Java 對 `emote > 7` 會檢查現金表情道具持有；MapleForge 目前道具使用/現金道具完整系統未完成，因此本輪先落地基礎廣播，完整持有檢查留待道具使用切片。
- **測試結果**：
  - `dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj --filter FaceExpression --no-restore`：通過 2/2。
  - `dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj --no-restore`：通過 119/119。
  - `dotnet build src\Maple.Host.Login\Maple.Host.Login.csproj --no-restore`：0 warning / 0 error。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113MapPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelFaceExpressionPacketTests.cs`
- `docs/specs/v113-protocol-spec.md`
