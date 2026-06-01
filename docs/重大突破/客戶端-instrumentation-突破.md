# 客戶端 Instrumentation — 重大突破口

> 建立 2026-06-01。標記為 **MapleForge 的重大技術突破**。
> 本文同時是「windower 視窗化接管」這段開發討論的備份，作為後續發想的基底。

---

## 0. 一句話

**不換任何客戶端檔案，在執行期把注入 DLL 載進 `MapleStory.exe`，攔截它呼叫的「系統 API」**——
我們因此拿到了**客戶端進程內的任意程式碼執行權**，可在「系統 API 邊界」做 instrumentation（視窗化、輸入、封包、記憶體、overlay），
而且**躲得過 Themida 保護殼**。這是一個強大、可長期複用的客戶端側立足點。

---

## 1. 突破內容（已驗證）

- 自製 windower（`tools/windower/`，C++）成功**接管視窗化**：強制視窗模式、套標題列「MapleForge」+ 可縮放邊框、修正 backbuffer 格式。
- 客觀驗證：`CreateDevice` 回 `hr=0`、`Present` 持續呼叫（遊戲在渲染）、桌面解析度全程不變。
- 主觀驗證：**使用者親眼確認**畫面正常（標題「MapleForge」視窗 + 正常遊戲畫面）。
- commit：`8920cda`（接管實作）、`acb4942`（接回 test-live.ps1）。

---

## 2. 技術原理：執行期 API hook（不是替換檔案）

對比過去失敗的做法（dgVoodoo2）：放一個**假的 `d3d8.dll`** 在 exe 旁取代系統的 → Themida 偵測到「載入的 d3d8 非系統檔」→ 靜默終止。

本方案完全不同：**不換任何檔案**，等遊戲自己載入「真正的系統 d3d8.dll」後，在**記憶體裡**攔截它的函式。

### Hook 鏈（log 逐行對應）
```
1. windower_host.exe 用 SetWindowsHookEx 設全域鈎子
   → Windows 自動把 windower.dll 載入有視窗的進程（含 MapleStory.exe）
   → "DllMain DLL_PROCESS_ATTACH"

2. GetProcAddress(d3d8.dll, "Direct3DCreate8") 取真實入口 → 入口 detour
   → "Direct3DCreate8 detour installed"

3. 遊戲呼叫 Direct3DCreate8 建 IDirect3D8 → 我們改 hook 它 vtable 的 CreateDevice
   → "IDirect3D8 vtable hook done (CreateDevice)"

4. 遊戲呼叫 CreateDevice(presentParams) ← 關鍵
   執行前改 presentParams：
     - Windowed = TRUE（遊戲本來要 165Hz 全螢幕）
     - BackBufferFormat 0x17 → 0x16（16-bit → 桌面 32-bit；★不改會 D3DERR_INVALIDCALL）
   → 呼叫真 CreateDevice → hr=0
   → 視窗改 WS_OVERLAPPEDWINDOW + 標題「MapleForge」+ AdjustWindowRect
   → 再 hook 裝置的 Reset / Present
```

### 為什麼躲得過 Themida（推論，非 100% 確定）
Themida 保護/完整性檢查的是**它自己包的那塊 `MapleStory.exe` 程式碼**。
我們動的是**系統 `d3d8.dll` 的函式入口與 COM vtable**——不在 Themida 的檢查範圍，所以 hook 活得下來。

---

## 3. 這次能突破的兩個關鍵發現

1. **Play! 啟動器**：`MapleStory.exe <ip> <port>` 先開的是**啟動器**（白底 + Play! 按鈕），
   **點 Play! 才會啟動 D3D 遊戲**、才會走到 `CreateDevice`。先前「注入成功卻沒到 CreateDevice」就是卡在啟動器、遊戲沒起來。靠截圖才發現。
2. **BackBufferFormat 必須相容桌面**：強制 `Windowed=TRUE` 後，backbuffer 格式仍是全螢幕的 16-bit → 視窗模式下非法 → `CreateDevice` 回 `D3DERR_INVALIDCALL (0x8876086C)`。
   修法：用 `IDirect3D8::GetAdapterDisplayMode` 取桌面格式覆寫；Reset 用快取格式。

---

## 4. 這個立足點「能做什麼」

有了客戶端進程內的程式碼執行權 + 可 hook 任何系統 API：
- **D3D8**：自訂 overlay、debug 資訊、FPS、畫面標記。
- **DirectInput / Win32 輸入**：改鍵位、加熱鍵、**從內部直接餵輸入**（比外部 SendInput 可靠）。
- **winsock send/recv**：**客戶端側觀測/補封包**（測試、驗證、甚至自動登入）。
- **讀客戶端記憶體**：拿遊戲狀態（HP/座標/地圖/UI 畫面）當測試斷言。
- **啟動器**：自動跳過 / 自動點 Play!。

---

## 5. 界線（誠實、必守）

### 界線一：hook「系統 API 邊界」安全；改「客戶端自己的程式碼」脆弱
- 攔它「對外的呼叫」（系統 API、vtable）＝穩。
- 改它「體內的程式碼」（NOP 掉檢查、改內部邏輯）＝**Themida 完整性檢查很可能抓到並讓客戶端崩**，逐案、不可靠。
- **原則：攔邊界，不動內臟。**

### 界線二：客戶端 hook 是 v113 專屬，不進「版本抽象核心」
- 綁死「這支 v113 客戶端 + Themida」。**未來升級楓之谷版本→客戶端與保護都換→補釘全要重做。**
- 定位＝「**客戶端工具 / 自動化**」這一桶；**不是**版本無關的乾淨 server 核心。
- 遊戲功能該做在 **server 端**（才跨版本）；別把功能做成客戶端 hack。

