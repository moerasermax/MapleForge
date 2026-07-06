# MapleForge Persistence Model

> 狀態：活文件。記錄會影響資料相容性的 collection/document shape 與 repository 語義。

## 原則

- Repository 介面放在 `Maple.Core`；LiteDB/Mongo 實作放在 `Maple.Persistence`。
- 執行期富領域物件不直接作為長期 schema 承諾；持久化以文件/快照 shape 為準。
- v113 opcode、packet byte layout 不進 persistence model。

## Hired Merchant

來源任務：P003-D5 HiredMerchant Cut1；P003-D7 補 merchant position 持久化。Java 對照：`HiredMerchantHandler.java`、`HiredMerchant.java`、`AbstractPlayerStore.saveItems`、`PlayerInteractionHandler` 商店分支。

Repository contract：`Maple.Core.PlayerShops.IHiredMerchantRepository`。

### Collections

`hired_merchants`

| Field | Meaning |
|---|---|
| `StoreId` | Merchant id / primary key. LiteDB auto id; Mongo uses `counters` sequence `hired_merchants`. |
| `OwnerId`, `OwnerAccountId`, `OwnerName` | Owner identity; account+character queries prevent duplicate active merchant/package. |
| `ItemId` | Hired merchant permit/item id used to create the shop. |
| `Title` | Store title/description. |
| `MapId`, `Channel` | Runtime placement target for Cut2 spawn/remote control. |
| `X`, `Y`, `Stance`, `Foothold` | Merchant map position used by startup/map-entry spawn replay. Added in P003-D7 to remove the Cut2 `(0,0)` fallback for newly persisted merchants. |
| `Mesos` | Accumulated merchant proceeds after Java `EntrustedStoreTax` calculation. |
| `Status` | `Draft`, `Open`, `Maintenance`, `PendingClaim`, `Closed`, `Expired`. |
| `OpenedAtUnixMillis`, `ExpireAtUnixMillis` | UTC unix millis; default hired merchant duration is 24 hours. |
| `MaxListings`, `MaxVisitors` | Domain limits; defaults are 16 listings and 3 visitor seats. |
| `Blacklist` | Owner-maintained visitor blacklist names. |

Indexes:

- owner/status: `OwnerAccountId`, `OwnerId`, `Status`
- map/status: `Channel`, `MapId`, `Status`
- expiry: `Status`, `ExpireAtUnixMillis`

`hired_merchant_items`

| Field | Meaning |
|---|---|
| `Id` | Stable row id: `{StoreId}:{ListingId}`. |
| `StoreId` | Parent merchant id. |
| `ListingId` | Domain listing id, preserved across roundtrip. |
| `InventoryType` | MapleForge neutral inventory type byte. |
| `Item` | `ItemRecord` snapshot for one bundle; for equips this is the full equip snapshot. |
| `Bundles` | Remaining bundle count. |
| `BundleQuantity` | Quantity per bundle. |
| `Price` | Meso price per bundle. |

### Semantics

- Open/maintenance merchants remain in the two active collections; purchases update `Bundles` and `Mesos`.
- Closing or expiring a hired merchant marks it `PendingClaim` or `Expired` and keeps remaining items/mesos for Fredrick-style claim.
- Successful claim gives all remaining items and mesos to the owner and deletes both header and item rows.
- Store proceeds use Java `GameConstants.EntrustedStoreTax` thresholds in Core domain logic.
- `ItemRecord` stores the per-bundle item snapshot; settlement reconstructs quantity as `BundleQuantity * Bundles`.
