# MapleForge 方法論融合綱領

> 目的：把跑跑卡丁車與黃易群俠傳的成熟經驗，轉譯成 MapleForge 自己可長期使用的方法論。這不是優先順序清單，也不是照搬另一個專案的目錄結構。

## 1. MapleForge 的本質

MapleForge 不是「重寫一個楓谷私服」這麼窄的任務。它是在做三件事：

1. 把舊 Java/OdinMS/TMS113 伺服器裡的行為知識萃取出來。
2. 把這些知識放進乾淨、可測、可替換的 .NET 架構。
3. 用真 v113 客戶端與機器化測試，證明新架構不是空想。

因此 MapleForge 的核心不是功能數量，而是知識位置：

- 遊戲語義在哪裡？
- v113 byte 細節在哪裡？
- Java 權威來源在哪裡？
- 真客戶端證據在哪裡？
- 哪些已經可機器驗證，哪些仍只是候選假說？

## 2. 兩個神諭，一個架構

MapleForge 的穩定開發依賴三個支點：

- **舊 Java server**：行為神諭。回答「原本 server 會怎麼做」。
- **真 v113 client / capture**：協定神諭。回答「客戶端實際接受什麼」。
- **MapleForge clean architecture**：承載形狀。決定知識應該放在哪一層。

舊 Java 可以決定語義，但不能決定 MapleForge 的架構形狀。真客戶端可以證明 byte 與時序，但不能取代 domain model。乾淨架構可以整理系統，但不能脫離 Java 與 client 事實自行發明協定。

## 3. 從跑跑卡丁車取回來的觀念

跑跑最有價值的是短迴路與真客戶端導向。

可融合成 MapleForge 做法：

- 遇到封包或 client 行為不明時，先縮短路徑：Java → MapleForge → headless → capture/live。
- byte layout 不靠名稱猜；看客戶端實際如何消費資料。
- 一個 C2S 可能觸發多個 S2C、狀態改變、廣播、UI gate，不要用「一進一出」簡化。
- replay/golden 測試是防回歸資產，不是一次性除錯腳本。
- client probe/instrumentation 是最後手段，用於 Java 與 capture 都不足以解釋的硬問題。

跑跑提醒 MapleForge：封包能 decode 不等於功能成立；客戶端願意續留、畫面正確、狀態機繼續前進，才是更高層級的成立。

## 4. 從黃易群俠傳取回來的觀念

黃易最有價值的是制度化。

可融合成 MapleForge 做法：

- 修復經驗要留下，不只留下最後 diff。
- 工具要資產化：有入口、有 README、有何時使用的說明。
- 資料格式一旦搞清楚，要回填到資料模型與規格，不讓知識只留在 debug log。
- 文件同步不是事後美化，而是讓下一個 session 不重踩同一個坑。
- DDD/context mapping 的價值不是目錄名稱，而是讓領域語義與協定細節分開。

黃易提醒 MapleForge：專案越大，真正拖慢人的不是缺幾行程式，而是「上一輪到底證明了什麼」找不到。

## 5. MapleForge 證據階梯

後續功能可依風險選擇證據層，不是每件事都要跑到最高層。

| 層級 | 名稱 | 用途 | 典型產物 |
|---|---|---|---|
| L0 | 目標與邊界 | 定義要移植什麼、不做什麼 | 任務歷程目標/DoD |
| L1 | Java 行為來源 | 找到原 handler、packet creator、資料流程 | Java path、方法名、摘錄筆記 |
| L2 | MapleForge 單元/黃金測試 | 證明 byte/語義在新架構成立 | xUnit、golden vector、contract test |
| L3 | Headless/synthetic client | 證明封包時序與狀態機能自動重演 | headless smoke、replay test |
| L4 | 真客戶端 capture/live | 證明真 client 接受、續留、畫面/互動正確 | NDJSON capture、server log、截圖、smoke 記錄 |
| L5 | Client instrumentation/probe | 解開 Java/capture 無法說明的 client 內部語義 | probe log、hook 證據、反編譯筆記 |

