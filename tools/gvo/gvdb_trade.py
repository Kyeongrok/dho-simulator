"""gvdb 의 tradeinfo.csv(data\\extracted\\gvdb\\tradeinfo.csv)에서 도시마다 파는 교역품 · 아이템과 값을 뽑는다.

줄(탭, cp932, 머리 줄 없음): 번호 · 물건 이름 · 도시 · 파는 사람 · 파는 값 · (다른 값) · 사는 값 둘 · 날짜 · 메모
「파는 값」이 적힌 줄만 그 도시가 그 물건을 판다고 본다(나머지는 그 도시에 팔았을 때의 값 보고).
일본어 이름은 클라이언트 일본어판 표(도시 10 · 교역품 19 · 아이템 14)로 번호에 잇는다.
결과: data\\extracted\\market-facts.json — [{CityId, Goods: [[교역품, 값]…], Items: [[아이템, 값]…]}]
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_quests
import gvdb_recipes

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "data", "extracted")


def main():
    sq = gvdb_recipes.squeeze
    city_by = {sq(n): i for n, i in gvdb_quests.names(10, gvo.LANG_JA).items()}
    good_by = {sq(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 19, 2).items()}
    item_by = {sq(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 14, 0).items()}
    towns, missed_town, missed_thing, rows = {}, {}, {}, 0
    for line in open(os.path.join(ROOT, "gvdb", "tradeinfo.csv"), encoding="cp932", errors="replace").read().splitlines():
        c = line.split("\t")
        # 파는 값은 다섯째 칸, 가끔 여섯째 칸에만 적혀 있다(런던의 종이 따위)
        if len(c) < 8 or not (c[4].isdigit() or c[5].isdigit()):
            continue
        rows += 1
        city = city_by.get(sq(c[2]))
        if city is None:
            missed_town[c[2]] = missed_town.get(c[2], 0) + 1
            continue
        town = towns.setdefault(city, dict(CityId=city, Goods={}, Items={}))
        name, price = sq(c[1]), int(c[4] if c[4].isdigit() else c[5])
        if c[3] == "交易所主人" and name in good_by:
            town["Goods"][good_by[name]] = price
        elif name in item_by and c[3] in ("道具屋主人", "行商人", "販売員"):
            town["Items"][item_by[name]] = price
        elif c[3] in ("交易所主人", "道具屋主人"):
            missed_thing[c[1]] = missed_thing.get(c[1], 0) + 1
    out = [dict(CityId=t["CityId"], Goods=sorted([k, v] for k, v in t["Goods"].items()), Items=sorted([k, v] for k, v in t["Items"].items()))
           for t in sorted(towns.values(), key=lambda t: t["CityId"])]
    print("rows with a sale price", rows, "| towns", len(out), "with goods", sum(1 for t in out if t["Goods"]),
          "| goods rows", sum(len(t["Goods"]) for t in out), "item rows", sum(len(t["Items"]) for t in out))
    print("towns not found", len(missed_town), sorted(missed_town.items(), key=lambda x: -x[1])[:10])
    print("things not found", len(missed_thing), sorted(missed_thing.items(), key=lambda x: -x[1])[:10])
    if "--write" in sys.argv:
        with open(os.path.join(ROOT, "market-facts.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False)
        print("wrote market-facts.json")


if __name__ == "__main__":
    main()
