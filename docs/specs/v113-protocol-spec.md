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

### 換頻道 `CHANGE_CHANNEL`

來源：

- `recv.properties`：`CHANGE_CHANNEL = 0x1F`
- `send.properties`：`CHANGE_CHANNEL = 0x08`
- `MapleServerHandler`：`CHANGE_CHANNEL` → `InterServerHandler.ChangeChannel(slea, c, c.getPlayer())`
- `InterServerHandler.ChangeChannel`：讀 `slea.readByte() + 1`
- `MapleCharacter.changeChannel`：保存角色後送 `MaplePacketCreator.getChannelChange(toch.getIP(), toch.getPort())`
- `MaplePacketCreator.getChannelChange(byte[] ip, int port)`

C→S：

```
writeShort(0x1F)
writeByte(targetChannel)    // client uses 0-based channel index; Java converts to 1-based.
```

S→C：

```
writeShort(0x08)
writeByte(1)                // success
write(ip[4])
writeShort(port)
```

備註：MapleForge 目前採單進程 MVP，login+channel 同 process，沒有獨立 channel server。收到 `CHANGE_CHANNEL` 後忽略目標 channel，先 `FlushInventory` 並保存角色文件，再回設定的同一個 `ChannelIp/ChannelPort`（預設 `127.0.0.1:8585`）；client 收包後會斷線重連，既有 `V113ChannelConnectionHandler` finally 負責 deregister map/registry/trade 與最後持久化。buff transfer、跨 channel server handoff 與 Java 的多 channel storage 語義留待正式多實例/多頻道設計。

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

### 傳送門 `USE_DOOR`

來源：

- `recv.properties`：`USE_DOOR = 0x7D`
- `PlayersHandler.UseDoor`：讀 `ownerId` 與 mode byte，mode byte 為 `0` 時 Java 傳入 `toTown=true`
- `MapleDoor.warp`：只允許 owner 或同 party 成員使用；`toTown=true` 換到 town portal，否則換回 target map 的 target position
- `MaplePacketCreator.spawnDoor/removeDoor/spawnPortal/partyPortal`

C→S：

```
writeShort(0x7D)
writeInt(ownerId)
writeByte(mode)     // 0 = target-to-town/backwarp, 1 = town-to-target
```

S→C door spawn/remove（本輪先提供 encoder，creation/cleanup 接線待 SPECIAL_MOVE）：

```
SPAWN_DOOR:
writeShort(0x10E)
writeByte(town ? 1 : 0)
writeInt(ownerId)
writeShort(x)
writeShort(y)

REMOVE_DOOR:
writeShort(0x10F)
writeByte(1)
writeInt(ownerId)
```

備註：MapleForge 本輪新增 runtime-only `Door` 與 `DoorService`，以 ownerId 在 town/target 兩側地圖查找同一扇門並回傳 warp map/position decision；真正換圖仍由未來 channel dispatch 接既有 `WarpAsync`。`SPECIAL_MOVE(0x55)` Mystic Door 建立尚未移植。證據層級為 Java source + Core/Application/Adapters 編譯 + Adapters handler tests；真 v113 client smoke 待後續接線後驗證。

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

### 一般消耗補藥 `USE_ITEM`

來源：

- `recv.properties`：`USE_ITEM = 0x42`
- `MapleServerHandler`：`USE_ITEM` → `InventoryHandler.UseItem(slea, c, c.getPlayer())`
- `InventoryHandler.UseItem`：驗活著、Use 欄 slot/itemId/quantity，套用 `MapleItemInformationProvider.getItemEffect(itemId).applyTo(chr)`，成功後移除 1 個 Use 道具。

C→S：

```
writeShort(0x42)
writeInt(tick)
writeShort(slot)
writeInt(itemId)
```

S→C：

```
MODIFY_INVENTORY_ITEM    // 消耗 Use 欄 slot 數量或移除
UPDATE_STATS             // HP/MP final value
ENABLE_ACTIONS
```

備註：MapleForge 目前由 `UseItemService` 承載版本無關補 HP/MP 語義，`IItemEffectCatalog` 提供道具效果資料；啟動預設使用 `HardcodedItemEffectCatalog`，已含常見 v113 補藥與 `2000000..2099999` unknown potion 最小 HP 恢復 fallback。地圖 potion-use field limit、consume cooldown、disease 禁止補藥等 Java 分支尚未移植；證據層級為 Java source + Core/Application/Adapters 單元測試，真 v113 client GUI smoke 待 #12 批量驗證。

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

### Batch-5 item-use 收官追加（2026-06-17）

來源：

