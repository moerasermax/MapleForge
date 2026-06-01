# 封包擷取模式 · windower 客戶端側 oracle 突破紀錄

> 2026-06-01。從「一注入 windower 客戶端就登不進去」到「真實雙向封包端到端解碼通過」的完整歷程。
> 相關 commit：`9ee3139`（突破）、`d72fba7`、`a290e85`。

---

## 一、背景：萬用鑰匙的最後一塊拼圖

封包擷取模式（逆向 v113 協定的「萬用鑰匙」）有兩條擷取路徑：

| 路徑 | 方向 | 狀態（突破前） |
|---|---|---|
| **server 端擷取**（slice 2） | c2s：客戶端送出的封包（server 收下解密，有 ground truth） | ✅ 早已可用 |
| **windower 客戶端側擷取**（slice 3） | 雙向，重點是 **s2c**：server 送給客戶端的封包 | ❌ 一注入客戶端就壞 |

windower 是學 **s2c「硬骨頭」**的唯一鑰匙——把真客戶端接到參考 server，才能學到我們還不會生成的 server→client 協定。但它一直有個致命問題：**只要注入 windower，真客戶端就登不進去**。

---

## 二、症狀

- 點 Play! 後客戶端彈出對話框：**「無法登入伺服器。詳情查看官方網站。」**
- server log：每次連線「握手送出（getHello）」後，客戶端立刻 `force-close (10054)`，重試 8 次。
- 客戶端是**優雅斷線，不是 crash**——這個細節後來成了破案關鍵。

## 三、破案之路（嚴謹逐層逼近，每步都有 live 證據）

### 第 0 步：先否定錯誤假設
原本以為卡在「Play! GUI 點擊脆弱」。**截圖診斷**（`diag-launcher.ps1`）拍下 launcher：Play! 按鈕在底部藍色橫條，點擊座標 (0.5, 0.965) 正確命中。→ **不是 GUI 問題。**

### 第 1 步：windower 有沒有罪？（A/B）
`diag3`：不注入 windower 跑同一套 → 客戶端**順利握手、到登入畫面、送出 opcode=0x17（Pong）、零斷線**。
→ **windower 注入本身就是 blocker**，與協定/getHello 格式無關（與 slice 2 成功擷取 Pong 也是「無 windower」完全一致）。

### 第 2 步：哪一組 hook？（整組隔離）
加旗標 `MAPLEFORGE_WINDOWER_DISABLE_WINSOCK` / `_DISABLE_D3D`，`diag4` 跑三組：

| 模式 | 結果 |
|---|---|
| 純 winsock（停 D3D） | 🔴 BROKEN |
| 純 D3D（停 winsock） | ✅ LOGIN OK |
| 兩組都停 | ✅ LOGIN OK |

→ **winsock hook 是元兇，D3D 無辜。**

### 第 3 步：是 detour 機制嗎？（排除）
Codex 懷疑 inline detour 寫死 copy 5 bytes、無 x86 指令長度反組譯，重寫成 **hotpatch-first（`mov edi,edi` slot）+ inline-LDE fallback**（變長指令反組譯 + 相對位移重定位）。
重跑 → **還是 BROKEN**。inject.log 顯示所有 hook 都走乾淨的 hotpatch（copied 2 bytes）。
→ **機制是乾淨的，問題不在 trampoline，而在 hook 被呼叫時做的事。**（呼應「優雅斷線非 crash」的直覺）

### 第 4 步：哪一個函式？（逐函式隔離）
加 `MAPLEFORGE_WINDOWER_HOOKS` 白名單，`diag5` 一次只開一個：

| hook | 結果 |
|---|---|
| **recv** | 🔴 BROKEN |
| WSARecv / send / WSASend / WSAGetOverlappedResult / GetQueuedCompletionStatus | ✅ 全 OK |

→ **唯獨同步 `recv` hook 害的。**

### 第 5 步：讀碼找真兇
`HookedRecv` 本身透明（呼叫原函式、記錄、回傳 ret，不改 buf/ret）——和 OK 的 `HookedSend` 一模一樣。差別在**呼叫頻率**：

> 客戶端用**非阻塞 recv 高頻輪詢**收資料（大量 `WSAEWOULDBLOCK / ret=-1` 空輪詢）。
> 而 `WriteChunkSingleBuffer` 對**每次** recv（含空輪詢）都 `fprintf + fflush` **強制刷碟**，且在**全域鎖**內。
> → 高頻輪詢 × 每次同步刷碟 = **I/O 風暴**，把客戶端網路 timing 拖垮 → 收 getHello 失敗 → 放棄連線。

`send` 很少被呼叫所以沒事；`WSARecv-only` OK 是因為客戶端主收迴圈用的是同步 `recv()` 不是 WSARecv。

### 修復（外科手術，一處）
```cpp
// HookedRecv：只在「真有資料」(ret>0) 時才記錄。
// 空輪詢(WSAEWOULDBLOCK)無資料本就不必記，順手消滅 I/O 風暴。
if (ret > 0)
    WriteChunkSingleBuffer(s, "s2c", "recv", ret, (const unsigned char*)buf, (DWORD)ret, false);
```

---

## 四、成果：windower 客戶端側 oracle 成立

修復後，**全 winsock hook + D3D 注入下客戶端順利登入**，windower 首次錄到真實雙向：

```
s2c getHello(未加密): 0e00 710001003146727a5b5230780306
   → 7100=版本113, 010031=patch"1", 46727a5b=recvIv, 52307803=sendIv, 06=locale
c2s 加密封包:        0b5b095be46c  (4-byte header + 2-byte body)
   → 離線解密 = opcode 0x17 (Pong)
```

`RealWindowerCaptureTests` 端到端解碼**綠燈**：真實 windower 擷取 → 解析 getHello 抽 IV → reframe c2s → 用真 cipher 解密 → 位元級對上。

---

## 五、可複用的工程資產

**診斷 harness（repo root）：**
- `diag-launcher.ps1` — 截 launcher 圖
- `diag2-windowed.ps1` — 截注入後狀態圖（抓到「無法登入伺服器」對話框）
- `diag3-no-windower-ab.ps1` — 有/無 windower 的 A/B
- `diag4-isolate.ps1` — winsock/D3D 整組隔離
- `diag5-perfn.ps1` — 逐函式隔離

**windower 診斷旗標：**
- `MAPLEFORGE_WINDOWER_DISABLE_WINSOCK` / `_DISABLE_D3D` — 整組開關
- `MAPLEFORGE_WINDOWER_HOOKS=recv,WSARecv,...` — 每函式白名單
- inject.log 記錄每 hook 的 installed/skipped/mode/copied bytes

**更穩健的 InlineDetour：** hotpatch-first + inline-LDE fallback（含安全降級，不認得的 opcode 就不裝而非裝壞）。

---

## 六、方法論教訓（給未來的自己）

1. **「注入破壞目標」先 A/B 隔離，別盲修。** 整組 → 逐函式 → 鎖到單一點 → 才讀碼。
2. **優雅斷線（非 crash）= timing/邏輯問題，不是記憶體崩潰。** 這個直覺指向了正確方向。
3. **高頻路徑上的同步 I/O 是隱形殺手。** 每次 fflush 在低頻路徑（send）無害，在高頻路徑（recv 輪詢）致命。
4. **擷取工具的第一守則：對被觀測對象零干擾。** 觀測不該改變被觀測者的行為（這裡是 timing）。
