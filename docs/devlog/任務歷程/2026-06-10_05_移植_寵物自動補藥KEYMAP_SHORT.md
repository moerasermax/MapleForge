---
編號: 2026-06-10_05
標題: 移植寵物自動補藥 KEYMAP short branch
類型: 移植
狀態: 完成
建立: 2026-06-10
更新: 2026-06-10
關聯里程碑: Java→.NET 移植主線 / 玩家體感功能
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 目標

補齊舊 Java `CHANGE_KEYMAP` 的短封包分支，保存寵物自動 HP/MP 補藥設定。

完成判準：

1. keymap parser 保留短封包的 type/data。
2. Core 保存寵物自動 HP/MP 道具 ID。
3. Channel handler 更新並持久化角色設定。
4. 有 parser 或 domain 測試覆蓋。

## 執行歷程

- 建檔定目標。
- `V113KeymapPackets.ParseChangeKeymap` 保留短封包 `type/data`。
- Core `Character` 新增 `PetAutoHpItemId` / `PetAutoMpItemId`，並依 Java type 1/2 更新或清除。
- Channel `CHANGE_KEYMAP` 短分支改為更新角色文件後放行。
- 測試：Adapters short branch parser 與 Core pet auto-pot domain。

## 接手點

- 已完成 headless/單元驗證；實際自動補血/補魔施放仍依賴後續寵物系統。

## 結果

- 完成。`dotnet test tests\Maple.Core.Tests\Maple.Core.Tests.csproj` 通過 43/43；`dotnet test tests\Maple.Adapters.V113.Tests\Maple.Adapters.V113.Tests.csproj` 通過 154/154。
