# v113 協定規格（X5・行為神諭）

> 從舊 Java 伺服器（`TestMapleStoryV113_Server`）逐項萃取，作為 `Maple.Adapters.V113` 的實作真值。
> 來源檔已標註。**M1 只需第 1~5 節即可讓客戶端顯示登入失敗。**

## 0. 版本常數（`constants/ServerConstants.java`）
- `MAPLE_VERSION = 113`
- `MAPLE_PATCH = "1"`
- locale：getHello 寫 `14`（GlobalMS）；結尾額外寫 `6`。

## 1. AES-OFB Cipher（`tools/MapleAESOFB.java`）
- **AES Key（256-bit, ECB, 無 padding；"AES" = AES/ECB/PKCS5 但只用 doFinal 單塊）**：
  `13 00 00 00 08 00 00 00 06 00 00 00 B4 00 00 00 1B 00 00 00 0F 00 00 00 33 00 00 00 52 00 00 00`
- **建構**：`new MapleAESOFB(iv[4], version)`；內部把 version 位元組互換：`((v>>8)&0xFF) | ((v<<8)&0xFF00)`。
- **crypt(data)**（OFB 串流）：
  - `myIv = iv 重複 4 次成 16 bytes`（`BitTools.multiplyBytes(iv,4,4)`）。
  - 區塊長度：第一塊 `0x5B0`，之後每塊 `0x5B4`。
  - 每 16 bytes：`myIv = AES.encrypt(myIv)`；`data[x] ^= myIv[(x-start)%16]`。
  - 結束後 `iv = getNewIv(iv)`。
- **getPacketHeader(length)**（送出時的 4-byte 頭，**有狀態**，用當前 iv）：
  ```
  iiv = ((iv[3]&0xFF) | ((iv[2]<<8)&0xFF00)) ^ versionSwapped
  mlen = (((len<<8)&0xFF00) | (len>>>8)) ^ iiv
  header = [ (iiv>>>8)&0xFF, iiv&0xFF, (mlen>>>8)&0xFF, mlen&0xFF ]
  ```
- **getPacketLength(header int)**：`plen = (h>>>16) ^ (h&0xFFFF)`；再 endian swap。
- **checkPacket**：`(p[0]^iv[2])==versionHigh && (p[1]^iv[3])==versionLow`。
- **getNewIv(oldIv)**（IV 演化 / shuffle）：
  - 初始 `in = {0xF2,0x53,0x50,0xC6}`；對 `oldIv` 4 個 byte 各做一次 `funnyShit`。
  - `funnyShit` 演算法見原檔 276–308 行（含 `funnyBytes[256]` 查表＋位移 `<<3 | >>>0x1d`）。
  - **`funnyBytes[256]` 表**：見原檔 48–64 行，需逐 byte 照抄。

## 2. 雙向 cipher 初始化（`handling/MapleServerHandler.java:173-174`）
- **Send cipher**（S→C）：`MapleAESOFB(sendIv, (short)(0xFFFF - 113))`
- **Recv cipher**（C→S）：`MapleAESOFB(recvIv, 113)`
- `sendIv`、`recvIv` 各為伺服器隨機產生的 4 bytes。

## 3. 握手 getHello（`tools/packet/LoginPacket.java:40`）— **未加密原始送出**
連線建立後，伺服器**先送這個原始封包**（前綴 2-byte 長度，無 AES 頭）：
```
writeShort(14)                 // locale: 14 = GlobalMS
writeShort(113)                // mapleVersion
writeMapleAsciiString("1")     // patch：[short len][bytes]
write(recvIv)                  // 4 bytes
write(sendIv)                  // 4 bytes
write(6)                       // 1 byte
```
> 送出後，雙方依第 2 節啟用 AES。之後所有封包都帶 4-byte AES 頭。

## 4. Opcodes（`properties/send.properties`、`recv.properties`）
**Recv（C→S）**：`LOGIN_PASSWORD=0x01`、`SERVERLIST_REQUEST=0x03`、`CHARLIST_REQUEST=0x04`、`PONG=0x0E`
**Send（S→C）**：`LOGIN_STATUS=0x00`、`PING=0x09`

## 5. 登入失敗封包（`tools/packet/LoginPacket.java:72` getLoginFailed）
```
writeShort(LOGIN_STATUS=0x00)
write(reason)        // 1 byte：4=密碼錯誤, 5=未註冊帳號, 7=已登入...
writeShort(0)
```
**M1 目標**：收到 `LOGIN_PASSWORD(0x01)` 後，回 `getLoginFailed(5)` → 客戶端顯示「非註冊帳號」＝管線打通。

