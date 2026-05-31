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

---
*待補（M1 後）：getAuthSuccessRequest、角色列表、移動等封包結構（M2/M3 再萃取）。*
