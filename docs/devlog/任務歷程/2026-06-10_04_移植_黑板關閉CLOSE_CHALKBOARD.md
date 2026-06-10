---
編號: 2026-06-10_04
標題: 移植黑板關閉 CLOSE_CHALKBOARD
類型: 移植
狀態: 完成
建立: 2026-06-10
更新: 2026-06-10
關聯里程碑: Java→.NET 移植主線 / 玩家體感功能
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 目標

移植舊 Java 關閉黑板行為，讓 `CLOSE_CHALKBOARD` 可清除玩家黑板文字並廣播 v113 `CHALKBOARD` 封包。

完成判準：

1. Core/World 可保存與清除玩家黑板文字。
2. v113 adapter 接上 `CLOSE_CHALKBOARD(0x2B)`。
3. 產出 `CHALKBOARD(0x9C)` 關閉封包。
4. 有封包或 handler 測試覆蓋。

## 執行歷程

- 建檔定目標。
- Core `Player` 新增 runtime-only 黑板文字與清除方法。
- Adapters.V113 新增 `CHALKBOARD(0x9C)` 封包 builder，支援有文字與清除兩種 layout。
- Channel handler 接上 `CLOSE_CHALKBOARD(0x2B)`，清除本人 runtime 狀態並送給自己與同地圖玩家。
- 測試：Adapters Chalkboard opcode 與封包 layout。

## 接手點

- 已完成 headless/單元驗證；開黑板仍依賴後續 `USE_CASH_ITEM` 現金道具分支。

## 結果

- 完成。`dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj` 通過 43/43；`dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj` 通過 154/154。
