---
編號: 2026-06-10_07
標題: 移植傳送石新增刪除 TROCK_ADD_MAP
類型: 移植
狀態: 完成
建立: 2026-06-10
更新: 2026-06-10
關聯里程碑: Java→.NET 移植主線 / 玩家體感功能
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 目標

移植舊 Java `TROCK_ADD_MAP` 行為，支援普通/VIP 傳送石地圖新增、刪除與 `MAP_TRANSFER_RESULT` 回包。

完成判準：

1. v113 adapter 解析 `TROCK_ADD_MAP(0x60)`。
2. Application/handler 更新角色傳送石清單。
3. 送出 `MAP_TRANSFER_RESULT` refresh 封包。
4. 有 parser 或封包測試覆蓋。

## 執行歷程

- 建檔定目標。
- Adapters.V113 新增 `TROCK_ADD_MAP(0x60)` parser 與 `MAP_TRANSFER_RESULT(0x27)` refresh 封包。
- Channel handler 支援普通/VIP 傳送石新增當前地圖與刪除指定地圖，保留舊 Java 限制：一般石不可存 `>197010000` 或 `180000000`，VIP 不可存 `180000000`。
- 角色資料變更時持久化，並送 refresh 給客戶端。
- 測試：Adapters Trock parser 與 refresh layout。

## 接手點

- 已完成 headless/單元驗證；FieldLimitType.VipRock 尚未移植，特殊地圖限制目前只保留 Java 明寫 map id 條件。

## 結果

- 完成。`dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj` 通過 43/43；`dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj` 通過 154/154。