- `recv.properties`：`USE_SUMMON_BAG = 0x45`、`USE_MOUNT_FOOD = 0x47`、`USE_CATCH_ITEM = 0x4B`、`USE_RETURN_SCROLL = 0x4F`
- `send.properties` / `MaplePacketCreator`：`MODIFY_INVENTORY_ITEM = 0x1B`、`SET_TAMING_MOB_INFO = 0x2D`、`CATCH_MONSTER = 0xF5`
- Java 來源：`InventoryHandler.UseSummonBag` / `UseMountFood` / `UseCatchItem` / `UseReturnScroll`

C→S 共用道具使用 layout：

```
writeShort(opcode)
writeInt(tick)
writeShort(slot)
writeInt(itemId)
```

`USE_CATCH_ITEM` 額外讀：

```
writeInt(mobObjectId)
```

S→C：

```
SET_TAMING_MOB_INFO:
writeShort(0x2D)
writeInt(characterId)
writeInt(mountLevel)
writeInt(mountExp)
writeInt(mountFatigue)
writeByte(levelUp)

CATCH_MONSTER:
writeShort(0xF5)
writeInt(monsterId)
writeInt(itemId)
writeByte(success)
```

備註：MapleForge 目前以 result intent 接中央流程：召喚袋產生 mob spawn intent、返回卷軸走既有 `WarpAsync`、捕捉成功移除怪物並給道具、坐騎飼料扣道具並更新 mount。所有失敗分支需送 `EnableActions` 避免客戶端卡住。證據層級為 Java source + headless/unit tests；真 v113 client GUI smoke 待 #12 批量驗證。

### 升級卷軸 `USE_UPGRADE_SCROLL`

來源：

- `recv.properties`：`USE_UPGRADE_SCROLL = 0x50`
- `send.properties` / `SendPacketOpcode.java`：`SHOW_SCROLL_EFFECT = 0x9F`
- Java 來源：`MapleServerHandler` dispatch、`InventoryHandler.UseUpgradeScroll`、`MaplePacketCreator.getScrollEffect`

C→S：

```
writeShort(0x50)
writeInt(tick)
writeShort(scrollSlot)   // USE inventory slot
writeShort(equipSlot)    // <0 equipped slot, >=0 EQUIP bag slot
writeShort(flags)        // flags & 2 = white scroll requested
```

S→C scroll effect（MapleForge MVP）：

```
writeShort(0x9F)
writeInt(characterId)
writeByte(success ? 1 : 0)
writeByte(curse ? 1 : 0)
writeShort(0)            // legendarySpirit；MVP 固定 0
writeByte(whiteScroll)   // task MVP uses consumed white-scroll flag; Java source writes trailing 0 ("pam's song?")
```

S→C inventory：

```
MODIFY_INVENTORY_ITEM(0x1B) for scroll consumption
MODIFY_INVENTORY_ITEM(0x1B) for white scroll consumption when item 2340000 is consumed
MODIFY_INVENTORY_ITEM(0x1B) remove+add equip update, or remove equip on curse
```

語義：

- `Maple.Core.Items.ScrollEffect` / `IScrollCatalog` 承載版本無關卷軸效果資料。
- `ScrollService.UseScroll` 在 Application 層執行 deterministic roll：`randomSeed % 100 < successRate`。
- 成功：套用 stat bonus，`UpgradeSlots--`，`Level++`。
- 失敗：無白衣保護時 `UpgradeSlots--`；白衣卷軸已消耗時不扣 slot。
- 詛咒：cursed scroll 失敗時移除目標裝備。
- 無 upgrade slots：本 MVP 回 `Fail`，不改裝備數值/slot，但仍消耗卷軸以符合本任務明定的 "always consume scroll" 範圍。
- 目前 catalog 為 hardcoded MVP：2040200/201/202、2044000/001/002 與 204xxxx catch-all；完整 WZ scroll metadata 待後續接入。
- 證據層級：Java source + Application/Adapters 單元測試；真 v113 client scroll UI/effect smoke 待 #12 批量驗證。

---
## P1 社交追加 opcode 註記（2026-06-18）

### 留言 `NOTE_ACTION` / `SHOW_NOTES`

來源：

- `recv.properties`：`NOTE_ACTION = 0x7B`
- `send.properties`：`SHOW_NOTES = 0x26`
- Java 來源：`PlayersHandler.Note`、`MapleCharacterUtil.sendNote`、`MapleCharacter.showNote/deleteNote`、`MTSCSPacket.showNotes`

C→S send note：

```
writeShort(0x7B)
writeByte(0)
writeMapleAsciiString(receiverName)
writeMapleAsciiString(message)
writeByte(fame > 0)
writeInt(0)
writeLong(cashId)
```

C→S delete notes：

