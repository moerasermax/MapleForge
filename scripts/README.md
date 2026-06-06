# scripts/ — 開發/診斷腳本

> 2026-06-06 整理：原本散在 repo 根目錄的 `.ps1` 全部收進此資料夾分類。
> 歷史 `docs/devlog/*` 仍可能提到舊路徑 `.\diag13-enter-game.ps1`、`.\test-live.ps1` 等 —— 對照下表找新位置。

所有腳本都用 `$Root = (Resolve-Path "$PSScriptRoot\..\..").Path` 解析回 repo 根（往上兩層），因此可從任意工作目錄執行，仍正確找到 `src/`、`tools/seed-test-data.ps1` 等。

## `live/` — 實機 / 啟動 / 擷取

| 腳本 | 用途 |
|---|---|
| `launch.ps1` | 啟動 Login host server |
| `test-live.ps1` | 實機 Live 測試（自動/手動模式；seed 測試資料→起 server→windower 注入→啟客戶端） |
| `test-live-capture.ps1` | 設兩個擷取環境旗標後委派 `test-live.ps1`（雙軌擷取）。委派同層 `$PSScriptRoot\test-live.ps1` |
| `capture-manual-login.ps1` | 手動登入擷取流程 |

## `diag/` — 診斷一次性腳本（多為登入框鍵盤注入調查，見記憶 `keyboard-injection-investigation`）

`diag2-windowed` / `diag3-no-windower-ab` / `diag4-isolate` / `diag5-perfn` / `diag6-login` / `diag7-kbd` / `diag8-kbd-inject` / `diag9-inputpath` / `diag10-kbd-layout-en` / `diag10-postmsg` / `diag11-full-login` / `diag12-login-record` / `diag13-enter-game` / `diag-launcher`

> 這些是歷史調查的一次性腳本（9 種鍵盤注入法多已驗證失敗），保留作記錄。需重跑時注意它們硬編了 `v113_Client` 的絕對路徑。
