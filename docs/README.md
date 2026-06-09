# MapleForge 文件索引

> 快速找到「規格 / 設計定案 / 開發紀錄 / 重大突破」。

## 📐 規格與規範（specs/）
- [新 session 常駐鐵律](specs/session-invariants.md) — 每次新 session 必載的北極星、流程鐵律、證據規則
- [命名與架構規範](specs/conventions.md) — namespace、命名慣例、分層守則
- [v113 協定規格](specs/v113-protocol-spec.md) — AES-OFB、封包結構、opcode
- [測試策略](specs/test-strategy.md) — 測試金字塔、黃金向量、預言機

## 🏗️ 設計定案（design/）
- [重構架構設計書](design/重構架構設計書.md) — 版本抽象、分層、里程碑
- [MapleForge 方法論融合綱領](design/MapleForge方法論融合綱領.md) — 將跑跑卡丁車與黃易群俠傳經驗轉成 MapleForge 自己的方法論
- [封包擷取模式-設計](design/封包擷取模式-設計.md) — 逆向協定的萬用鑰匙（圓桌定案）
- [AI 工作流程](design/workflow.md) — 圓桌分工、能力地圖、派活規則
- [Java 移植路線圖](design/Java移植路線圖.md) — **參照 Java 完整移植的 gap 分析＋順序（2026-06-02 轉向）**

## 📝 開發紀錄（devlog/）
- [進度日誌](devlog/進度日誌.md) — 每次 session 敘事
- [任務追蹤](devlog/任務追蹤.md) — 進度儀表板、完成判準
- [任務歷程／](devlog/任務歷程/README.md) — **單一任務**級日誌：執行前定標、執行中保持崩潰救命接手點（韌性層）

## 💡 重大突破（重大突破/）
> 收錄高技術門檻、有 live 驗證、可複用為後續基石的突破。命名格式 `{技術名稱}_{YYYYMMDD}.md`。

| 日期 | 主題 | 檔案 |
|---|---|---|
| 2026-06-01 | windower 客戶端側 oracle（recv I/O 風暴根因 + 真實雙向擷取） | [windower客戶端側oracle_20260601.md](重大突破/windower客戶端側oracle_20260601.md) |
| — | 客戶端 instrumentation 破口 | [客戶端-instrumentation-突破.md](重大突破/客戶端-instrumentation-突破.md) |

---

### 維護慣例
- **什麼進「重大突破」**：技術門檻高 + 有 live 驗證 + 成為後續開發基石。例行功能/小修不進。
- 新增突破文件後，於上表補一行索引。
- 規格(specs)穩定少改；設計(design)記決策與 trade-off；開發紀錄(devlog)時序動態。