```
writeShort(0x7B)
writeByte(1)
writeByte(count)
writeShort(0)
repeat count:
  writeInt(noteId)
  writeByte(gainFame > 0)
```

S→C show notes：

```
writeShort(0x26)
writeByte(3)
writeByte(count)
repeat count:
  writeInt(noteId)
  writeMapleAsciiString(senderName)
  writeMapleAsciiString(message)
  writeLong(PacketHelper.getKoreanTimestamp(timestampMillis))
  writeByte(fame)
```

備註：Java send branch 會用 cash inventory 驗證 cash note item/gift source/是否已寄過；MapleForge MVP 依任務範圍不做 cash item 驗證，只解析 cashId 並建立 note。刪除時 Application 會以被刪 note 的 `Fame` 與 client gain flag 共同決定 fame delta，保留 Java 防濫用語義。證據層級為 Java source + Adapters 單元測試；真 v113 client note UI smoke 待後續 dispatch/DI 接線後驗證。

### 家族 Family 系統 9 opcode

來源：

- `recv.properties`：`REQUEST_FAMILY=0x88`、`OPEN_FAMILY=0x89`、`FAMILY_OPERATION=0x8A`、`DELETE_JUNIOR=0x8B`、`DELETE_SENIOR=0x8C`、`ACCEPT_FAMILY=0x8D`、`USE_FAMILY=0x8E`、`FAMILY_PRECEPT=0x8F`、`FAMILY_SUMMON=0x90`
- `send.properties` / `SendPacketOpcode.java`：`FAMILY_CHART_RESULT=0x56`、`FAMILY_INFO_RESULT=0x57`、`FAMILY_RESULT=0x58`、`FAMILY_JOIN_REQUEST=0x59`、`FAMILY_JOIN_REQUEST_RESULT/FAMILY_JUNIOR=0x5A`、`FAMILY_JOIN_ACCEPTED=0x5B`、`FAMILY_PRIVILEGE_LIST=0x5C`、`FAMILY_FAMOUS_POINT_INC_RESULT=0x5D`、`FAMILY_NOTIFY_LOGIN_OR_LOGOUT=0x5E`、`FAMILY_SET_PRIVILEGE=0x5F`、`FAMILY_SUMMON_REQUEST=0x60`
- Java 來源：`FamilyHandler.java`、`MapleFamily.java`、`MapleFamilyCharacter.java`、`MapleFamilyBuff.java`、`FamilyPacket.java`

C→S：

```
REQUEST_FAMILY:
writeShort(0x88)
writeMapleAsciiString(targetName)

OPEN_FAMILY:
writeShort(0x89)

FAMILY_OPERATION:
writeShort(0x8A)
writeMapleAsciiString(targetName)

DELETE_JUNIOR:
writeShort(0x8B)
writeInt(juniorCharacterId)

DELETE_SENIOR:
writeShort(0x8C)

ACCEPT_FAMILY:
writeShort(0x8D)
writeInt(inviterCharacterId)
writeMapleAsciiString(inviterName)
writeByte(accepted)

USE_FAMILY:
writeShort(0x8E)
writeInt(buffType)
if buffType == 0 or 1:
  writeMapleAsciiString(targetName)

FAMILY_PRECEPT:
writeShort(0x8F)
writeMapleAsciiString(notice)

FAMILY_SUMMON:
writeShort(0x90)
writeMapleAsciiString(summonerName)
writeByte(accepted)
```

S→C 主布局：

```
FAMILY_JOIN_REQUEST(0x59):
writeInt(inviterCharacterId)
writeMapleAsciiString(inviterName)

FAMILY_JUNIOR / FAMILY_JOIN_REQUEST_RESULT(0x5A):
writeByte(accepted)
writeMapleAsciiString(acceptedCharacterName)

FAMILY_JOIN_ACCEPTED(0x5B):
writeMapleAsciiString(seniorName)

FAMILY_FAMOUS_POINT_INC_RESULT(0x5D):
writeInt(repDelta)
writeInt(0)

FAMILY_SUMMON_REQUEST(0x60):
writeMapleAsciiString(summonerName)
writeMapleAsciiString(mapName)
```

`FAMILY_INFO_RESULT(0x57)` 對齊 Java `FamilyPacket.getFamilyInfo`：`currentRep/totalRep/todayRep`、junior count、leader id/name、notice、used buff list。`FAMILY_CHART_RESULT(0x56)` 對齊 Java `getFamilyPedigree/addFamilyCharInfo`：角色 id、senior id、job、level、online flag、rep、channel/time、name、descendant summary、used buff list。MapleForge 目前由 `FamilyService` 產生 protocol-neutral DTO，`V113FamilyPackets` 負責 byte layout。

