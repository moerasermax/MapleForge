---
編號: 2026-09-06_49
標題: P047 — IMapSessionRegistry 改帶 Player，接上 SPAWN_PLAYER 戒指外觀（P046 深化）
類型: 修補
狀態: ✅ 完成
建立: 2026-09-06
更新: 2026-09-06
關聯里程碑: P047
關聯記憶: <空>
關聯commit: 待填
---

## 🎯 目標（執行前先寫死，過程不偷改）

P046 查出 `V113MapPackets.SpawnPlayer` 已保留位元組位置但硬編碼恆為 0 的
`MarriageRingLook` 片段，阻塞點是 `IMapSessionRegistry.GetOthers` 回傳的
`MapPlayerEntry` 只帶 `Character`，取不到只存在 `Player` 執行期欄位的戒指狀態
（`MarriagePartnerCharacterId`/`MarriageRingId`）。完成判準：`MapPlayerEntry` 能取得
完整 `Player`，`SpawnPlayer` 依對方玩家的實際戒指狀態動態寫入。

## 📋 背景與查證

- 查證 `Register` 的兩個既有呼叫點（`V113ChannelConnectionHandler.cs:469`/`2141`）都已經
  有 `player` 變數在 scope 內（`chr` 本來就是 `player.Character` 的別名），改介面簽章不需要
  額外查找，是低風險的直接替換。
- `GetOthers` 的既有消費端（`FollowHandler`/`PartySearchHandler`/`RingHandler`/
  `SummonHandler`）都只用到 `entry.Character`——在 `MapPlayerEntry` 上保留一個
  `Character => Player.Character` 的薄轉發計算屬性，這些消費端完全不用改，把異動範圍鎖在
  「新增讀取路徑」而非「重寫既有讀取路徑」。
- 對照 Java `MaplePacketCreator.spawnPlayerMapobject`／`addMarriageRingLook`：確認
  MapleForge 現有的 `rings × 2`（一般戒指清單）欄位仍是另一個更大的獨立缺口（角色戒指
  清單本身，非婚戒外觀），本次刻意只處理婚戒外觀這一項，不擴大範圍。

## 🔧 實作內容

- **`Maple.Application`**：
  - `IMapSessionRegistry.cs`：`Register` 簽章從 `Character character` 改成 `Player player`；
    `MapPlayerEntry` 從 `Character Character` 改成 `Player Player`，新增計算屬性
    `Character Character => Player.Character` 維持既有消費端相容。
  - `InMemoryMapSessionRegistry.cs`：`Register` 同步改參數型別，內部建構 `MapPlayerEntry`
    直接帶入 `player`。
- **`Maple.Adapters.V113`**：
  - `V113ChannelConnectionHandler.cs`：兩處 `_mapRegistry.Register(...)` 呼叫改傳
    `player`（原本傳 `chr`，兩者本就是同一參考）。
  - `BuildSpawnPlayerPacketAsync` 簽章從 `Character chr` 改成 `Player player`（內部
    `var chr = player.Character;` 維持既有欄位存取不變）；「進場迴圈」兩處呼叫改傳
    `player`（新玩家自己）與 `other.Player`（既有玩家，透過新的 `MapPlayerEntry.Player`）。
  - `V113MapPackets.SpawnPlayer`：簽章從 `Character chr` 改成 `Player player`；`// marriage
    ring look` 段落改成依 `player.HasVisibleMarriageRing` 動態寫入（比照
    `V113RingPackets.MarriageRingLook` 的邏輯直接內嵌，不額外配置陣列），其餘欄位（buff
    mask/CHAR_MAGIC 區塊/AddCharLook/mount/announce box/chalkboard/rings×2）完全不動。

## 🧪 測試

- `tests/Maple.Adapters.V113.Tests/ChannelRingPacketTests.cs` 新增：
  - `SpawnPlayer_WithVisibleMarriageRing_EmbedsRingLookBytes`：戴戒指時，封包內含
    `V113RingPackets.MarriageRingLook` 產出的完整片段（子字串比對）。
  - `SpawnPlayer_WithoutMarriageRing_PacketIsExactlyRingFragmentShorter`：兩個玩家裝備
    同一枚戒指道具（讓 `AddCharLook` 輸出相同），只差在是否呼叫 `WearMarriageRing`，
    驗證封包長度剛好差 12 bytes（13 bytes 戒指片段 − 1 byte 無戒指旗標）。
- 既有 `ChannelGuildPacketTests.SpawnPlayer_WritesGuildDisplayBeforeBuffMasks` 改用
  `Player` 包裝 `Character` 呼叫，斷言內容不變。
- `tests/Maple.Adapters.V113.Tests/ChannelPartySearchTests.cs` 的 `mapRegistry.Register`
  呼叫改傳 `Player`。
- `dotnet build` 0 warning/0 error；全 8 個測試專案 968 passed / 1 skipped（P046 收案基準
  966 +2：Adapters.V113 490→492）；Core/Application 禁區 grep clean。

## ✅ 結果與結論

- `MapPlayerEntry` 保留 `Character` 計算屬性這個選擇，讓異動範圍精準限縮在「新增
  `Player` 讀取能力」，既有四個消費端（Follow/PartySearch/Ring/Summon Handler）完全
  不用碰，是這次能把 P046 記錄的「全域契約異動」控制在單一 P-phase 內完成的關鍵。
- 一般戒指清單（`rings × 2`，`chr.getRings(false)` 對照的「Crush Ring／Friendship
  Ring」清單，非婚戒外觀）仍是恆寫 0 的獨立缺口，留給後續評估——這需要先確認 MapleForge
  是否已有一般戒指的資料模型（目前只查證了婚戒 `Player.Ring.cs`）。

## 🔗 產出

- 修改：`src/Maple.Application/Maps/IMapSessionRegistry.cs`、
  `src/Maple.Application/Maps/InMemoryMapSessionRegistry.cs`、
  `src/Maple.Adapters.V113/Channel/V113ChannelConnectionHandler.cs`、
  `src/Maple.Adapters.V113/Channel/V113MapPackets.cs`
- 修改（測試）：`tests/Maple.Adapters.V113.Tests/ChannelRingPacketTests.cs`、
  `tests/Maple.Adapters.V113.Tests/ChannelGuildPacketTests.cs`、
  `tests/Maple.Adapters.V113.Tests/ChannelPartySearchTests.cs`
- commit：待填