使用原則：

- 低風險純 domain 行為可停在 L2。
- 協定格式或時序至少要到 L3。
- 會影響真客戶端畫面、切圖、NPC、戰鬥、商店、商城的功能，里程碑級要到 L4。
- L5 只用於硬問題，不能變成預設路徑。

## 6. 子系統完成定義

任何一個子系統要宣稱完成，至少補齊以下欄位：

| 欄位 | 問題 |
|---|---|
| Java source map | 對應 Java 類別、handler、packet creator、常數在哪裡？ |
| Domain map | Core/Application 裡承載的領域名詞、狀態、事件是什麼？ |
| Adapter map | v113 parser/serializer/opcode 放在哪裡？是否沒有漏進 Core？ |
| Persistence map | 是否改變 LiteDB/document shape？是否需要規格？ |
| Evidence map | L1-L5 跑到哪一層？哪些是 verified，哪些是 unverified？ |
| Tooling map | 是否新增 capture、decoder、bot action、oracle、fixture？工具是否有入口說明？ |
| Doc sync | protocol/world/persistence/tool/task journal 是否同步？ |

這張表比「功能看起來能跑」更重要，因為它讓後續人知道可以相信哪一部分。

## 7. 四本活帳

MapleForge 後續應把知識收進四本活帳，而不是散在聊天紀錄。

1. **協定帳**：opcode、packet layout、時序、verified/unverified fixture。主要落點是 `docs/specs/v113-protocol-spec.md` 與相關測試。
2. **世界帳**：map object、player state、field registry、AOI、NPC、combat、drop 等 runtime 語義。落在 design/spec 文件。
3. **持久化帳**：LiteDB document shape、hydrate/flush 邊界、repository 語義、資料相容策略。
4. **工具與證據帳**：windower、PacketDecoder、headless client、oracle、captures、PacketArchive、修復經驗。

任務完成時要問：這次新知識應該進哪一本帳？

## 8. 不照搬清單

MapleForge 不應照搬以下內容：

- 跑跑的 source-less 優先級。MapleForge 有 Java 神諭，應 Java-first。
- 黃易的 Python 目錄與 8-context 外形。MapleForge 已有 .NET 分層。
- 太早、太重的 Doc-Sync Gate。先讓規則可用、可理解，再逐步加硬。
- 把 capture fixture 當成真理。capture 是證據，設計真相要回到 spec/domain。
- 為了抽象而抽象。v113-first 是已定原則，版本抽象要由重複語義催生。
- 為了快而把 Java static/global 直接搬進來。那會破壞 MapleForge 的核心價值。

## 9. 新功能建議流程

後續任何較大的功能，可使用這個節奏：

1. 建任務歷程，寫死目標與 DoD。
2. 找 Java source map，列出 handler、packet creator、資料來源。
3. 判斷需要的證據層級，避免過度 live 測或過度逆向。
4. 先放 domain/use case，再放 adapter byte layout。
5. 寫 targeted tests 或 golden vectors。
6. 必要時跑 headless/live/capture。
7. 把新知識回填到四本活帳。
8. 更新任務歷程、進度日誌、任務追蹤或 commit。

這個流程的目標不是變慢，而是讓每次變快都不靠運氣。

## 10. 最終判準

融合跑跑與黃易的成功，不是 MapleForge 多了多少文件，而是後續每次開工都能更快回答：

- 我現在該相信 Java、capture、測試，還是真客戶端？
- 這個 byte 細節該不該進 Core？
- 這個修補會不會破壞多實例？
- 我完成的是可重演事實，還是只是一輪手動觀察？
- 下一個 session 能不能不用問人就知道從哪裡接？

能穩定回答這些問題，才代表取經真的融合回 MapleForge。