語義：

- Core 新增 `Family` / `FamilyMember` / `FamilyBuff` / `IFamilyRepository`，Application `FamilyService` 承載 invite、accept、delete junior/senior、rep spend、notice、tree split、pending summon。
- Java `USE_FAMILY` 與 `FAMILY_SUMMON` 的 GM-only gate 是舊服停用開關；MapleForge 依本輪任務移除，家族 buff/召喚對一般玩家可用。
- `FamilyBuff` 目錄提供 type `0..10`：0 teleport、1 summon、2/3 50% drop/exp 15m、4 pedigree 100% drop+exp 30m、5..10 100% self/party drop/exp。
- 本輪不修改 `V113ChannelConnectionHandler.cs` / `V113ChannelOpcodes.cs`；dispatch、實際 warp/buff application、map name lookup 與真 v113 client smoke 待後續接線後驗證。
- 證據層級：Java source + Core/Application/Adapters build + Adapters family handler tests；S2C pedigree/info layout 仍為 Java-source candidate，尚未 live-client verified。

---
## P2 Batch 1A trivial recv opcode stubs（2026-06-18）

來源：

- `recv.properties`：`CLIENT_ERROR=0x0C`、`STRANGE_DATA=0x7FFF`、`CLIENT_FEEDBACK=0x0F`、`CLIENT_LOGOUT=0x1A`、`SHOW_EXP_CHAIR=0x24`、`CP_UserCalcDamageStatSetRequest=0x66`、`CYGNUS_SUMMON=0x91`、`GAME_POLL=0xA3`、`SNOWBALL=0xCD`、`LEFT_KNOCK_BACK=0xCE`、`CP_BeansUpdate=0xE1`、`MAPLETV=0x10A`
- Java 來源：`MapleServerHandler.java` dispatch；MapleTV cash-item message path also notes no MapleTV broadcast support.

Login C→S:

```
CLIENT_ERROR:
writeShort(0x0C)
writeBytes(errorData)    // MapleForge logs remaining bytes as warning-level error data.

CLIENT_FEEDBACK:
writeShort(0x0F)
writeBytes(data)         // MapleForge logs at information level.

CLIENT_LOGOUT:
writeShort(0x1A)
```

Channel C→S handling:

```
STRANGE_DATA(0x7FFF): no-op
CP_UserCalcDamageStatSetRequest(0x66): no-op
SHOW_EXP_CHAIR(0x24): readInt(); ENABLE_ACTIONS
CYGNUS_SUMMON(0x91): ENABLE_ACTIONS stub; NPC script start deferred
SNOWBALL(0xCD): ENABLE_ACTIONS stub; real hit logic belongs to attack flow/event system
LEFT_KNOCK_BACK(0xCE): ENABLE_ACTIONS stub
GAME_POLL(0xA3): ENABLE_ACTIONS stub
MAPLETV(0x10A): ENABLE_ACTIONS stub; no MapleTV broadcast system yet
CP_BeansUpdate(0xE1): ENABLE_ACTIONS stub; bean system not implemented yet
```

備註：本批只把已知 trivial opcode 從未處理狀態降噪/解除 client action lock，不建立新 domain service。證據層級為 Java source + `Maple.Host.Shared` build + `Maple.Adapters.V113.Tests` 299 passed / 1 skipped；真 v113 client smoke 未跑。

---
## P2 Batch 1B 簡易 handler opcode 註記（2026-06-18）

來源：

- `MobHandler.java`：`handleDisplayNode`、`handleMonsterBomb`、`handleFriendlyDamage`、`HypnotizeDmg`
- `PlayerHandler.java`：`UseItemEffect` dispatch sibling、`closeRangeAttack(..., true)`、`AranCombo`
- `CashShopOperation.java`：`sendCashShopUpdate`

C→S：

```
PASSIVE_ENERGY:
writeShort(0x28)
// same close-range attack body as CLOSE_RANGE_ATTACK(0x25)

WHEEL_OF_FORTUNE:
writeShort(0x2E)
writeInt(itemId)    // routed to existing USE_ITEMEFFECT MVP handler

ARAN_COMBO:
writeShort(0x92)
writeInt(timestamp) // MapleForge MVP reads when present, then EnableActions

FRIENDLY_DAMAGE:
writeShort(0xBA)
writeInt(mobOid1)
writeInt(mobOid2)
// remaining Java damage details not modeled yet

MONSTER_BOMB:
writeShort(0xBB)
writeInt(mobOid)    // MVP validates Shadower jobs 421/422, then EnableActions

HYPNOTIZE_DMG:
writeShort(0xBC)
writeInt(fromMobOid)
writeInt(toMobOid)
writeInt(damage)

DISPLAY_NODE:
writeShort(0xBE)
writeInt(mobOid)

CS_UPDATE:
writeShort(0xE5)
```

