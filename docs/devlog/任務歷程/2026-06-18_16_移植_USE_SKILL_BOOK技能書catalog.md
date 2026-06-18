---
編號: 2026-06-18_16
標題: USE_SKILL_BOOK 技能書 catalog 全鏈
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 15:35
更新: 2026-06-18 15:58
關聯里程碑: M6-3
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

完成 `USE_SKILL_BOOK(0x4C)` 從 MVP stub 到可用主鏈：新增 Core 技能書 catalog contract、Content JSON catalog、V113 handler catalog 查詢/職業匹配/等級與 master level 檢查/成功率 roll/技能 master level 更新/消耗道具/廣播結果，並以 targeted xUnit 覆蓋 catalog 與 handler 主要分支。

完成判準：
- `Maple.Core` 不依賴 `Maple.Adapters.V113`。
- `JsonSkillBookCatalog` 可從 stream/path 載入 JSON，支援大小寫不敏感、註解、尾逗號。
- `HandleUseSkillBookAsync` 不再固定失敗，catalog miss 只 enable actions，其他分支依 Java 語義處理。
- 技能書成功時保留目前技能 level、提升 master level、消耗道具並廣播 useSkillBook 結果。
- 指定 Content/Adapters 測試通過。

## 📋 背景與假設

Java 行為神諭：`../TestMapleStoryV113_Server/src/handling/channel/handler/InventoryHandler.java` 的 `UseSkillBook`。技能書資料來源等價於 `MapleItemInformationProvider.getSkillStats(itemId)`，MapleForge 本輪以 JSON catalog 承載，不把 v113 byte/protocol 細節放進 Core。

## 🪜 計畫步驟

- [x] 1. 檢查既有 catalog、Player skill、handler、測試與 DI patterns。
- [x] 2. 新增 Core `SkillBookDefinition` / `ISkillBookCatalog`。
- [x] 3. 新增 Content `JsonSkillBookCatalog` 與 catalog 測試。
- [x] 4. 改造 V113 channel handler 注入 catalog 並實作 USE_SKILL_BOOK 主鏈。
- [x] 5. 補 Adapter handler 測試與必要測試 helper。
- [x] 6. 跑指定 targeted tests，確認 Core/Application 無 v113 import 破壞。

## 📜 執行歷程（邊做邊追加，附時間）

- **15:35** 建立任務歷程；下一步檢查 catalog/handler/player skill 現況。
- **15:49** 已新增 Core catalog contract、Content JSON catalog、v113 skill-book helper、channel handler DI/流程接線與 focused tests；下一步跑 targeted tests。
- **15:58** Content tests 17/17 綠、skill-book adapter focused tests 8/8 綠、Host.Shared build 綠；完整 Adapters.V113 測試專案目前被既有 `ChannelUseCashItemTests.CashPetFood_FeedsActivePetAndConsumesItem` 失敗擋住，與本任務 skill-book path 無關。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

若此刻斷線，skill-book 任務本身已完成；若要追完整 Adapters.V113 專案綠燈，先處理既有 cash-item pet-food 測試期待值與目前 handler 兩個 broadcast packets 的落差。

## ✅ 結果與結論

`USE_SKILL_BOOK(0x4C)` 已從固定失敗 stub 改為 catalog-backed 主鏈。成功率 100 會更新 master level 並送 skill update；成功率 0 會消耗書但不更新 master level；catalog miss/非法 item 只 enable actions；職業不符、技能等級不足或 master level 已達標會廣播 canUse=false 且不消耗。

驗證：
- `dotnet test tests/Maple.Content.Tests/Maple.Content.Tests.csproj -v quiet --nologo`：17/17 passed。
- `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj --filter "FullyQualifiedName~ChannelSkillBookPacketTests" -v quiet --nologo`：8/8 passed。
- `dotnet build src/Maple.Host.Shared/Maple.Host.Shared.csproj --nologo -v quiet`：0 warning / 0 error。
- Core/Application `^using Maple.Adapters.V113` scan：no matches。
- 完整 `dotnet test tests/Maple.Adapters.V113.Tests/Maple.Adapters.V113.Tests.csproj -v quiet --nologo`：目前 339 passed / 1 failed / 1 skipped，唯一 failure 是既有 cash-item pet-food 測試，非本任務修改路徑。

## 🔗 產出

新增/修改：
- `src/Maple.Core/Skills/SkillBookDefinition.cs`
- `src/Maple.Core/Skills/ISkillBookCatalog.cs`
- `src/Maple.Content/Skills/JsonSkillBookCatalog.cs`
- `src/Maple.Content/Skills/minimal-skill-books.v113.json`
- `src/Maple.Content/Maple.Content.csproj`
- `src/Maple.Adapters.V113/Channel/V113SkillBookHandler.cs`
- `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`
- `src/Maple.Host.Shared/MapleServerHost.cs`
- `tests/Maple.Content.Tests/Skills/JsonSkillBookCatalogTests.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelSkillBookPacketTests.cs`