### 紀律
- ✅ 安全可靠：hook 系統 API、讀記憶體、overlay、自動化測試。
- ⚠️ 賭/逐案：改 Themida 保護的客戶端內部碼。
- 🔒 守 Approval Queue：注入/動客戶端相關的重大改動先確認。

---

## 6. 對「現在」最有價值的用法：把自動化測試做穩

這個能力直接給了「登入最後一哩」一條比外部 SendInput 可靠得多的路：
- **客戶端內 hook 輸入**直接餵帳密；或 **hook winsock** 觀測/補登入封包；
- **讀記憶體**拿「目前畫面/地圖」當斷言（比只看 server log 更強）。
→ 這才是「無人值守自動驗證」真正穩的做法。屬 Codex 的 binary lane。

---

## 7. 團隊分工的反思（為何效率與正確率都提升）

這次突破驗證了 [能力地圖](workflow.md#2-ai-能力地圖與任務路由2026-06-01能力圓桌定版) 的分工：
- **Codex（binary/C++/D3D8 lane）**：寫 + 反覆修 `windower.cpp` 的 hook、處理編譯坑（`/TP`、`/utf-8`），純程式碼、不碰實機。
- **Opus（協調/live 測試/診斷）**：實際啟動真客戶端、**截圖發現 Play! 啟動器**、從 log **診斷出 BackBufferFormat 是 `D3DERR_INVALIDCALL` 根因**、餵精準錯誤回 Codex、寫安全閥 + 驗證腳本、跑 live 驗收。
- 鐵則「**實作主責 ≠ 驗收主責**」落地：Codex 寫（看不到螢幕、不碰實機），Opus 跑 live 驗收，最後使用者親眼確認。

**心得**：把「寫」交給最會寫的、把「跑起來找真相」交給能跑 live 的、把最終拍板交給人——
分工把一個過去卡很久的 Themida 視窗化問題，在一個 session 內打通並驗證。

---

## 8. 封包 / 記憶體擷取 —— 破口最硬也最有價值的應用（2026-06-01 討論整合）

> 名詞：**overlay** = 用已 hook 的 D3D 裝置，在遊戲畫面「上面」疊我們自己畫的東西（Present hook 裡畫 FPS/座標/debug HUD/server 狀態）。純客戶端視覺，不動邏輯。

### 為什麼是破口：把「靜態反編譯」變「動態觀測」
- 過去搞懂客戶端要**靜態反編譯**，但客戶端被 **Themida** 包（混淆/加密/反調試）→ 靜態幾近地獄。
- 破口（已在進程內、Themida 已自解殼到記憶體）讓我們改走**動態**：hook、讀記憶體、看它活著怎麼跑。**不跟 Themida 靜態硬碰**——這就是難度從「不可能」降到「可行」的關鍵槓桿。

### 兩個封包方向，難度天差地別
1. **client → server（玩家操作送出的）：已經不難。** MapleForge server 本來就有 cipher、會解密（log 的 `解密封包 opcode=…`）。玩家操作 → server 端就能記下解密後的封包。
2. **server → client（功能「該回」什麼）：真正的硬骨頭。** 沒有原始可運作伺服器可錄 → 封包擷取給不了。唯一路＝逆向「客戶端怎麼解析 server 回包」（期待的結構/欄位/長度）＝組語/反編譯。破口把它從「靜態反編譯 Themida」變「**動態追蹤：餵試探回包 → 看客戶端在記憶體/行為怎麼反應/在哪 crash**」。

### 三件組合＝萬用鑰匙
1. **封包擷取**（client→server，易）
2. **動態記憶體/行為追蹤**（破口讓 server→client 的逆向可行）← 最硬、最有價值
3. **已有的 cipher**（MapleAesOfb 已 byte 級驗證 → 解密不再是門檻）

### 鐵則：功能的家在 Server，破口是「神諭/診斷工具」
- 功能壞/缺 → 先問「**server 沒送對，還是 client 顯示錯**」→ 99% 在 **server 實作/修**。
- 破口用來：**看真相(診斷)**、**把真客戶端當協定神諭**、**自動化驗證**。**不是**拿來改客戶端記憶體「實作」功能（會綁死 v113、違反版本抽象北極星）。
- 範例：**小鋼珠機**（client 有 UI、server 沒實作）。修法＝server 實作協定+邏輯；破口幫忙錄 client 送的封包 + 動態逆向 client 期待的回包。**（目前暫緩；先做通用封包擷取模式。）**

## 9. 第一個落地工作：封包擷取模式（packet capture）★進行中
在 windower 加 hook winsock，把 client↔server 流量 dump 成**帶時間戳的 log/檔**。
- 價值：任何「客戶端有、server 還沒實作」的功能都能用它逆向協定 → MapleForge 的**萬用鑰匙**。
- 設計討論見對話；要點：hook 點(winsock send/recv/WSASend/WSARecv)、擷取原始(加密)bytes + 時間戳 + 方向、離線用我們的 cipher 解密(stream 順序+握手 IV)、可開關、安全 fallback。

## 10. 待發想（使用者後續想法的接口）
- 客戶端內自動登入（hook 輸入 / winsock）→ 完成無人值守 E2E 驗證。
- overlay 顯示 server 端狀態（debug HUD）。
- 客戶端記憶體讀取 → 測試斷言來源。
- 多開（每個客戶端各自 windower + 連不同 port）。
- （其餘想法陸續補在此）