語義：

- `PASSIVE_ENERGY(0x28)` 走既有 close-range attack parser/combat path；Java 的 `energy=true` 細節尚未分離建模。
- `WHEEL_OF_FORTUNE(0x2E)` 走既有 `USE_ITEMEFFECT(0x2D)` handler，保留持有檢查與視覺廣播語義。
- `FRIENDLY_DAMAGE`、`MONSTER_BOMB`、`HYPNOTIZE_DMG`、`DISPLAY_NODE`、`ARAN_COMBO` 目前是讀取指定欄位後 `EnableActions` 的 MVP，不做 mob kill、node packet、combo state/buff application。
- `CS_UPDATE(0xE5)` 目前送既有 cash balances 與 Cash inventory snapshot；Java 的 gifts/wishlist refresh 尚無 MapleForge model/encoder。
- 證據層級：Java source + Host.Shared build + Adapters.V113 targeted tests；真 v113 client UI/cash-shop/combat smoke 待後續批量驗證。

---
## P2 Batch 2A Event Systems MVP stubs（2026-06-18） / 三 stub 升級（2026-06-18）

來源：

- `PlayersHandler.java`：`hitCoconut()`；`server/events/MapleCoconut.java`
- `NPCHandler.java`：`RPSGame()`；`client/RockPaperScissors.java`
- `BeanGame.java`：`BeansGameAction()`
- `recv.properties`：`RPS_GAME=0x80`、`COCONUT=0xCF`、`CP_BeansGameAction=0xE0`

C→S：

```
RPS_GAME:
writeShort(0x80)
writeByte(mode)      // 0=start, 1=answer, 2=timeout, 3=continue, 4=leave in Java
if mode == 1:
  writeByte(choice)  // 0=rock, 1=scissors, 2=paper in MapleForge Core enum

COCONUT:
writeShort(0xCF)
writeShort(coconutId)

CP_BeansGameAction:
writeShort(0xE0)
writeByte(subType)   // MapleForge MVP supports task values 0,4,6,8,0x0B,0x0D,0x0E,0x0F; Java low aliases 1/2/3/5/7 tolerated.
if subType == 0x0E:
  writeByte(powerOrUnknown) optional
  writeByte(count) optional, defaults to 1
```

S→C（Java-source candidate；未經真 client event smoke，不升 golden truth）：

```
RPS_GAME = 0x144:
writeShort(0x144)
writeByte(mode)
if mode == 6:
  writeInt(currentMeso)       // not enough mesos candidate
if mode == 8:
  writeInt(9209002)           // RPS NPC id, Java getRPSMode special case
if mode == 11:
  writeByte(serverChoice)
  writeByte(answer)           // win/tie count, 0xFF lose

HIT_COCONUT = 0x11B:
writeShort(0x11B)
if spawn:
  writeByte(0)
  writeInt(0x80)
else:
  writeInt(coconutId)
  writeByte(type)             // 1=stopped/hit, 2=bomb, 3=fall

COCONUT_SCORE = 0x11C:
writeShort(0x11C)
writeShort(mapleScore)
writeShort(storyScore)

UPDATE_BEANS = 0x6A:
writeShort(0x6A)
writeInt(characterId)
writeInt(beans)
writeInt(0)

LP_BeanGameShow = 0x153:
writeShort(0x153)
writeInt(beans)

LP_BeanGameShoot = 0x154:
writeShort(0x154)
writeByte(action)             // 3 light, 5 reward, 6 exit in Java BeansPacket
...
```

語義：

- 2026-06-18 stub 升級已新增 Core models：
  - `Maple.Core.MiniGames.RpsSession` / `RpsChoice` / `RpsResult`：start、play、tie retry、win continue/cashout、timeout/end；entry fee 1000 meso 由 adapter handler 在 start 扣款。
  - `Maple.Core.Events.CoconutEvent`：map-scoped coconut states、running flag、Maple/Story scores；MVP hit roll 為 40% stopped、20% bomb、其餘 fall+score。
  - `Maple.Core.MiniGames.BeansGameSession` + `Character.Beans`：start cost 1 bean、shoot deducts count、runtime light/reward flags。
