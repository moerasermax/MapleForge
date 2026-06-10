---
編號: 2026-06-09_07
標題: 移植玩家鍵位 CHANGE_KEYMAP
類型: 移植
狀態: ✅ 完成
建立: 2026-06-09
更新: 2026-06-09
關聯里程碑: Java→.NET 移植主線 / M3 in-game 基礎移植
關聯記憶: v113-pivot-port-from-java, task-journal-discipline
關聯commit: 未提交
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植舊 Java `PlayerHandler.ChangeKeymap` 的一般鍵位變更主幹，讓 MapleForge 能保存玩家在真客戶端調整的快捷鍵。

完成判準：

1. Core/Character 有版本無關的 key binding 文件模型與變更方法；不出現 v113 opcode 或 byte layout。
2. Adapters.V113 解析 `CHANGE_KEYMAP(0x7F)` 一般分支：`tick + numChanges + key/type/action...`，並接到 Channel handler。
3. Channel handler 在成功解析後更新角色文件，並在可行時持久化角色。
4. `KEYMAP(0x163)` 送包 encoder 以 Java `MapleKeyLayout.writeData` 為來源，輸出 90 格 `[byte type][int action]`。
5. 補針對性測試；若不跑全測，說明原因。
6. 同步 protocol spec、任務追蹤與進度日誌。

不做範圍：

- `CHANGE_KEYMAP` 短封包的寵物 auto-pot 分支先不做，等 Pet/Quest auto pot 系統。
- `SKILL_MACRO(0x68/0x7A)` 另開下一個切片。
- 真客戶端 UI smoke 本輪不跑，待功能批量完整後再做。

## 背景與假設

- 使用者指示「自動推進」，前一批已完成 GiveFame、FaceExpression、Chair、ItemEffect。
- Keymap 比戰鬥、reactor、portal script 低耦合；會補日常操作體驗，也會為後續技能宏/技能操作鋪路。
- 舊 Java 來源：
  - `handling/channel/handler/PlayerHandler.java`：`ChangeKeymap`
  - `client/MapleKeyLayout.java`：`writeData`
  - `properties/recv.properties`：`CHANGE_KEYMAP = 0x7F`
  - `properties/send.properties`：`KEYMAP = 0x163`

## 計畫步驟

- [x] 1. 建檔定目標。
- [x] 2. 新增 Core key binding 模型與 Player/Character 變更入口。
- [x] 3. 新增 v113 keymap parser/encoder 與 opcode。
- [x] 4. 接 Channel handler 與角色持久化。
- [x] 5. 補測試並跑 targeted/full project checks。
- [x] 6. 同步活文件與任務歷程收尾。

## 執行歷程（邊做邊追加，附時間）

- 讀 Java `ChangeKeymap`、`MapleKeyLayout.writeData` 與目前 MapleForge Character/Channel handler；決定本輪只做一般鍵位變更，短封包 pet auto-pot 與 skill macro 另拆。
- 新增 `KeyBindingRecord`、`Character.Keymap`、`Character.ChangeKeyBinding`、`Player.ChangeKeyBinding`。
- 新增 `V113KeymapPackets`：解析 `CHANGE_KEYMAP` 一般分支、編碼 `KEYMAP(0x163)` 90 格；Channel handler 登入後送 `KEYMAP`，收到變更後更新 Character 並呼叫 `CharacterService.UpdateAsync`。
- 補 `CharacterService.CreateCharacterAsync` 預設 keymap，使用 Java `saveNewCharToDB` 的 `array1/array2/array3` 初始鍵位。
- Targeted 測試：Core Keymap 3/3、Adapters Keymap 4/4、Application Keymap 1/1 通過；先前平行測試造成 `CS2012` 檔案鎖，序列重跑已通過。
- 完整檢查：Core.Tests 33/33、Application.Tests 79/79、Adapters.V113.Tests 130/130 通過；`dotnet build src\Maple.Host.Login\Maple.Host.Login.csproj --no-restore` 0 警告 0 錯誤。
- `git diff --check` 無 whitespace error（僅 LF→CRLF 提示）；`git status` 確認只動 MapleForge，未修改舊 Java/client/WZ 參考資產。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成 `CHANGE_KEYMAP/KEYMAP` 主幹與完整檢查。下一手可接 `SKILL_MACRO(0x68/0x7A)`，或先做 #12 真機 smoke；本功能仍待真機鍵位 UI smoke，pet auto-pot 短分支未補。

## ✅ 結果與結論

完成 `CHANGE_KEYMAP` 一般鍵位變更與 `KEYMAP` 登入送包主幹。新建角色會套用舊 Java 預設鍵位；既有角色若已有 `Character.Keymap`，登入後會收到 90 格 keymap。收到一般鍵位變更後會更新角色文件並立即持久化。

未完成範圍維持原定邊界：pet auto-pot 短分支只放行不改資料；`SKILL_MACRO` 另拆；真機 UI smoke 待後續批量驗證。

## 產出

- `src/Maple.Core/Characters/KeyBindingRecord.cs`
- `src/Maple.Core/Characters/Character.cs`
- `src/Maple.Core/World/Player.cs`
- `src/Maple.Application/Characters/CharacterService.cs`
- `src/Maple.Adapters.V113/Channel/V113KeymapPackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `tests/Maple.Core.Tests/Characters/CharacterKeymapTests.cs`
- `tests/Maple.Core.Tests/World/PlayerKeymapTests.cs`
- `tests/Maple.Application.Tests/Characters/CharacterServiceKeymapTests.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelKeymapPacketTests.cs`
- `docs/specs/v113-protocol-spec.md`
- `docs/devlog/任務追蹤.md`
- `docs/devlog/進度日誌.md`
- `docs/devlog/任務歷程/README.md`
