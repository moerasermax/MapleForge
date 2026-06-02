# 重大突破：方法 A 突破中文 IME，登入框首次收到合成鍵盤輸入

> 2026-06-02。歷經 ~9 輪全失敗後，**方法 A（切英文鍵盤佈局）在真正的 `MapleStoryClass` 登入框上首次讓合成 scancode 正常進字**。

## 結論（決定性）
在中文 IME 環境下，注入前把佈局切成 en-US(0x0409)，windower 的 `SendInput(KEYEVENTF_SCANCODE)` 送出的 "test1234"：
- WndProc probe 收到的 `WM_KEYDOWN wParam` = `0x54 0x45 0x53 0x54 0x31 0x32 0x33 0x34`（T E S T 1 2 3 4 的正常 VK）
- **`VK_PROCESSKEY(0xE5)` 出現 0 次**（之前 9 輪一律是 0xE5）
- 伴隨 15 行 `WM_CHAR`
- 注入目標確認 `class=MapleStoryClass`（真正的根因窗，非 Play! launcher 的 IE 窗）

→ 中文 IME 的 ProcessKey 攔截被繞過。鍵盤自動輸入登入框的長期 blocker（active blocker #1）**機制上已解**。

## 關鍵技術洞察（別忘）
**真正讓方法 A 生效的是 `ActivateKeyboardLayout(en, KLF_SETFORPROCESS)`（process 層同步切換），不是 `PostMessageW(WM_INPUTLANGCHANGEREQUEST)`。**
- log 顯示 `kbd-layout NOT verified`：輪詢期間 `GetKeyboardLayout(targetThread)` 仍回舊的 0x0404，因為 `WM_INPUTLANGCHANGEREQUEST` 是 async、要等目標遊戲 loop pump 才處理（慢/沒同步）。
- 但注入緊接在 `ActivateKeyboardLayout(KLF_SETFORPROCESS)` 之後就成功了——process-wide activate 是同步的，`SendInput` 的 scancode 即以 en-US 佈局解析，不進中文 IME。
- 推論：WM_INPUTLANGCHANGEREQUEST 在此並非必要條件；KLF_SETFORPROCESS 才是。`NOT verified` 是診斷指標的瑕疵（驗 thread 佈局而非 process），**不影響功能**；後續可改成驗 process 層或縮短期望。

## 怎麼重現
- 實作：`tools/windower/windower.cpp` 的 `BeginEnglishLayout`/`EndEnglishLayout`（搜 "kbd-layout"）。
- 測試腳本：`diag10-kbd-layout-en.ps1`（鎖定 `MapleStoryClass` 窗、開 `KBD_DEBUG`、尾段判讀 0xE5 是否消失）。
- 預設開啟；`MAPLEFORGE_WINDOWER_KBD_LAYOUT_EN=0` 可關掉做 A/B 對照。
- 注意：第一輪 Play! launcher 沒帶起遊戲（"已取消瀏覽該網頁"、0x17=0），疑遠端 RDP 讓 `mouse_event` 點擊飄掉；改鎖 `MapleStoryClass` 並重跑即成立。Play! 點擊仍是已知脆弱點。

## ✅ 完整無人值守自動登入打通（2026-06-02 同日達成）
方法 A 突破後，當天就把整條登入鏈路串通，**server 端確認登入成功**：
```
[v113] ✓ 登入成功 account='testuser' (id=1)
```
鏈路（`diag11-full-login.ps1`，零人工）：起 server+windower → Play! 啟動遊戲 → 鎖定 `MapleStoryClass` 主窗 → 切 en-US 佈局 → 注入帳密 → 點登入鈕 → server 驗證成功。

**關鍵踩坑與解法（座標/焦點，全部實證）**：
1. **窗尺寸會在 800×600／808×631 間變動**：要用 `FindLargestByClass` 取 width 最大的 `MapleStoryClass` 主窗，別抓到小子窗(會導致截圖 175px、座標全偏)。
2. **比例座標靠局部 zoom 反推會累積誤差**：改用「原圖疊 5% 網格」直接讀。實測登入面板：帳號框≈(0.56,0.45)、密碼框≈(0.50)、登入鈕≈**(0.78,0.46)**(先前 0.73 偏左點到鈕外、0.74,0.37 整個偏高 0.1 全沒中)。
3. **自繪登入框的滑鼠點擊切不動內部欄位焦點**：帳號框能進字是因為它是「記憶帳號」的預設焦點；密碼框點不到焦點 → 改用 **Tab 鍵**從帳號框跳密碼框。注入字串 `"testuser\ttest1234"`(中間 `\t`)，windower `SendMappedCharacterInput` 支援 VK_TAB。實測 WndProc 收到 wParam=0x09(Tab)，密碼框成功顯示星號。
4. **記憶帳號預填是假象陷阱**：登入框啟動就反白預填上次帳號，別把它誤判成「注入成功」——要看注入後內容變化(反白→白字/追加)。

## 尚未證明 / 下一步
- 肉眼截圖為縮圖、欄位字元未清晰確認；log 證據已足夠強，但可再放大截圖或讀記憶體 buffer 佐證「字確實落進密碼欄」。
- 帳號框未注入、登入未完成 → 仍卡 active blocker #2（AuthSuccess 彈回）/#3（沒角色）。鍵盤這一哩通了，接著要把「帳號框+密碼框都注入→完整登入」串起來，再深查彈回。
- 可選精煉：修 `kbd-layout verified` 判定（驗 process 層佈局），讓 log 不再誤報 NOT verified。

關聯記憶：[[keyboard-injection-investigation]] [[current-state-resume]] [[protocol-learning-strategy]]