## 6. 封包框架（`tools/data/MaplePacketLittleEndianWriter`、`handling/mina/MaplePacketDecoder`）
- 全部 little-endian。`writeMapleAsciiString` = `[short length][raw bytes]`。
- 解碼：讀 4-byte 頭 → `getPacketLength` → 收滿 body → `crypt` 解密 →（首 2 bytes 為 opcode，little-endian short）。

## 7. Channel 玩家體感封包（逐步補）

### 玩家表情 `FACE_EXPRESSION`

來源：

- `recv.properties`：`FACE_EXPRESSION = 0x2C`
- `send.properties`：`FACIAL_EXPRESSION = 0xB9`
- `MapleServerHandler`：`FACE_EXPRESSION` → `PlayerHandler.ChangeEmotion(slea.readInt(), chr)`
- `MaplePacketCreator.facialExpression(from, expression)`

C→S：

```
writeShort(0x2C)
writeInt(emote)
```

S→C 廣播給同地圖其他玩家：

```
writeShort(0xB9)
writeInt(characterId)
writeInt(expression)
```

備註：舊 Java 對 `emote > 7` 會檢查現金表情道具持有；MapleForge 目前先移植基礎廣播語義，完整現金表情持有檢查依賴後續道具使用/現金道具完整系統。

### 玩家椅子 `USE_CHAIR` / `CANCEL_CHAIR`

來源：

- `recv.properties`：`USE_CHAIR = 0x23`、`CANCEL_CHAIR = 0x22`
- `send.properties`：`SHOW_CHAIR = 0xBD`、`CANCEL_CHAIR = 0xC6`
- `MapleServerHandler`：`USE_CHAIR` → `PlayerHandler.UseChair(slea.readInt(), c, chr)`；`CANCEL_CHAIR` → `PlayerHandler.CancelChair(slea.readShort(), c, chr)`
- `MaplePacketCreator.showChair(characterid, itemid)` / `cancelChair(id)`

C→S：

```
USE_CHAIR:
writeShort(0x23)
writeInt(itemId)

CANCEL_CHAIR:
writeShort(0x22)
writeShort(id)      // -1 = cancel item chair; otherwise map chair id
```

S→C：

```
SHOW_CHAIR:
writeShort(0xBD)
writeInt(characterId)
writeInt(itemId)    // 0 = clear chair look

CANCEL_CHAIR(id == -1):
writeShort(0xC6)
writeByte(0)

CANCEL_CHAIR(id != -1):
writeShort(0xC6)
writeByte(1)
writeShort(id)
```

備註：舊 Java `UseChair` 另含釣魚地圖、飛行椅 mount buff 與封包修改封鎖分支；MapleForge 目前先移植基礎坐椅子/取消椅子視覺同步，特殊椅子語義待釣魚、mount buff、反作弊系統補齊。

### 玩家道具效果 `USE_ITEMEFFECT` / `CANCEL_ITEM_EFFECT`

來源：

- `recv.properties`：`USE_ITEMEFFECT = 0x2D`、`CANCEL_ITEM_EFFECT = 0x43`
- `send.properties`：`SHOW_ITEM_EFFECT = 0xBA`
- `MapleServerHandler`：`USE_ITEMEFFECT` → `PlayerHandler.UseItemEffect(slea.readInt(), c, chr)`；`CANCEL_ITEM_EFFECT` → `PlayerHandler.CancelItemEffect(slea.readInt(), chr)`
- `MaplePacketCreator.itemEffect(characterid, itemid)`

C→S：

```
USE_ITEMEFFECT:
writeShort(0x2D)
writeInt(itemId)

CANCEL_ITEM_EFFECT:
writeShort(0x43)
writeInt(id)
```

S→C:

```
SHOW_ITEM_EFFECT:
writeShort(0xBA)
writeInt(characterId)
writeInt(itemId)    // MapleForge uses 0 to clear current visual effect; live smoke pending.
```

備註：舊 Java `CancelItemEffect` 透過 `cancelEffect(getItemEffect(-id))` 間接取消效果；MapleForge 目前以 `SHOW_ITEM_EFFECT(itemId=0)` 表示清除視覺效果，需真機 UI smoke 確認。