- `Maple.Application.Events.CoconutEventService` 暫以 mapId lazy 建立 running MVP event，尚未接完整 event lifecycle/start/end timer。
- `RPS_GAME`、`COCONUT`、`CP_BeansGameAction` dispatch 已不再只是 `EnableActions`；會依 Core 狀態回 S2C packet、扣/加 meso 或 beans、必要時廣播同地圖。
- 保留邊界：Coconut 完整活動排程/進退場/team assignment、RPS item reward/world notice、Beans 完整中獎節奏與跑馬燈 reward gate 尚未 live 驗證。
- 證據層級：Java source map + Core/Adapters focused tests + `Maple.Host.Shared` build；真 v113 client event smoke 未跑，S2C layout 目前為 Java-source candidate。

---
## P2 Batch 2B misc medium opcode stubs（2026-06-18）

來源：

- `recv.properties`：`USE_SCRIPTED_NPC_ITEM=0x48`、`USE_TELE_ROCK=0x4E`、`CP_UserThrowGrenade=0x67`、`MOB_NODE=0xBD`、`QUEST_ITEM=0x10C`
- Java dispatch：`MapleServerHandler.java` routes `USE_SCRIPTED_NPC_ITEM` → `InventoryHandler.UseScriptedNPCItem`、`CP_UserThrowGrenade` → `PlayerHandler.ThrowGrenade`、`MOB_NODE` → `MobHandler.handleMobNode`
- Java handlers：`MobHandler.handleMobNode`、`InventoryHandler.UseScriptedNPCItem`、`InventoryHandler.UseTeleRock`、`PlayerHandler.ThrowGrenade`

C→S MVP layouts:

```
MOB_NODE:
writeShort(0xBD)
writeInt(mobOid)
writeInt(nodeIndex)

USE_TELE_ROCK:
writeShort(0x4E)
writeByte(rockType)
writeByte(mode)                    // 0 = map id, 1 = target character name (player mode deferred)
if mode == 0:
  writeInt(mapId)
if mode == 1:
  writeMapleAsciiString(charName)

QUEST_ITEM:
writeShort(0x10C)
// no-op in MapleForge MVP

USE_SCRIPTED_NPC_ITEM:
writeShort(0x48)
// Java full packet has leading tick; MapleForge parser also accepts the earlier compact MVP shape without tick.
writeInt(tick)                     // optional compatibility field
writeShort(slot)
writeInt(itemId)

CP_UserThrowGrenade:
writeShort(0x67)
```

語義：

- `MOB_NODE(0xBD)` Java 會更新 escort mob node/talk/stage transition；MapleForge Phase A MVP 讀 `mobOid/nodeIndex`，驗證目前地圖存在該 mob OID，寫 log 後 `EnableActions`；escort node state、talk 與 stage transition deferred。
- `USE_TELE_ROCK(0x4E)` Java full path 依 rock item/rock type 檢查儲存地圖、同大陸、FieldLimit/EventInstance，並可傳送到玩家；MapleForge Phase A MVP 支援 map mode：讀 `rockType/mode/mapId`，用 `MapService.MapExists` 驗 map，成功送 `MAP_TRANSFER_RESULT` MVP success byte `0` 後走既有 `WarpAsync`，失敗送 failure byte `1` + `EnableActions`。player-name mode、item consumption、field limit 與 stored-rock 權限 deferred。此 use-result S2C shape 為 MapleForge candidate，尚未 live-client verified；Java 僅有 saved-map refresh helper。
- `QUEST_ITEM(0x10C)` 此 v113 Java enum 註解為 header → questid → open/close，server dispatch 未接；MapleForge MVP no-op。
- `USE_SCRIPTED_NPC_ITEM(0x48)` Java 實作含 leading tick、slot、itemId，並會依 243xxxx 等道具啟動 NPC script/給道具/warp；MapleForge Phase A MVP 驗 Use 背包 slot/itemId，無 scripted item → NPC mapping 時先消耗 1 個道具、送 `MODIFY_INVENTORY_ITEM` quantity update + `EnableActions`。完整 scripted item binding / NPC script start deferred。
- `CP_UserThrowGrenade(0x67)` Java handler 明確未處理；MapleForge MVP 送 `EnableActions` 避免 client action lock。
- 證據層級：Java source + `Maple.Host.Shared` build 0 warning/0 error + `Maple.Adapters.V113.Tests` 313 passed / 1 skipped + `Maple.Core.Tests` 98 passed；真 v113 client teleport/scripted-item/escort smoke 待後續批量驗證。

---
## P2 Batch 2C CashShop + AntiMacro simple opcode stubs（2026-06-18）

來源：

- `recv.properties`：`CP_UserOldAntiMacroQuestionResult=0x63`、`ITEM_UNLOCK=0x95`、`COUPON_CODE=0xE7`
- Java dispatch：`MapleServerHandler.java` routes old anti-macro answer to `PlayersHandler.OldAntiMacroQuestion`、`ITEM_UNLOCK` to `PlayersHandler.UnlockItem`、`COUPON_CODE` to `CashShopOperation.CouponCode`
- Java handlers：`PlayersHandler.OldAntiMacroQuestion`、`PlayersHandler.UnlockItem`、`CashShopOperation.CouponCode`

