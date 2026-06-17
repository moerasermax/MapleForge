---
編號: 2026-06-17_03
標題: 移植升級卷軸 USE_UPGRADE_SCROLL
類型: 移植
狀態: ✅ 完成
建立: 2026-06-17 14:16
更新: 2026-06-17 14:31
關聯里程碑: M4-6
關聯記憶:
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

移植 v113 `USE_UPGRADE_SCROLL(0x50)` 主幹：Core/Application 承載卷軸效果與裝備變更語義，Adapters.V113 只負責 packet/opcode/layout，完成成功、失敗、白衣卷軸保護、詛咒破壞、消耗道具與無升級次數失敗等單元驗證；同步 protocol/devlog 文件。

完成判準：

- `Maple.Core` 新增版本無關 `ScrollEffect` / `IScrollCatalog`。
- `Maple.Application` 新增 deterministic `ScrollService` 與 `HardcodedScrollCatalog`，測試覆蓋成功、失敗、白衣保護、詛咒、卷軸消耗與無 upgrade slot。
- `Maple.Adapters.V113` 接 `USE_UPGRADE_SCROLL=0x50`，解析 Java layout，送 scroll effect 與 inventory 修改封包。
- `docs/specs/v113-protocol-spec.md`、本任務歷程與進度日誌更新。
- Targeted tests：Core / Application / Adapters.V113 通過或留下明確未通過原因。

## 📋 背景與假設

Java 行為來源：`InventoryHandler.UseUpgradeScroll`，recv opcode `USE_UPGRADE_SCROLL=0x50`，packet layout 為 `int tick, short scrollSlot, short equipSlot, short flags`，`flags & 2` 表示使用白衣卷軸。卷軸資料先用 hardcoded catalog 作 MVP，之後可替換為 WZ/item metadata provider。

分層假設：成功率、裝備數值變更、消耗道具與白衣保護是 Application use case；v113 opcode、`SHOW_SCROLL_EFFECT` 與 `MODIFY_INVENTORY_ITEM` byte layout 留在 Adapters.V113。

## 🪜 計畫步驟

- [x] 1. 探查現有 inventory/equip/player/item-use 與 channel handler/packet patterns。
- [x] 2. 查 Java `SHOW_SCROLL_EFFECT` send opcode 與 scroll packet creator layout。
- [x] 3. 新增 Core scroll model、Application service/catalog 與單元測試。
- [x] 4. 新增 V113 handler/parser/packets/opcodes/DI wiring 與 adapter tests。
- [x] 5. 跑 targeted tests，修正失敗。
- [x] 6. 更新 protocol spec、進度日誌與本任務收尾。

## 📜 執行歷程（邊做邊追加，附時間）

- **14:16** 建立任務歷程並切為執行中；下一步探查現有 item-use/inventory/channel patterns 與 Java 來源。
- **14:19** 查 Java：`USE_UPGRADE_SCROLL=0x50`、`SHOW_SCROLL_EFFECT=0x9F`；`getScrollEffect` layout 為 success byte + curse byte + legendarySpirit short + trailing byte。
- **14:23** 完成 Core/Application：新增 `ScrollEffect` / `IScrollCatalog`、`ScrollService`、`HardcodedScrollCatalog`，並補 `EquipEntry` mutable stats 與穿脫裝轉換保留 stats。
- **14:25** Application tests 通過 127/127；後續既有未提交 USE_ITEM 工作使最終 App 總數為 134/134。
- **14:28** 完成 V113 parser/handler/packet/opcode/DI/channel switch，並更新 `SET_FIELD` equipped item info 送出真實 equip stats。
- **14:29** V113 adapter tests 通過 263 passed + 1 skipped；Core tests 通過 65/65。
- **14:31** 最終驗證：Core 65/65、Application 134/134、Adapters.V113 263+1skip、Host.Shared build 0 warning/0 error；`git diff --check` exit 0（僅 CRLF normalization warnings）。

## ⏯️ 接手點（★崩潰救命行★ — 永遠保持最新一行）

已完成 USE_UPGRADE_SCROLL MVP 與文件同步。後續若接手，下一步是跑真 v113 client scroll UI/effect smoke，並把 hardcoded scroll catalog 替換/擴充為 WZ-backed metadata。

## ✅ 結果與結論

達標。卷軸主幹已落在 Core/Application/V113 正確分層：Core 放 scroll catalog contract 與 rich equip data，Application 執行 deterministic scroll use case，V113 adapter 負責 opcode、packet layout、inventory modify 與 effect broadcast。

保留：完整 scroll compatibility、特殊卷軸、WZ metadata 與真 client smoke 未做；`SHOW_SCROLL_EFFECT` trailing byte 目前依任務要求承載 white-scroll flag，但 Java source 寫固定 0，需 live/capture 再確認。

## 🔗 產出

主要產出：

- `src/Maple.Core/Items/ScrollEffect.cs`
- `src/Maple.Application/Items/ScrollService.cs`
- `src/Maple.Application/Items/HardcodedScrollCatalog.cs`
- `src/Maple.Adapters.V113/Channel/V113ScrollHandler.cs`
- `tests/Maple.Application.Tests/Items/ScrollServiceTests.cs`
- `tests/Maple.Adapters.V113.Tests/ChannelScrollPacketTests.cs`
- 補強 `EquipEntry`、`Player.Equip` conversion、`V113ChannelPackets` equipped item info、Channel opcode/dispatch 與 Host DI。
- 文件：`docs/specs/v113-protocol-spec.md`、`docs/devlog/任務追蹤.md`、`docs/devlog/進度日誌.md`、任務歷程 README。

commit：待填。