### 玩家鍵位 `CHANGE_KEYMAP` / `KEYMAP`

來源：

- `recv.properties`：`CHANGE_KEYMAP = 0x7F`
- `send.properties`：`KEYMAP = 0x163`
- `MapleServerHandler`：`CHANGE_KEYMAP` → `PlayerHandler.ChangeKeymap(slea, chr)`
- `PlayerHandler.ChangeKeymap`：一般分支 `tick + numChanges + key/type/action...`；短封包分支為 pet auto pot。
- `MaplePacketCreator.getKeymap(MapleKeyLayout)` / `MapleKeyLayout.writeData`
- `MapleCharacter.saveNewCharToDB`：新角色預設 `array1/array2/array3` keymap。

C→S 一般鍵位變更：

```
writeShort(0x7F)
writeInt(tick)
writeInt(numChanges)
repeat numChanges:
  writeInt(key)
  writeByte(type)
  writeInt(action)
```

C→S 短封包分支（pet auto pot）：

```
writeShort(0x7F)
writeInt(type)      // 1 = HP auto pot, 2 = MP auto pot in Java branch
writeInt(data)      // item id or <=0 clear
```

S→C 登入後送鍵位：

```
writeShort(0x163)
writeByte(0)
repeat key 0..89:
  writeByte(binding.type or 0)
  writeInt(binding.action or 0)
```

備註：MapleForge 目前移植一般 keymap 變更、角色文件持久化、新角色預設 keymap，以及短封包 pet auto-pot 的 HP/MP item id 保存；實際自動補藥施放仍依賴後續寵物系統。`SKILL_MACRO(0x68/0x7A)` 已另拆切片移植。

### 玩家技能宏 `SKILL_MACRO`

來源：

- `recv.properties`：`SKILL_MACRO = 0x68`
- `send.properties`：`SKILL_MACRO = 0x7A`
- `MapleServerHandler`：`SKILL_MACRO` → `PlayerHandler.ChangeSkillMacro(slea, chr)`
- `PlayerHandler.ChangeSkillMacro`：讀取 `num` 組 macro，position 使用 loop index `i`
- `MaplePacketCreator.getMacros(SkillMacro[] macros)`
- `MapleCharacter.sendMacros()`：登入後只要有任一 macro 非 null 才送 `SKILL_MACRO`

C→S 保存技能宏：

```
writeShort(0x68)
writeByte(num)
repeat i in 0..num-1:
  writeMapleAsciiString(name)
  writeByte(shout)
  writeInt(skill1)
  writeInt(skill2)
  writeInt(skill3)
```

S→C 登入後送技能宏：

```
writeShort(0x7A)
writeByte(count)    // non-null macros only
repeat each macro in position order:
  writeMapleAsciiString(name)
  writeByte(shout)
  writeInt(skill1)
  writeInt(skill2)
  writeInt(skill3)
```

備註：MapleForge 目前只保存/送出 macro 設定，不驗證 skill id 是否已學會；舊 Java 此 handler 也只依客戶端送來的 macro 更新角色資料。技能施放、cooldown、buff 語義屬技能/戰鬥系統。

### 角色資訊 `CHAR_INFO_REQUEST` / `CHAR_INFO`

來源：

- `recv.properties`：`CHAR_INFO_REQUEST = 0x5B`
- `send.properties`：`CHAR_INFO = 0x36`
- `MapleServerHandler`：`CHAR_INFO_REQUEST` → `PlayerHandler.CharInfoRequest(slea.readInt(), c, c.getPlayer())`
- `PlayerHandler.CharInfoRequest`：先送 `enableActions()`，再從同地圖找目標角色，存在且權限允許才送 `MaplePacketCreator.charInfo(player, isSelf)`。
- `MaplePacketCreator.charInfo(chr, isSelf)`

C→S：

```
writeShort(0x5B)
writeInt(targetCharacterId)
```

S→C：

