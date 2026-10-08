"""gvdb 의 tradeinfo.csv 에서 도시마다 조선소(造船親方)가 파는 배와 값을 뽑는다: python gvdb_shipyard.py [--write]

줄(탭, cp932): 번호 · 물건 이름 · 도시 · 파는 사람 · 파는 값 · … (gvdb_trade.py 와 같은 파일 — 거기서는 교역소 · 도구점만 쓴다)
배 이름은 ship-names.csv 의 일본어 ↔ 한국어로, 도시는 클라이언트 도시 표(10)의 일본어 이름으로 잇는다.
선체(○○型○型船体)는 배가 아니라 조선 재료라 뺀다. 값이 안 적힌 배는 0(값은 모른다 — 게임이 다른 도시의 값이나 제 식으로 메운다).
→ data\\extracted\\shipyard-gvdb.json: [{CityId, Ships: [[배 이름(한국어), 값]…]}]
"""
import csv
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_quests
from wiki_ships import squeeze

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


def main():
    city_by = {squeeze(n): i for n, i in gvdb_quests.names(10, gvo.LANG_JA).items()}
    ships = {squeeze(r["日本語"]): r["한국어"] for r in csv.DictReader(open(os.path.join(ROOT, "ship-names.csv"), encoding="utf-8-sig")) if r["日本語"] not in ("", "※")}
    towns, lost_town, lost_ship = {}, set(), {}
    for line in open(os.path.join(ROOT, "gvdb", "tradeinfo.csv"), encoding="cp932", errors="replace").read().splitlines():
        c = line.split("\t")
        if len(c) < 5 or c[3] != "造船親方" or c[1].endswith("船体"):
            continue
        city, ship = city_by.get(squeeze(c[2])), ships.get(squeeze(c[1]))
        if city is None:
            lost_town.add(c[2])
        elif ship is None:
            lost_ship[c[1]] = lost_ship.get(c[1], 0) + 1
        else:
            price = int(c[4]) if c[4].isdigit() else 0
            town = towns.setdefault(city, {})
            town[ship] = max(town.get(ship, 0), price)
    rows = [dict(CityId=city, Ships=sorted(([n, p] for n, p in sold.items()), key=lambda s: (s[1], s[0]))) for city, sold in sorted(towns.items())]
    print(len(rows), "도시 ·", sum(len(r["Ships"]) for r in rows), "줄 ·", len({s[0] for r in rows for s in r["Ships"]}), "척 · 값 없는 줄", sum(1 for r in rows for s in r["Ships"] if s[1] == 0))
    print("못 이은 도시", sorted(lost_town)[:10], "· 배", sorted(lost_ship.items(), key=lambda kv: -kv[1])[:15])
    # 같은 배의 값이 도시마다 다른가
    prices = {}
    for r in rows:
        for n, p in r["Ships"]:
            if p:
                prices.setdefault(n, set()).add(p)
    print("도시마다 값이 다른 배", sum(1 for v in prices.values() if len(v) > 1), "/", len(prices), [(n, sorted(v)) for n, v in prices.items() if len(v) > 1][:5])
    # 조선소 줄에 값이 없는 배 가운데 gvdb 아이템 목록(items.csv 의 「船」 — 여섯째 칸이 값)에 값이 적힌 것 — 도시 0 번(어느 도시도 아니다)에 모아 값만 알린다
    known = {n for n, v in prices.items()} | {f["Name"] for f in json.load(open(os.path.join(ROOT, "ship-facts.json"), encoding="utf-8")) if f.get("Price", 0) > 0}      # 위키의 배 자료에 값이 있는 배도 건드리지 않는다
    extra = {}
    for line in open(os.path.join(ROOT, "gvdb", "items.csv"), encoding="cp932", errors="replace"):
        c = line.rstrip("\n").split("\t")
        if len(c) > 7 and c[2] == "船" and c[5].isdigit() and int(c[5]) >= 1000 and ships.get(squeeze(c[1])) not in known and ships.get(squeeze(c[1])):
            extra[ships[squeeze(c[1])]] = int(c[5])
    print("아이템 목록에서 더 얻은 배 값", len(extra), sorted(extra.items(), key=lambda kv: kv[1])[:8])
    if extra:
        rows.append(dict(CityId=0, Ships=sorted(([n, p] for n, p in extra.items()), key=lambda s: (s[1], s[0]))))
    if "--write" in sys.argv:
        json.dump(rows, open(os.path.join(ROOT, "shipyard-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
        print("wrote shipyard-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
