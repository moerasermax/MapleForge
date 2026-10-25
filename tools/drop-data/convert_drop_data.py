#!/usr/bin/env python3
"""P119：把 Java 舊伺服器 SQL dump 的 `drop_data` 表轉成 MapleForge 的怪物掉落 JSON。

用法：
    python3 tools/drop-data/convert_drop_data.py <testms.sql> <輸出.json>

輸出格式（JsonMonsterDropCatalog）：
    {"source": "drop_data", "drops": {"<dropperId>": [[itemId, chance, min, max, questId], ...]}}

輸出檔是遊戲資料，不進版控（同 WZ）；伺服器以 Content:MonsterDropCatalogPath 指定路徑。
欄位順序對照 Java 表結構：id, dropperid, itemid, minimum_quantity, maximum_quantity, questid, chance。
"""
import json
import re
import sys

ROW = re.compile(r"INSERT INTO `drop_data` VALUES \(([^)]*)\);")


def convert(sql_path: str) -> dict:
    drops: dict[str, list[list[int]]] = {}
    with open(sql_path, encoding="utf-8", errors="replace") as sql:
        for line in sql:
            match = ROW.match(line.strip())
            if not match:
                continue
            _id, dropper, item, minimum, maximum, quest, chance = (int(v) for v in match.group(1).split(","))
            drops.setdefault(str(dropper), []).append([item, chance, minimum, maximum, quest])
    return {"source": "drop_data", "drops": drops}


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    document = convert(sys.argv[1])
    with open(sys.argv[2], "w", encoding="utf-8") as out:
        json.dump(document, out, separators=(",", ":"))
    rows = sum(len(v) for v in document["drops"].values())
    print(f"{len(document['drops'])} monsters, {rows} rows -> {sys.argv[2]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
