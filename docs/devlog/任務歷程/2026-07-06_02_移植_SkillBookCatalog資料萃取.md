---
編號: 2026-07-06_02
標題: P003 D4b SkillBook catalog 資料萃取
類型: 移植
狀態: ✅ 完成
建立: 2026-07-06 17:09
更新: 2026-07-06 17:31
關聯里程碑: P003-D4b
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

從 v113 `Item.wz` 萃取 228x mastery book / 229x skill book 的 `itemId -> skillIds, successRate, reqSkillLevel, masterLevel`，產出既有 `JsonSkillBookCatalog` 可載入的 JSON，並新增 Content 測試證明 catalog 筆數大於 0 且抽查 `2290096` 等已知項目；完成時 `dotnet build` 與 `dotnet test tests/Maple.Content.Tests/Maple.Content.Tests.csproj` 綠，commit 並 push。

## 📋 背景與假設

P002 已完成 `ISkillBookCatalog` / `JsonSkillBookCatalog` / `V113SkillBookHandler` 全鏈，但預設 JSON 仍為空。Java 行為神諭是 `MapleItemInformationProvider.getSkillStats(itemId)` 讀 `Item.wz` 的 `info/skill` 結構；MapleForge 已有 `Maple.Content/Wz` reader 與測試硬編 v113 client WZ 路徑。

## 🪜 計畫步驟

- [x] 1. 定位 Java `getSkillStats` 欄位語義與 `Item.wz` 實際節點形狀。
- [x] 2. 寫一次性 tools 萃取器，使用既有 WZ model 讀 `Item.wz`，輸出既有 JSON schema。
- [x] 3. 產出/替換 `src/Maple.Content/Skills/minimal-skill-books.v113.json`。
- [x] 4. 補 Content 測試：筆數 > 0，抽查 `2290096` 與另一筆內容。
- [x] 5. 跑 targeted build/test，更新活文件與任務歷程，commit + push。

## 📜 執行歷程（邊做邊追加，附時間）

- **17:09** 已完成 AGENTS 必讀與 WZ/protocol 相關規範讀取；工作樹有兩個既有文件變更，需避開不覆蓋。
- **17:18** Java 神諭確認 `getSkillStats` 讀 `info/masterLevel`、`info/reqSkillLevel`、`info/success` 與 `info/skill/{0..n}`；工具萃取 `Item.wz` 成 165 筆（228x=26、229x=139）。抽查 `2290096` 與 `2280000` 完成，已補 Content catalog 測試。
- **17:31** 驗證完成：`dotnet build` 綠；Content 18、Core 104、Application 140、Adapters 412+1skip 綠；SkillBookExtractor build 綠。已同步進度日誌，準備只 stage D4b 檔案 commit/push。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

> 已完成；接手只需確認 commit/push 狀態。若 push 被拒，依派工指示 `git pull --rebase` 後再 push。

## ✅ 結果與結論

已達標。`Item.wz` 存在於 `D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client\Item.wz`，以既有 WZ reader 成功萃取 165 筆技能書資料：228x 26 筆、229x 139 筆。未改 JSON schema，只在 `JsonSkillBookCatalog` 補 `Count` 方便測試驗證載入筆數。

抽查：
- `2290096`：skills `[1121000,1221000,1321000,2121000,2221000,2321000,3121000,3221000,4121000,4221000,5121000,5221000,21121000]`，success 70，reqSkillLevel 5，masterLevel 20。
- `2280000`：skill `[2121003]`，success 100，reqSkillLevel 0，masterLevel 10。

驗證：
- `dotnet build --nologo -v quiet`：0 warning / 0 error。
- `dotnet test tests/Maple.Content.Tests/Maple.Content.Tests.csproj -v quiet --nologo`：18 passed。
- 抽驗既有測試：Core 104 passed、Application 140 passed、Adapters 412 passed / 1 skipped。
- `dotnet build tools/Maple.Tools.SkillBookExtractor/Maple.Tools.SkillBookExtractor.csproj --nologo -v quiet`：0 warning / 0 error。

缺口：本刀只涵蓋派工指定 228x/229x；Java `getSkillStats` 也允許 562x，但不在本次範圍。未做真客戶端 skill-book UI smoke。

## 🔗 產出

- `src/Maple.Content/Skills/minimal-skill-books.v113.json`
- `src/Maple.Content/Skills/JsonSkillBookCatalog.cs`
- `tests/Maple.Content.Tests/Skills/JsonSkillBookCatalogTests.cs`
- `tools/Maple.Tools.SkillBookExtractor/`
- `docs/devlog/進度日誌.md`
- commit：待填
