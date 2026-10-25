# 怪物掉落資料（P119）

MapleForge 不在版控收錄遊戲資料（見 `NOTICE.md`）。怪物掉落表沿用舊 Java 伺服器 SQL dump 的 `drop_data` 表，於本機轉成 JSON 後由設定檔指定路徑。

```bash
python3 tools/drop-data/convert_drop_data.py <舊伺服器>/sql/testms.sql <你的資料目錄>/monster-drops.v113.json
```

`src/Maple.Host.Login/appsettings.json`（或環境變數 `Content__MonsterDropCatalogPath`）：

```json
"Content": { "MonsterDropCatalogPath": "<你的資料目錄>/monster-drops.v113.json" }
```

未設定或檔案不存在時，伺服器啟動會記一筆警告並退回空表（怪物只掉楓幣）。

驗證真實資料：`MAPLEFORGE_DROP_CATALOG=<json> dotnet test tests/Maple.Content.Tests --filter JsonMonsterDropCatalogTests`。