```
writeShort(0x36)
writeInt(characterId)
writeByte(level)
writeShort(job)
writeShort(fame)
writeByte(marriageHeart)
writeMapleAsciiString(guildName)
writeMapleAsciiString(allianceName)
writeMapleAsciiString(characterMessage)
writeByte(expression)
writeByte(constellation)
writeByte(blood)
writeByte(month)
writeByte(day)

repeat summoned pets:
  writeByte(summonedSlot)
  writeInt(petItemId)
  writeMapleAsciiString(petName)
  writeByte(petLevel)
  writeShort(closeness)
  writeByte(fullness)
  writeShort(flags)
  writeInt(petEquipItemId)
writeByte(0)        // pet terminator

writeByte(hasMountInfo)
if hasMountInfo:
  writeInt(mountLevel)
  writeInt(mountExp)
  writeInt(mountFatigue)

writeByte(wishlistSize)
repeat wishlistSize:
  writeInt(itemId)

// MonsterBook.addCharInfoPacket
writeInt(bookLevel)
writeInt(normalCards)
writeInt(specialCards)
writeInt(totalCards)
writeInt(coverMobId)

writeInt(equippedMedalItemId)
writeShort(viewableMedalQuestCount)
repeat viewableMedalQuestCount:
  writeShort(questId)
```

備註：MapleForge 目前移植基礎入口與欄位骨架：等級/職業/人氣/公會名稱/個人訊息/表情/生日星座欄位會填入，Marriage/Pet/Mount/Wishlist/MonsterBook/Medal 等尚未移植系統以空值/預設值保留欄位。未加入公會/聯盟的舊 Java 中文預設文案暫不寫入，因目前 `PacketWriter.WriteMapleString` 尚未抽出 TMS 字碼頁處理，直接寫中文會造成單位元組字串錯誤。

### 角色資訊更新 `UPDATE_CHAR_INFO`

來源：

- `recv.properties`：`UPDATE_CHAR_INFO = 0x97`
- `MapleServerHandler`：`UPDATE_CHAR_INFO` → `PlayersHandler.UpdateCharInfo(slea, c, c.getPlayer())`
- `PlayersHandler.UpdateCharInfo`：空封包送 `enableActions()`；type 0 更新 `charmessage`，type 1 更新 `expression`，type 2 更新 `blood/month/day/constellation`。

C→S：

```
writeShort(0x97)

// type 0: character message
writeByte(0)
writeMapleAsciiString(message)

// type 1: profile expression
writeByte(1)
writeByte(expression)

// type 2: birthday / constellation
writeByte(2)
writeByte(blood)
writeByte(month)
writeByte(day)
writeByte(constellation)
```

備註：MapleForge 將這些欄位持久化在 `Character` 文件中，並由 `CHAR_INFO` 回填。空封包沿用 Java 行為送 `EnableActions`。

### 怪物書封面 `MONSTER_BOOK_COVER` / `MONSTERBOOK_CHANGE_COVER`

來源：

- `recv.properties`：`MONSTER_BOOK_COVER = 0x32`
- `send.properties`：`MONSTERBOOK_CHANGE_COVER = 0x4E`
- `MapleServerHandler`：`MONSTER_BOOK_COVER` → `PlayerHandler.ChangeMonsterBookCover(slea.readInt(), c, c.getPlayer())`
- `PlayerHandler.ChangeMonsterBookCover`：`bookid == 0 || GameConstants.isMonsterCard(bookid)` 時更新角色封面並送 `MonsterBookPacket.changeCover(bookid)`。
- `PacketHelper.addMonsterBookInfo`：登入 `SET_FIELD` 的 monster book info 先寫 `chr.getMonsterBookCover()`，再寫 byte `0` 與卡片清單。

C→S：

```
writeShort(0x32)
writeInt(cardItemId)    // 0 = clear; monster card item id range is 238xxxx
```

S→C：

```
writeShort(0x4E)
writeInt(cardItemId)
```

SET_FIELD MonsterBookInfo：

```
writeInt(monsterBookCoverCardItemId)
writeByte(0)
writeShort(cardEntryCount)
repeat cardEntryCount:
  writeShort(cardShortId)
  writeByte(cardLevel)
```

備註：MapleForge 目前只移植 cover 欄位與 change-cover 封包；完整 MonsterBook cards 尚未移植，因此暫不檢查角色是否實際持有該卡。角色資訊卡尾段需要 `MapleItemInformationProvider.getCardMobId(cardItemId)` 的 card→mobId 對照，尚未有 catalog，仍保守寫 0。

### 黑板 `CLOSE_CHALKBOARD` / `CHALKBOARD`

來源：

- `recv.properties`：`CLOSE_CHALKBOARD = 0x2B`
- `send.properties`：`CHALKBOARD = 0x9C`
- `MapleServerHandler`：`CLOSE_CHALKBOARD` → `c.getPlayer().setChalkboard(null)`
- `MTSCSPacket.useChalkboard(charid, msg)`