C→S MVP layouts:

```
CP_UserOldAntiMacroQuestionResult:
writeShort(0x63)
writeMapleAsciiString(answer)

ITEM_UNLOCK:
writeShort(0x95)
writeShort(slot)                    // MapleForge compact shape
// Java full packet compatibility:
// writeShort(itemSize)
// writeShort(inventoryType)
// writeShort(slot)

COUPON_CODE:
writeShort(0xE7)
writeShort(unknown)                 // Java dispatch skips 2 bytes before code.
writeMapleAsciiString(code)
```

語義：

- `CP_UserOldAntiMacroQuestionResult(0x63)` Java 會檢查角色 anti-macro state，驗證輸入 code 後觸發 success/reduce；MapleForge Phase A MVP 讀 answer string、寫 log 並送 `EnableActions`，不建立 anti-macro runtime state。
- `ITEM_UNLOCK(0x95)` 此 Java tree 實際由 `PlayersHandler.UnlockItem` 處理，full handler 讀三個 short（item size/type/slot），移除 `LOCK` 或 `UNTRADEABLE` flag，並消耗解除鑰匙 `2051000`；MapleForge Phase A MVP 支援 compact one-short slot 與 Java three-short shape，找 Equip 背包 slot 的裝備，若有 `LOCK` flag 則清除、flush/persist inventory，送完整 `MODIFY_INVENTORY_ITEM` remove+add update + `EnableActions`。`UNTRADEABLE` unlock 與 `2051000` key consumption deferred。
- `COUPON_CODE(0xE7)` Java 會查 DB coupon code 並發放 GASH/MaplePoints/item/meso，失敗回 cash-shop fail；MapleForge MVP 只讀 skip short + coupon code string 後 `EnableActions`，完整 coupon DB/table 與 reward flow 待後續 CashShop 任務。
- 證據層級：Java source + Host.Shared build 0 warning/0 error + Adapters.V113 tests 313 passed / 1 skipped + Core 98 passed；真 v113 client cash-shop/anti-macro/item-unlock smoke 未跑。

---
## P2 Migration Wave 3 complex opcode MVP stubs（2026-06-18）

來源：

- `recv.properties`：`CP_UserAntiMacroItemUseRequest=0x61`、`CP_UserAntiMacroSkillUseRequest=0x62`、`REWARD_ITEM=0x6A`、`ITEM_MAKER=0x6B`、`USE_TREASUER_CHEST=0x6C`、`MONSTER_CARNIVAL=0xD5`
- Java dispatch：`MapleServerHandler.java` routes anti-macro requests to `PlayersHandler.AntiMacro`、`ITEM_MAKER` to `ItemMakerHandler.ItemMaker`、`USE_TREASUER_CHEST` / `REWARD_ITEM` to `InventoryHandler`、`MONSTER_CARNIVAL` to `MonsterCarnivalHandler.MonsterCarnival`
- Java handlers：`ItemMakerHandler.java`、`InventoryHandler.UseRewardItem`、`InventoryHandler.UseTreasureChest`、`PlayersHandler.AntiMacro`、`MonsterCarnivalHandler.java`

C→S MVP layouts:

```
CP_UserAntiMacroItemUseRequest:
writeShort(0x61)
writeInt(targetCharacterId)
writeByte(mode)

CP_UserAntiMacroSkillUseRequest:
writeShort(0x62)
writeInt(targetCharacterId)

REWARD_ITEM:
writeShort(0x6A)
writeShort(slot)
writeInt(itemId)

ITEM_MAKER:
writeShort(0x6B)
writeInt(makerType)
// full makerType-specific payload deferred

USE_TREASUER_CHEST:
writeShort(0x6C)
writeShort(slot)
writeInt(itemId)

MONSTER_CARNIVAL:
writeShort(0xD5)
writeByte(tab)
writeInt(number)
```

語義：

