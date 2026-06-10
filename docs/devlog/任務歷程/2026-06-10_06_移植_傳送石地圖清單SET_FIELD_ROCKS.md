---
編號: 2026-06-10_06
標題: 移植傳送石地圖清單 SET_FIELD AddRocksInfo
類型: 移植
狀態: 完成
建立: 2026-06-10
更新: 2026-06-10
關聯里程碑: Java→.NET 移植主線 / 玩家體感功能
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 目標

補上普通傳送石與 VIP 傳送石地圖清單資料，使 `SET_FIELD` 輸出符合舊 Java `addRocksInfo`。

完成判準：

1. Core 保存普通 5 格與 VIP 10 格傳送石地圖。
2. `SET_FIELD` 依 v113/Java 順序輸出普通 5 格再 VIP 10 格。
3. 空欄位使用舊 Java 空地圖值。
4. 有封包或 domain 測試覆蓋。

## 執行歷程

- 建檔定目標。
- Core `Character` 新增普通 5 格與 VIP 10 格傳送石地圖清單，空欄位使用 Java `999999999`。
- 新增新增/刪除/正規化傳送石清單 domain 方法。
- `SET_FIELD` `AddRocksInfo` 改為依 Java 順序輸出普通 5 格，再 VIP 10 格。
- 測試：Core rocks domain 與 `SET_FIELD` rocks layout。

## 接手點

- 已完成 headless/單元驗證；待真機登入後 UI smoke 確認傳送石清單顯示。

## 結果

- 完成。`dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj` 通過 43/43；`dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj` 通過 154/154。