C→S：

```
writeShort(0x2B)
```

S→C：

```
writeShort(0x9C)
writeInt(characterId)
writeByte(hasMessage)
if hasMessage:
  writeMapleAsciiString(message)
```

備註：MapleForge 目前移植關閉/清除分支；開黑板需後續 `USE_CASH_ITEM` 5370000/5370001 分支。

### 傳送石 `TROCK_ADD_MAP` / `MAP_TRANSFER_RESULT`

來源：

- `recv.properties`：`TROCK_ADD_MAP = 0x60`
- `send.properties`：`MAP_TRANSFER_RESULT = 0x27`
- `MapleServerHandler`：`TROCK_ADD_MAP` → `PlayerHandler.TrockAddMap(slea, c, c.getPlayer())`
- `MTSCSPacket.getTrockRefresh(chr, vip, delete)`
- `PacketHelper.addRocksInfo`

C→S：

```
writeShort(0x60)
writeByte(addrem)   // 0 = delete, 1 = add current map
writeByte(vip)      // 1 = VIP rock, otherwise regular rock
if addrem == 0:
  writeInt(mapId)
```

S→C refresh：

```
writeShort(0x27)
writeByte(delete ? 2 : 3)
writeByte(vip)
if vip == 1:
  repeat 10: writeInt(vipRockMapId)
else:
  repeat 5: writeInt(regularRockMapId)
```

SET_FIELD AddRocksInfo：

```
repeat 5:  writeInt(regularRockMapId)
repeat 10: writeInt(vipRockMapId)
```

備註：空格對齊 Java 使用 `999999999`。MapleForge 目前保留 Java 明寫限制：一般石不可存 `>197010000` 或 `180000000`，VIP 不可存 `180000000`；`FieldLimitType.VipRock` 尚未移植。

### 玩家受傷 `TAKE_DAMAGE` / `DAMAGE_PLAYER`

來源：

- `recv.properties`：`TAKE_DAMAGE = 0x29`
- `send.properties`：`DAMAGE_PLAYER = 0xB8`
- `MapleServerHandler`：`TAKE_DAMAGE` → `PlayerHandler.TakeDamage(slea, c, chr)`
- `MaplePacketCreator.damagePlayer(...)`

C→S：

```
writeShort(0x29)
writeInt(tick)
writeByte(type)       // signed byte; -2/-3/-4 = map damage
writeByte(element)
writeInt(damage)
if type not in [-2, -3, -4]:
  writeInt(monsterIdFrom)
  writeInt(objectId)
  writeByte(direction)
```

S→C 廣播給同地圖其他玩家：

```
writeShort(0xB8)
writeInt(characterId)
writeByte(type)
writeInt(damage)
writeInt(monsterIdFrom)
writeByte(direction)
writeShort(0)         // no reflect branch
writeInt(damage)
if fake > 0:
  writeInt(fake)
```

備註：舊 Java 會拒絕 `< -1` 或 `> 60000` 的傷害。MapleForge 目前移植無反傷主幹：扣 HP、回 `UPDATE_STATS(HP)` 並廣播受傷動畫；fake dodge、反傷、Power Guard、buff 交互待技能/buff 系統補齊。

### 丟楓幣 `MESO_DROP`

來源：

- `recv.properties`：`MESO_DROP = 0x58`
- `MapleServerHandler`：`MESO_DROP` → `PlayerHandler.DropMeso(meso, chr)`
- `PlayerHandler.DropMeso`：限制 10~50000，餘額不足或未存活時 enable actions，不改資料。

C→S：

```
writeShort(0x58)
writeInt(tick)
writeInt(meso)
```

S→C：

```
UPDATE_STATS(MESO)
DROP_ITEM_FROM_MAPOBJECT
```

備註：MapleForge 透過既有 `MapDrop.ForMeso` 與 `DropItemFromMapObject` 廣播 player-drop 型楓幣掉落；地形落點與 pickup 權限時間仍待後續校準。

### 同圖內部傳點 `USE_INNER_PORTAL` / `CURRENT_MAP_WARP`

來源：

- `recv.properties`：`USE_INNER_PORTAL = 0x5F`
- `send.properties`：`CURRENT_MAP_WARP = 0xC8`
- `MapleServerHandler`：先 skip 1 byte，再 `PlayerHandler.InnerPortal(slea, c, chr)`
- `MaplePacketCreator.instantMapWarp(byte portal)`

