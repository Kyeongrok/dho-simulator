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
SELLERS = ("道具屋主人", "行商人", "取引商人", "工房職人", "販売員", "陸上交易管理人", "翻訳家")


def main():
    sq = gvdb_recipes.squeeze
    city_by = {sq(n): i for n, i in gvdb_quests.names(10, gvo.LANG_JA).items()}
    good_by = {sq(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 19, 2).items()}
    item_by = {sq(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 14, 0).items()}
    towns, missed_town, missed_thing, rows = {}, {}, {}, 0
    sells = {}     # 도시 → 그 도시의 누군가(도구점 · 행상인 · 거래 상인 · 공방 장인 · 판매원)가 파는 아이템 — 값이 안 적힌 줄도
    buys = {}      # (도시, 교역품) → 그 도시에 팔았을 때의 값들(이용자들의 보고)
    for line in open(os.path.join(ROOT, "gvdb", "tradeinfo.csv"), encoding="cp932", errors="replace").read().splitlines():
        c = line.split("\t")
        # 아이템을 파는 사람의 줄은 값이 안 적혀 있어도 「그 도시에서 판다」는 것은 알린다(행상인의 재해 대처 아이템 따위) — Sells
        if len(c) >= 8 and c[3] in SELLERS and sq(c[1]) in item_by and sq(c[2]) in city_by:
            sells.setdefault(city_by[sq(c[2])], set()).add(item_by[sq(c[1])])
        # 파는 값은 다섯째 칸, 가끔 여섯째 칸에만 적혀 있다(런던의 종이 따위)
        if len(c) >= 8 and not (c[4].isdigit() or c[5].isdigit()) and c[3] == "交易所主人" and (c[6].isdigit() or c[7].isdigit()):
            if sq(c[1]) in good_by and sq(c[2]) in city_by:
                buys.setdefault((city_by[sq(c[2])], good_by[sq(c[1])]), []).extend(int(v) for v in (c[6], c[7]) if v.isdigit())
            continue
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
            # 보통 값(다섯째 칸) 없이 동맹 값(여섯째 칸)만 적힌 품목은 투자해야 나오는 것이다(세비야의 도시 쪽과 대어 봄: 셋 다 要投資) — 금액은 모른다(−1)
            if not c[4].isdigit():
                town.setdefault("Invest", {})[good_by[name]] = -1
        elif name in item_by and c[3] in ("道具屋主人", "行商人", "販売員"):
            town["Items"][item_by[name]] = price
        elif c[3] in ("交易所主人", "道具屋主人"):
            missed_thing[c[1]] = missed_thing.get(c[1], 0) + 1
    # 도시 쪽(town_N.html — N 은 gvdb 의 도시 번호)을 받아 둔 도시는 품목마다의 「要投資（必要投資額：N）」을 그대로 쓴다
    import glob, html as html_lib, re as re_lib
    gvdb_city = {}
    towns_csv = os.path.join(ROOT, "gvdb", "towns.csv")
    if os.path.exists(towns_csv):
        for line in open(towns_csv, encoding="cp932", errors="replace").read().splitlines():
            c = line.split("\t")
            if len(c) > 1 and sq(c[1]) in city_by:
                gvdb_city[c[0]] = city_by[sq(c[1])]
    exact = 0
    for path in glob.glob(os.path.join(ROOT, "gvdb", "town_*.html")):
        number = os.path.basename(path)[5:-5]
        if number not in gvdb_city or gvdb_city[number] not in towns:
            continue
        page = open(path, encoding="utf-8").read()
        names = dict(re_lib.findall(r"TradeInfoEdit\?id=(\d+)&is_trade=T[^>]*>\s*(?:<[^>]+>\s*)*([^<\s][^<]*)", page))
        town = towns[gvdb_city[number]]
        town["Invest"] = {}          # 이 도시는 쪽의 값만 믿는다
        for m in re_lib.finditer(r"id=\"t0_(\d+)\" class=\"detail_hidden\"[^>]*>([\s\S]*?)</tr>", page):
            note = html_lib.unescape(re_lib.sub(r"<[^>]+>", " ", m.group(2)))
            need = re_lib.search(r"要投資[^0-9０-９]*([0-9,]+)", note)
            name = sq(names.get(m.group(1), ""))
            if "要投資" in note and name in good_by:
                town["Invest"][good_by[name]] = int(need.group(1).replace(",", "")) if need else -1
                exact += 1
    print("invest-locked goods", sum(len(t.get("Invest", {})) for t in towns.values()), "| from town pages", exact)
    # 사 주는 값: 보고가 둘이면 큰 쪽(시세가 좋을 때), 그 교역품의 가운데 값의 여덟 배를 넘는 것은 잘못 적힌 것으로 보고 뺀다
    per_good = {}
    for (city, good), values in buys.items():
        per_good.setdefault(good, []).append(max(values))
    middle = {g: sorted(v)[len(v) // 2] for g, v in per_good.items()}
    for (city, good), values in buys.items():
        price = max(values)
        if price > middle[good] * 8 or price <= 0:
            continue
        towns.setdefault(city, dict(CityId=city, Goods={}, Items={})).setdefault("Buys", {})[good] = price
    for city in sells:
        towns.setdefault(city, dict(CityId=city, Goods={}, Items={}))
    print("item sellers", len(sells), "towns ·", sum(len(v) for v in sells.values()), "rows ·", len(set().union(*sells.values())), "items")
    out = [dict(CityId=t["CityId"], Goods=sorted([k, v, t.get("Invest", {}).get(k, 0)] for k, v in t["Goods"].items()), Items=sorted([k, v] for k, v in t["Items"].items()), Buys=sorted([k, v] for k, v in t.get("Buys", {}).items()),
                Sells=sorted(sells.get(t["CityId"], ())))
           for t in sorted(towns.values(), key=lambda t: t["CityId"])]
    print("rows with a sale price", rows, "| towns", len(out), "with goods", sum(1 for t in out if t["Goods"]),
          "| goods rows", sum(len(t["Goods"]) for t in out), "item rows", sum(len(t["Items"]) for t in out), "| buy reports", sum(len(t["Buys"]) for t in out))
    print("towns not found", len(missed_town), sorted(missed_town.items(), key=lambda x: -x[1])[:10])
    print("things not found", len(missed_thing), sorted(missed_thing.items(), key=lambda x: -x[1])[:10])
    # 명산품: items.csv 의 설명에 「※○○の名産品」 — ○○ 은 문화권(클라이언트 표 1)
    import re
    culture_by = {sq(n): i for n, i in gvdb_quests.names(1, gvo.LANG_JA).items()}
    special, lost = {}, {}
    items_csv = os.path.join(ROOT, "gvdb", "items.csv")
    if os.path.exists(items_csv):
        for line in open(items_csv, encoding="cp932", errors="replace").read().splitlines():
            c = line.split("\t")
            m = re.search(r"※([^<※]+?)の名産品", c[7]) if len(c) > 7 else None
            if not m or sq(c[1]) not in good_by:
                continue
            if sq(m.group(1)) in culture_by:
                special[good_by[sq(c[1])]] = culture_by[sq(m.group(1))]
            else:
                lost[m.group(1)] = lost.get(m.group(1), 0) + 1
    print("specialties", len(special), "| culture names not found", lost)
    # 전용: 「食料への転用量：3」 · 「水への転用量：1」 · 「資材…」 · 「弾薬 / 砲弾…」 — 숫자는 전각일 수도 있다
    turns, kinds = {}, {"水": 0, "食料": 1, "資材": 2, "弾薬": 3, "砲弾": 3}
    if os.path.exists(items_csv):
        for line in open(items_csv, encoding="cp932", errors="replace").read().splitlines():
            c = line.split("\t")
            m = re.search(r"(水|食料|資材|弾薬|砲弾)への転用量[：:]\s*([0-9０-９]+)", c[7]) if len(c) > 7 else None
            if m and sq(c[1]) in good_by:
                turns[good_by[sq(c[1])]] = [kinds[m.group(1)], int(m.group(2).translate(str.maketrans("０１２３４５６７８９", "0123456789")))]
    print("conversions", len(turns), {k: sum(1 for v in turns.values() if v[0] == k) for k in range(4)})
    if "--write" in sys.argv:
        with open(os.path.join(ROOT, "specialties.json"), "w", encoding="utf-8") as f:
            json.dump({str(k): v for k, v in sorted(special.items())}, f)
        with open(os.path.join(ROOT, "conversions.json"), "w", encoding="utf-8") as f:
            json.dump({str(k): v for k, v in sorted(turns.items())}, f)
        with open(os.path.join(ROOT, "market-facts.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False)
        print("wrote market-facts.json")


if __name__ == "__main__":
    main()
