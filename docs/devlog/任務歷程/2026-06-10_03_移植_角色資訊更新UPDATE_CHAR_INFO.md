---
編號: 2026-06-10_03
標題: 移植角色資訊更新 UPDATE_CHAR_INFO
類型: 移植
狀態: 完成
建立: 2026-06-10
更新: 2026-06-10
關聯里程碑: Java→.NET 移植主線 / 玩家體感功能
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 目標

移植舊 Java `UPDATE_CHAR_INFO` 行為，讓玩家可更新角色資訊頁的個人訊息、表情、血型、生日與星座欄位。

完成判準：

1. Core 保存角色資訊頁欄位。
2. v113 adapter 解析 `UPDATE_CHAR_INFO(0x97)`。
3. `CHAR_INFO` 會輸出實際欄位，不再固定空值。
4. 有封包或 domain 測試覆蓋。

## 執行歷程

- 建檔定目標。
- Core `Character` 新增角色資訊頁欄位：個人訊息、表情、血型、生日、星座。
- Adapters.V113 新增 `UPDATE_CHAR_INFO(0x97)` 解析，Channel handler 依 type 更新角色文件。
- `CHAR_INFO(0x36)` 改為輸出角色實際資訊頁欄位。
- 測試：Core profile domain、Adapters `ParseUpdateCharInfo` 與 `CharInfo` layout。

## 接手點

- 已完成 headless/單元驗證；待真客戶端角色資訊 UI smoke。

## 結果

- 完成。`dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj` 通過 43/43；`dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj` 通過 154/154。
