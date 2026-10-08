"""gvdb 의 tradeinfo.csv 에서 도시마다 선박 부품(대포 · 돛 · 장갑 · 선수상 · 문장 · 특수장비)을 누가 얼마에 파는지 뽑는다: python gvdb_partshops.py [--write]

파는 사람: 武器職人(대포) · 製帆職人(돛) · 彫刻家(선수상) · 塗装職人(문장) · 船大工 · 製材職人(장갑 · 특수장비) — gvdb_trade.py · gvdb_shipyard.py 가 안 쓰는 줄.
이름은 클라이언트 부품 표(돛 25 · 장갑 23 · 선수상 27 · 문장 26 · 대포 22 · 특수장비 24)의 일본어 이름으로 번호에 잇는다.
값이 안 적힌 줄은 0(값은 모른다).
→ data\\extracted\\partshops-gvdb.json: [{CityId, Parts: [[부품 번호, 값]…], Materials: [[조선 재료 아이템 번호, 값]…]}]
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_quests
import gvdb_recipes

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")
SELLERS = ("武器職人", "製帆職人", "彫刻家", "塗装職人", "船大工", "製材職人")
TABLES = ((25, 16), (23, 8), (27, 20), (26, 0), (22, 40), (24, 22))      # (표, 줄 꼬리 바이트)


def main():
    sq = gvdb_recipes.squeeze
    city_by = {sq(n): i for n, i in gvdb_quests.names(10, gvo.LANG_JA).items()}
    part_by, total = {}, 0
    for table, tail in TABLES:
        found = gvdb_recipes.names(gvo.LANG_JA, table, tail)
        total += len(found)
        for rid, name in found.items():
            part_by.setdefault(sq(name), rid)
    # 조선 재료(아이템 2,200,000 대 — 대 개프세일 · 가공 목재 · 개량 대형 포문 …)도 같은 장인들이 판다
    material_by = {sq(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 14, 0).items() if 2200000 <= i < 2300000 and n}
    materials = {}
    towns, lost, by_seller = {}, {}, {}
    for line in open(os.path.join(ROOT, "gvdb", "tradeinfo.csv"), encoding="cp932", errors="replace").read().splitlines():
        c = line.split("\t")
        if len(c) < 5 or c[3] not in SELLERS:
            continue
        city, part = city_by.get(sq(c[2])), part_by.get(sq(c[1]))
        if city is not None and part is None and sq(c[1]) in material_by:
            sold = materials.setdefault(city, {})
            sold[material_by[sq(c[1])]] = max(sold.get(material_by[sq(c[1])], 0), int(c[4]) if c[4].isdigit() else 0)
            continue
        if city is None or part is None:
            lost[(c[3], c[1])] = lost.get((c[3], c[1]), 0) + 1
            continue
        by_seller[c[3]] = by_seller.get(c[3], 0) + 1
        town = towns.setdefault(city, {})
        town[part] = max(town.get(part, 0), int(c[4]) if c[4].isdigit() else 0)
    rows = [dict(CityId=city, Parts=sorted([p, v] for p, v in towns.get(city, {}).items()), Materials=sorted([p, v] for p, v in materials.get(city, {}).items()))
            for city in sorted(set(towns) | set(materials))]
    print("조선 재료", len(materials), "도시 ·", sum(len(v) for v in materials.values()), "줄 ·", len({p for v in materials.values() for p in v}), "가지 · 값 없는 줄", sum(1 for v in materials.values() for p in v.values() if p == 0))
    print("클라이언트 부품", total, "→", len(rows), "도시 ·", sum(len(r["Parts"]) for r in rows), "줄 ·", len({p[0] for r in rows for p in r["Parts"]}), "가지 ·", by_seller)
    names = sorted({(s, n) for (s, n) in lost}, key=lambda k: -lost[k])
    print("부품 표에 없는 물건", len(names), [f"{s}:{n}" for s, n in names[:30]])
    prices = {}
    for r in rows:
        for p, v in r["Parts"]:
            if v:
                prices.setdefault(p, set()).add(v)
    print("도시마다 값이 다른 부품", sum(1 for v in prices.values() if len(v) > 1), "/", len(prices), list(prices.items())[:3])
    if "--write" in sys.argv:
        json.dump(rows, open(os.path.join(ROOT, "partshops-gvdb.json"), "w", encoding="utf-8"), indent=0)
        print("wrote partshops-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
