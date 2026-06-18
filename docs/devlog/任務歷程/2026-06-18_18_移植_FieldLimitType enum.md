---
編號: 2026-06-18_18
標題: FieldLimitType enum
類型: 移植
狀態: ✅ 完成
建立: 2026-06-18 15:37
更新: 2026-06-18 15:40
關聯里程碑: M4-6
關聯記憶:
關聯commit: cf4981b
---

## 🎯 目標（執行前先寫死，過程不偷改）

新增 `Maple.Core.Maps.FieldLimitType` flags enum 與 `Check` extension，值以舊 Java `FieldLimitType.java` 為 ground truth，並以 `Maple.Core.Tests` 驗證 `VipRock` 與組合旗標判斷；完成判準是指定 Core test command 綠燈，且 `Maple.Core` 不引入 `Maple.Adapters.V113`。

## 📋 背景與假設

Teleport rock cash item 後續 D7 需要用 `FieldLimitType.VipRock.check(map.getFieldLimit())` 等價語義判斷地圖限制。此任務只建立 domain bitmask 基礎，不接 handler、不改 protocol 文件。

## 🪜 計畫步驟

- [x] 1. 讀取舊 Java `FieldLimitType.java`，確認所有 enum 與 bit 值。
- [x] 2. 在 `Maple.Core/Maps` 新增 flags enum 與 `Check` extension。
- [x] 3. 在 `Maple.Core.Tests/Maps` 新增 focused tests。
- [x] 4. 執行 `dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj -v quiet --nologo` 並檢查 Core adapter import。

## 📜 執行歷程（邊做邊追加，附時間）

- **15:37** 建立任務歷程；下一步讀 Java reference 與現有 Core map/test 結構。
- **15:39** 已從 Java reference 確認 `VipRock = 0x40` 與全部 field-limit bit 值；已新增 Core enum/extension 與 focused Core tests。
- **15:40** Core tests 102/102 passed；`rg "using Maple\.Adapters\.V113" src/Maple.Core` 無命中。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

下一步：若接續 D7，可在 teleport rock handler 使用 `FieldLimitType.VipRock.Check(fieldLimit)` 判斷 VIP rock map restriction。

## ✅ 結果與結論

達標。`Maple.Core.Maps` 已有 Java reference 對齊的 `FieldLimitType` flags enum，`VipRock = 0x40`，並提供 `Check` extension；Core focused tests 通過，且 Core 未引入 v113 adapter using。

## 🔗 產出

新增：

- `src/Maple.Core/Maps/FieldLimitType.cs`
- `tests/Maple.Core.Tests/Maps/FieldLimitTypeTests.cs`

驗證：

- `dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj -v quiet --nologo` → 102 passed
- `rg "using Maple\.Adapters\.V113" src/Maple.Core` → no matches

Commit：未提交。