C→S：

```
writeShort(0x5F)
writeByte(unknown)
writeMapleAsciiString(portalName)
writeShort(toX)
writeShort(toY)
```

S→C：

```
writeShort(0xC8)
writeByte(0)
writeByte(portalId)
```

備註：MapleForge 目前驗證當前地圖 portal 名稱存在後更新玩家 runtime 位置並回 warp 封包；距離容錯/反作弊與 `CHANGE_MAP_SPECIAL(0x5E)` script portal 尚未移植。

### 背包聚集/排序 `ITEM_GATHER` / `ITEM_SORT`

來源：

- `recv.properties`：`ITEM_SORT = 0x3F`、`ITEM_GATHER = 0x40`
- `SendPacketOpcode.java`：`GATHER_ITEM_RESULT = 0x32`、`SORT_ITEM_RESULT = 0x33`
- `InventoryHandler.ItemSort` / `InventoryHandler.ItemGather`
- `MaplePacketCreator.finishedSort(type)` / `finishedGather(mode)`

C→S：

```
ITEM_SORT:
writeShort(0x3F)
writeInt(tick)
writeByte(inventoryType)

ITEM_GATHER:
writeShort(0x40)
writeInt(tick)
writeByte(inventoryType)
```

S→C：

```
finishedSort(type):
writeShort(0x32)     // Java enum GATHER_ITEM_RESULT
writeByte(1)
writeByte(type)

finishedGather(type):
writeShort(0x33)     // Java enum SORT_ITEM_RESULT
writeByte(1)
writeByte(type)
```

備註：舊 Java 的方法名稱與 result enum 名稱交錯，MapleForge 以 `MaplePacketCreator` 實際寫出的 opcode 為準。Core 目前以 itemId 排序並從 slot 1 重排，cash item / pet id 特殊比較待完整 item metadata 與寵物系統補齊。

---
## Batch-5 中央整合 opcode 註記（2026-06-12）

本批已接 active dispatch：

- `PLAYER_INTERACTION(0x73)`：目前只接 trade branch；player shop / hired merchant / omok / match-card 留在 router TODO。
- `DUEY_ACTION(0x3B)` / `DUEY(0x155)`：Duey 宅配主流程。
- `BBS_OPERATION(0x94)` / `BBS_OPERATION(0x68)`：公會留言板。
- `RING_ACTION(0x81)`，send `MARRIAGE_REQUEST(0x41)` / `MARRIAGE_RESULT(0x42)` / `MARRIAGE_UPDATE(0x62)` / `SHOW_FOREIGN_EFFECT(0xBF)`：戒指/求婚 MVP；ring effect 仍是 candidate，待真機驗。
- `DAMAGE_REACTOR(0xC9)` / `TOUCH_REACTOR(0xCA)`，send `REACTOR_HIT(0x113)` / `REACTOR_SPAWN(0x115)` / `REACTOR_DESTROY(0x116)`：reactor spawn/hit/touch MVP。
- `OWL(0x3C)` / `OWL_WARP(0x3D)` / `USE_OWL_MINERVA(0x4D)`，send `SHOP_SCANNER_RESULT(0x3F)` / `SHOP_LINK_RESULT(0x40)` / `REPAIR_WINDOW(0xD5)`：Owl active opcodes 已接；repair send constant 保留。
- `SOLOMON(0x9B)` / `GACH_EXP(0x9C)` / `TRANSFORM_PLAYER(0xA0)` / `XMAS_SURPRISE(0xA2)`，send `XMAS_SURPRISE(0x161)`：特殊增益/獎勵道具 MVP。

本批保留但不接 dispatch：

- `FOLLOW_REQUEST` / `FOLLOW_REPLY`：此版 Java `recv.properties` 註解掉；`FOLLOW_REPLY` 候選 `0x7A` 撞 active `BUDDYLIST_MODIFY`。
- `REPAIR` / `REPAIR_ALL`：此版 Java `recv.properties` 註解掉；舊註解 `0x73`/`0x72` 撞 active `PLAYER_INTERACTION`/`MESSENGER`。
- Owl cash-item `5230000` route：master 尚無既有 `USE_CASH_ITEM` inventory 路由可乾淨接入，暫列 TODO。

---
*待補（M1 後）：getAuthSuccessRequest、角色列表、移動等封包結構（M2/M3 再萃取）。*