- 本輪只把 6 個 complex subsystem opcode 接成安全 MVP stub：讀取任務指定最小欄位後送 `EnableActions`。
- `ITEM_MAKER(0x6B)` Java 依 `makerType` 分岔處理寶石/道具製作/分解與成功廣播；MapleForge MVP 只讀 `makerType`，不建立 maker catalog 或 crafting/synthesis service。
- `REWARD_ITEM(0x6A)` 與 `USE_TREASUER_CHEST(0x6C)` Java 會檢查背包道具、抽 reward、消耗道具/key 並播放 reward animation；MapleForge MVP 只讀 slot/itemId 後放行。
- `CP_UserAntiMacroItemUseRequest(0x61)` / `CP_UserAntiMacroSkillUseRequest(0x62)` 本輪依 migration wave 範圍讀 `targetCharacterId`/`mode` 或 `targetCharacterId` 後放行，不建立 anti-macro runtime state。注意：目前 Java tree 的 `PlayersHandler.AntiMacro` full handler 讀 target character name string，item 分支再讀 slot/itemId；完整 anti-macro 移植時需重新對齊真 v113 client capture 或最終採用的 Java source map。
- `MONSTER_CARNIVAL(0xD5)` Java 依 `tab`/`number` 消耗 CP 召喚怪物、debuff 或 guardian；MapleForge MVP 只讀 `tab + number` 後放行，不建立 carnival party/CP/event state machine。
- 證據層級：Java source map + MapleForge adapter-only implementation；`Maple.Host.Shared` build 0 warning/0 error + `Maple.Adapters.V113.Tests` 299 passed / 1 skipped。真 v113 client crafting/reward/anti-macro/carnival smoke 未跑。

---
## P2 Migration Wave 4 heavy opcode MVP stubs（2026-06-18）

來源：

- `recv.properties`：`ENTER_CASH_SHOP=0x20`、`CP_HiredMerchantRemoteControl=0x34`、`USE_HIRED_MERCHANT=0x38`、`MERCH_ITEM_STORE=0x3A`、`ENTER_MTS=0x99`、`TOUCHING_MTS=0xFA`、`MTS_TAB=0xFB`
- Java dispatch：`MapleServerHandler.java` routes `ENTER_CASH_SHOP` / `ENTER_MTS` near channel transition handling, `CP_HiredMerchantRemoteControl` to `PlayerInteractionHandler.HiredMerchantRemoteControl`, `USE_HIRED_MERCHANT` / `MERCH_ITEM_STORE` to `HiredMerchantHandler`, and `TOUCHING_MTS` / `MTS_TAB` to `MTSOperation`
- Java handlers：`handling/channel/handler/InterServerHandler.java`、`handling/channel/handler/PlayerInteractionHandler.java`、`handling/channel/handler/HiredMerchantHandler.java`、`handling/cashshop/handler/MTSOperation.java`

C→S MVP layouts:

```
ENTER_CASH_SHOP:
writeShort(0x20)
// mode transition; MapleForge MVP consumes no payload.

CP_HiredMerchantRemoteControl:
writeShort(0x34)
writeShort(action)

USE_HIRED_MERCHANT:
writeShort(0x38)
writeInt(npcOid)

MERCH_ITEM_STORE:
writeShort(0x3A)
writeByte(operation)

ENTER_MTS:
writeShort(0x99)
// mode transition; MapleForge MVP consumes no payload.

TOUCHING_MTS:
writeShort(0xFA)
writeByte(operation)

MTS_TAB:
writeShort(0xFB)
writeInt(tabOrPage)
```

語義：

- 本輪只把 7 個 heavy subsystem opcode 接成安全 MVP stub：讀取任務指定最小欄位或 no-op 後送 `EnableActions`。
- `ENTER_CASH_SHOP(0x20)` Java 會保存角色、移除地圖/頻道狀態並回 cash-shop server endpoint；MapleForge MVP 不做跨 server / cash shop mode transition。
- `CP_HiredMerchantRemoteControl(0x34)`、`USE_HIRED_MERCHANT(0x38)`、`MERCH_ITEM_STORE(0x3A)` 完整語義需要 hired merchant runtime、merchant persistence、owner/visitor UI 與 item store package flow；MapleForge MVP 不建立 merchant subsystem。
- `ENTER_MTS(0x99)`、`TOUCHING_MTS(0xFA)`、`MTS_TAB(0xFB)` 完整語義需要 MTS cart/listing/buy/sell/search/page state；MapleForge MVP 不建立 MTS storage 或 auction model。
- 注意：此 Java tree 的部分 full handler 實際 first-read 與本 migration scope 的 MVP 固定欄位不同（例如 remote merchant control 與 MTS update/tab flow）；完整移植時需回到 Java source + 真 v113 client capture 校準最終 layout。
- 證據層級：Java source map + MapleForge adapter-only implementation；`Maple.Host.Shared` build 0 warning/0 error + `Maple.Adapters.V113.Tests` 299 passed / 1 skipped。真 v113 client CashShop/HiredMerchant/MTS smoke 未跑。

---
*待補（M1 後）：getAuthSuccessRequest、角色列表、移動等封包結構（M2/M3 再萃取）。*
