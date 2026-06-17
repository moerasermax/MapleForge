---
編號: 2026-06-17_05
標題: 移植 CHANGE_CHANNEL
類型: 移植
狀態: ✅ 完成
建立: 2026-06-17 14:16
更新: 2026-06-17 14:23
關聯里程碑: M3 in-game / protocol
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植 v113 `CHANGE_CHANNEL(0x1F)` 的最小單進程 MVP：Channel handler 收到目標頻道 byte 後保存角色，送出 Java 對齊的 `CHANGE_CHANNEL(0x08)` 成功封包，讓 client 斷線並重連同一個 channel endpoint。完成判準：opcode 常數、parser/serializer、handler dispatch、協定文件與 targeted Adapters.V113 測試都完成，且 `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo` 通過。

## 📋 背景與假設

Java 來源為 `recv.properties CHANGE_CHANNEL=0x1F` 與 `send.properties CHANGE_CHANNEL=0x08`；C→S 只讀 `byte targetChannel`，S→C layout 為 `[short opcode][byte success=1][4-byte ip][short port]`。MapleForge MVP 是 login+channel 單 process，沒有獨立 channel server，因此忽略目標 channel，保存 DB 後回同一 IP/port，既有 `V113ChannelConnectionHandler` finally 負責 deregister/cleanup。

## 🪜 計畫步驟

- [x] 1. 探查現有 Channel opcode、packet、handler 與設定中的 channel port 來源。
- [x] 2. 新增 `V113ChannelChangePackets` 與 opcode 常數。
- [x] 3. 在 `V113ChannelConnectionHandler` 加最小 dispatch：flush inventory、save character、send change-channel packet。
- [x] 4. 補 targeted Adapters.V113 測試與 protocol doc。
- [x] 5. 跑指定測試並回填任務歷程/進度日誌。

## 📜 執行歷程（邊做邊追加，附時間）

- **14:16** 建立任務歷程，狀態切 `🚧 執行中`；下一步先探查既有 handler 與 channel port 設定來源。
- **14:23** 確認 channel port 來源為 `ServerInstanceOptions.ChannelPort`，預設與 appsettings 皆為 `8585`；已新增 opcode/packet、handler dispatch、Host 的 `V113ChannelOptions` endpoint 投影與 packet tests。
- **14:23** 對照 Java：`InterServerHandler.ChangeChannel` 讀 `slea.readByte()+1`，`MaplePacketCreator.getChannelChange` 寫 `[0x08][1][ip][port]`；已同步 `v113-protocol-spec.md`。
- **14:23** 驗證通過：Adapters.V113 254 passed + 1 skipped；Host.Login targeted build 0 warning / 0 error；`git diff --check` exit 0（僅既有 CRLF normalization warnings）。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> ✅ 已完成並驗證。若後續接手，下一步是用真 v113 client 點換頻道跑 GUI smoke，觀察 client 收 `CHANGE_CHANNEL(0x08)` 後是否重連 `127.0.0.1:8585` 並走正常 `PLAYER_LOGGEDIN`。

## ✅ 結果與結論

達標。MapleForge 已具備單進程 MVP 換頻道流程：收到 `CHANGE_CHANNEL(0x1F)` 後保存角色文件並回同一個設定 channel endpoint；cleanup 不新增分支，仍由既有 handler finally 統一處理。證據層級為 Java source + MapleForge targeted tests/build，真客戶端 GUI smoke 尚未執行。

## 🔗 產出

- `src/Maple.Adapters.V113/Channel/V113ChannelChangePackets.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelOpcodes.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `src/Maple.Host.Shared/MapleServerHost.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelChangeChannelTests.cs`
- `docs/specs/v113-protocol-spec.md`
- 驗證：`dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo`、`dotnet build src/Maple.Host.Login/Maple.Host.Login.csproj --no-restore --nologo`、`git diff --check`
