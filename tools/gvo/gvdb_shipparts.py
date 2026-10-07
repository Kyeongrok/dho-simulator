"""gvdb 의 아이템 목록(items.csv)에서 조선 재료(造船素材)의 강화 수치를 뽑는다: python gvdb_shipparts.py [--write]

줄의 설명 칸: 「素材種類：主帆<br/>船タイプ：すべて<br/>船サイズ：大型<br/>縦帆性能強化：+40～+50<br/>横帆性能強化：-10～0」
→ data\\extracted\\ship-parts-gvdb.json: [{Id, Name, Kind, Types, Sizes, Stats: {능력치: [낮은 값, 높은 값]}}]
Kind: 0 主帆(주요 돛) · 1 砲門(포문) · 2 船体(선체) · 3 兵装(장비). Sizes: 0 소형 · 1 중형 · 2 대형(빈 목록이면 전부).
이름은 클라이언트 아이템 표(14)의 일본어 ↔ 한국어로 잇는다(번호 2,200,000 대).
"""
import json
import os
import re
import struct
import sys

import gvo

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")
KINDS = {"主帆": 0, "砲門": 1, "船体": 2, "兵装": 3}
STATS = {"耐久力強化": "Durability", "耐久": "Durability", "縦帆性能強化": "Vertical", "横帆性能強化": "Horizontal", "漕力強化": "Rowing",
         "旋回速度強化": "Turn", "旋回強化": "Turn", "旋回性能強化": "Turn", "対波性能強化": "Wave", "対波性能": "Wave",
         "装甲強化": "Armor", "船室強化": "Cabin", "砲室強化": "Guns", "倉庫強化": "Hold", "倉庫": "Hold"}


def squeeze(s):
    return re.sub(r"[\s　・･]", "", s)


def item_names(lang):
    t = bytes(gvo.data_tables(lang)[14])
    out, off = {}, 4
    for _ in range(struct.unpack_from("<I", t, 0)[0]):
        rid = struct.unpack_from("<I", t, off)[0]
        name, o = gvo.packed_string(t, off + 4, rid)
        _, off = gvo.packed_string(t, o, rid)
        out[rid] = name
    return out


def main():
    ja, ko = item_names(gvo.LANG_JA), item_names(gvo.LANG_KO)
    by_name = {}
    for i, n in ja.items():
        if 2200000 <= i < 2300000 and n and ko.get(i):
            by_name.setdefault(squeeze(n), i)
    rows, lost = [], []
    for line in open(os.path.join(ROOT, "gvdb", "items.csv"), encoding="cp932", errors="replace"):
        c = line.rstrip("\n").split("\t")
        if len(c) < 8 or c[2] != "造船素材":
            continue
        fields = dict(p.split("：", 1) for p in c[7].split("<br/>") if "：" in p)
        if "素材種類" not in fields:
            continue
        item = by_name.get(squeeze(c[1]))
        if item is None:
            lost.append(c[1])
            continue
        stats = {}
        for label, value in fields.items():
            if label in STATS and (m := re.match(r"\s*([+-]?\d+)\s*[～~]\s*([+-]?\d+)", value)):
                stats[STATS[label]] = [int(m.group(1)), int(m.group(2))]
        size = fields.get("船サイズ", "すべて")
        sizes = [] if "すべて" in size else [k for k, w in enumerate(("小型", "中型", "大型")) if w in size]
        # 캐시(아이템 샵) 재료 — gvdb 에 표시가 없어 「얻는 곳이 비어 있고 이름이 特注 · デルフィン 으로 시작하는 것」으로 본다(짐작)
        cash = c[4] == "" and (c[1].startswith("特注") or c[1].startswith("デルフィン"))
        rows.append(dict(Id=item, Name=ko[item], Kind=KINDS.get(fields["素材種類"], 3), Types=fields.get("船タイプ", ""), Sizes=sizes, Stats=stats, Cash=cash))
    rows.sort(key=lambda r: r["Id"])
    print(len(rows), "재료,", "이름을 못 이은 것", len(lost), lost[:12])
    for r in rows[:5]:
        print(r)
    if "--write" in sys.argv:
        json.dump(rows, open(os.path.join(ROOT, "ship-parts-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("wrote ship-parts-gvdb.json")


if __name__ == "__main__":
    main()
