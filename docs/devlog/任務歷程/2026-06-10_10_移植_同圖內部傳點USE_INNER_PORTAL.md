---
編號: 2026-06-10_10
標題: 移植同圖內部傳點 USE_INNER_PORTAL
類型: 移植
狀態: 完成
建立: 2026-06-10
更新: 2026-06-10
關聯里程碑: Java→.NET 移植主線 / 地圖移動
關聯記憶: v113-pivot-port-from-java
關聯commit: 未提交
---

## 目標

移植舊 Java `PlayerHandler.InnerPortal` 的同圖座標傳送主幹，讓 `USE_INNER_PORTAL` 更新玩家位置並回 `CURRENT_MAP_WARP`。

完成判準：

1. v113 adapter 接上 `USE_INNER_PORTAL(0x5F)` parser。
2. Channel handler 驗證當前地圖 portal 存在後更新 runtime 位置。
3. 回送 `CURRENT_MAP_WARP(0xC8)`，避免客戶端卡動作。
4. 有 parser/封包或 domain 測試覆蓋。

## 執行歷程

- 建檔定目標。
- 對照舊 Java `PlayerHandler.InnerPortal` 與 `MaplePacketCreator.instantMapWarp`，確認 `USE_INNER_PORTAL=0x5F`、`CURRENT_MAP_WARP=0xC8`。
- 新增 `V113InnerPortalPackets`，解析 skip byte、portal name、目標 x/y，並編碼 `CURRENT_MAP_WARP`。
- Channel handler 讀取當前 map data，驗 portal 名稱存在後更新玩家 runtime 座標並回 warp 封包。
- 補 Adapter parser/encoder 測試。

## 接手點

- 本任務已完成。後續若要更貼近 Java，應補玩家與 portal 距離容錯/反作弊分支，以及真機同圖 portal smoke。

## 結果

- 完成 `USE_INNER_PORTAL(0x5F)` 主幹移植。
- 驗證：Core 45/45、Application 83/83、Adapters.V113 165/165、Host build 0 警告 0 錯誤。
- 邊界：本任務只處理同圖內部 portal；`CHANGE_MAP_SPECIAL(0x5E)` 的 script portal/enterPortal 語義仍未移植。
