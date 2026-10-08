"""大航海時代DB(gvdb.mydns.jp) 사본에서 모험 의뢰를 뽑아 data\\extracted\\quest-facts.json 에 넣는다: python gvdb_quests.py [--write]

받지는 않는다 — data\\extracted\\gvdb\\quests.csv(탭으로 나눈 Shift-JIS, 6,784줄)와 quest_town_*.html(도시별 쪽 — 보수 · 선금이 여기에만 있다)을 읽는다.
CSV 의 칸: ID · クエスト名 · 難易度 · クロノ · オファー方法 · オファー都市 · 必要スキル · 発見物種類 · 発見物 · 入手アイテム · 備考(차례 글) · …
オファー方法 이 「冒険クエスト」이고 발견물이 있는 줄만 쓴다. 발견물 · 도시 · 상륙지는 클라이언트의 일본어 표로 번호에 잇는다.
찾는 자리: 차례 글에 세계 좌표가 있으면 바다의 그 자리(Place 1), 마지막 차례에 상륙지 이름이 있으면 그 상륙지(2), 도시 이름이 있으면 그 도시(3 — 좌표 없이 「…前で視認」이면 그 도시 앞바다라 1), 못 읽으면 0.
"""
import csv
import glob
import html
import io
import json
import os
import re
import struct
import sys

import gvo
import wiki_discovery

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "data", "extracted")
# --maps: 의뢰가 아니라 서고의 지도(학문 갈래 일곱 — 「○○の地図」: 지도가 나오는 서고 도시 · 필요 스킬 · 발견물 · 자리)를 같은 꼴로 뽑아 map-facts-gvdb.json 에 적는다
MAPS = "--maps" in sys.argv
KINDS = ("生物学", "地理学", "考古学", "財宝鑑定", "天文学", "宗教学", "美術") if MAPS else ("冒険クエスト",)
LOOK = ("視認", "探索", "生態調査")


def names(table, lang):
    """이름 하나로 시작하는 표의 {이름: 번호} — 줄 꼬리의 길이를 모르니 다음 번호로 더듬는다."""
    t = gvo.data_tables(lang)[table]
    count = struct.unpack_from("<I", t, 0)[0]
    at, out = 4, {}
    for _ in range(count):
        rid = struct.unpack_from("<I", t, at)[0]
        name, o = gvo.packed_string(t, at + 4, rid)
        out.setdefault(name, rid)
        nxt = None
        for k in range(o, min(len(t) - 4, o + 600)):
            v = struct.unpack_from("<I", t, k)[0]
            if rid < v <= rid + 40:
                try:
                    s, _ = gvo.packed_string(t, k + 4, v)
                    if s and len(s) < 60 and all(ord(c) >= 0x20 for c in s):
                        nxt = k
                        break
                except Exception:
                    pass
        if nxt is None:
            break
        at = nxt
    return out


def text(cell):
    return re.sub(r"[ \t　]+", " ", html.unescape(re.sub(r"<[^>]+>", " ", cell))).strip()


def rewards():
    """도시별 쪽에서 의뢰 이름 → (보수, 선금)."""
    out = {}
    for path in glob.glob(os.path.join(ROOT, "gvdb", "quest_town_*.html")):
        page = open(path, "rb").read().decode("utf-8", "replace")
        for row in re.findall(r"<tr[\s\S]*?</tr>", max(re.findall(r"<table[\s\S]*?</table>", page), key=len)):
            head = re.findall(r"<t[hd][^>]*>([\s\S]*?)</t[hd]>", row)
            if len(head) < 4:
                continue
            found = text(head[3])
            num = lambda label: int((re.search(label + r"：\s*([\d,]+)", found) or [0, "0"])[1].replace(",", ""))
            if "報酬" in found:
                out[text(head[0]).split(" td")[0].strip()] = (num("報酬"), num("前金"))
    return out


def main():
    discoveries = {}
    for rid, (name, _) in wiki_discovery.client_names(gvo.LANG_JA).items():
        discoveries.setdefault(name, rid)
    cities = json.load(open(os.path.join(ROOT, "cities.json"), encoding="utf-8-sig"))
    sea_of = {c["Id"]: (c["SeaX"], c["SeaY"]) for c in cities if c["SeaX"] or c["SeaY"]}
    ja_city = names(10, gvo.LANG_JA)
    ja_landing = names(11, gvo.LANG_JA)
    city_by_len = sorted((n for n in ja_city if len(n) >= 2), key=len, reverse=True)
    landing_by_len = sorted((n for n in ja_landing if len(n) >= 3), key=len, reverse=True)
    ja_sea = names(8, gvo.LANG_JA)
    sea_by_len = sorted((n for n in ja_sea if len(n) >= 3 or (len(n) == 2 and n.endswith("海"))), key=len, reverse=True)      # 「北海」 · 「黒海」 · 「紅海」도
    paid = rewards()
    # 入手アイテム — 「アルヴィダの兜(耐久30 正装5 …)」처럼 이름 뒤에 설명이 붙는다: 클라이언트의 아이템 · 장비 이름 가운데 그 글의 앞머리와 맞는 가장 긴 것
    import gvdb_recipes, wiki_gear
    things = {}
    for rid, name in list(wiki_gear.gear_names(gvo.LANG_JA).items()) + list(gvdb_recipes.names(gvo.LANG_JA, 14, 0).items()):
        if name and len(name) >= 2:
            things.setdefault(gvdb_recipes.squeeze(name), rid)
    thing_names = sorted(things, key=len, reverse=True)

    def item_of(note):
        """(아이템 번호, 수) — 수는 이름 바로 뒤의 숫자(「依頼斡旋書×6」 · 「依頼斡旋書７枚」), 없으면 1. 여럿이 적혔으면 첫 것만."""
        import unicodedata
        head = unicodedata.normalize("NFKC", gvdb_recipes.squeeze(note))
        for n in thing_names:
            if head.startswith(unicodedata.normalize("NFKC", n)):
                m = re.match(r"[^0-9(（、,]{0,2}(\d{1,2})(?!\d)", head[len(unicodedata.normalize("NFKC", n)):])
                return things[n], int(m.group(1)) if m else 1
        return 0, 1
    raw = open(os.path.join(ROOT, "gvdb", "quests.csv"), "rb").read().decode("cp932", "replace")
    quests, lost, places = [], 0, [0, 0, 0, 0, 0]
    for r in list(csv.reader(io.StringIO(raw), delimiter="\t"))[1:]:
        if len(r) < 11 or r[4] not in KINDS or not r[8]:
            continue
        if r[8] not in discoveries:
            lost += 1
            continue
        steps = "\n".join(text(line) for line in re.split(r"<br\s*/?>", r[10])).strip()
        lines = [line for line in steps.split("\n") if re.match(r"\s*\d+[\.．]", line)]
        last = lines[-1] if lines else steps.split("\n")[0] if steps else ""
        spot = re.search(r"(\d{3,5})\s*[\.,，、．]\s*(\d{3,5})", steps)
        x = y = landing = town = zone = 0
        place = 0
        if spot:
            place, x, y = 1, int(spot.group(1)), int(spot.group(2))
        else:
            hit = next((n for n in landing_by_len if n in last), None)
            if hit:
                place, landing = 2, ja_landing[hit]
            else:
                hit = next((n for n in city_by_len if n in last), None)
                if hit:
                    town = ja_city[hit]
                    if re.search(r"前|沖|付近.*視認|洋上", last) and town in sea_of and not re.search(r"教会|酒場|書庫|広場|宮|邸|モスク|商館|郊外", last):
                        place, x, y = 1, sea_of[town][0], sea_of[town][1]
                    else:
                        place = 3
        if place == 0:
            # 마지막 줄에서 못 읽은 것: 「○○海域 視認 ※海域内ならどこでも発見可」는 해역(자리 4), 그 밖에는 차례를 거슬러 올라가며 상륙지 · 도시를 찾는다
            for line in reversed(lines or steps.split("\n")):
                sea = next((n for n in sea_by_len if n in line), None)
                if sea and re.search(r"海域|視認|洋上|沖", line):
                    place, zone = 4, ja_sea[sea]
                    break
                hit = next((n for n in landing_by_len if n in line), None)
                if hit:
                    place, landing = 2, ja_landing[hit]
                    break
                hit = next((n for n in city_by_len if n in line), None)
                if hit:
                    place, town = 3, ja_city[hit]
                    break
        places[place] += 1
        skills = re.findall(r"([^\s,()（）]+?)\((\d+)\)", r[6])
        reward, advance = paid.get(r[1], (0, 0))
        quests.append({
            "Id": int(r[0]), "Title": r[1], "Kind": r[7], "Field": r[4] if MAPS else "", "DiscoveryId": discoveries[r[8]], "Discovery": r[8],
            "Difficulty": int(r[2] or 0), "Cities": [ja_city[t] for t in r[5].split(",") if t in ja_city],
            "Skills": [{"Name": s, "Rank": int(k)} for s, k in skills],
            "Reward": reward, "Advance": advance, "Place": place, "X": x, "Y": y, "LandingId": landing, "TownId": town, "SeaZone": zone,
            "Night": bool(re.search(r"荒天以外の夜|夜のみ|夜間のみ|夜\(曇り可\)|夜（曇り可）", steps)),
            "Item": r[9], "ItemId": item_of(r[9])[0] if r[9] else 0, "ItemCount": item_of(r[9])[1] if r[9] else 0, "Steps": steps,
            # 선행 의뢰(前提クエスト — 「6526:優れた改良望遠鏡,2811:古代の道具の地図」)의 번호들
            "Requires": [int(n) for n in re.findall(r"(?:^|[,、])\s*(\d+):", r[14])] if len(r) > 14 else [],
        })
    print(len(quests), "건(발견물을 못 이은 것", lost, ") — 자리: 못 읽음", places[0], "· 바다", places[1], "· 상륙지", places[2], "· 도시", places[3], "· 해역", places[4], "· 보수를 아는 것", sum(1 for q in quests if q["Reward"]))
    print("받는 아이템이 적힌 것", sum(1 for q in quests if q["Item"]), "· 번호에 이은 것", sum(1 for q in quests if q["ItemId"]), "· 못 이은 보기", [q["Item"][:24] for q in quests if q["Item"] and not q["ItemId"]][:12])
    have = {q["Id"] for q in quests}
    print("선행 의뢰가 적힌 것", sum(1 for q in quests if q["Requires"]), "· 그 선행 의뢰가 이 목록에 있는 것", sum(1 for q in quests if any(n in have for n in q["Requires"])))
    kinds = {}
    for q in quests:
        kinds[q["Kind"]] = kinds.get(q["Kind"], 0) + 1
    print(kinds)
    if "--write" in sys.argv:
        target = "map-facts-gvdb.json" if MAPS else "quest-facts.json"
        with open(os.path.join(ROOT, target), "w", encoding="utf-8") as out:
            json.dump(quests, out, ensure_ascii=False, indent=1)
        print(target, "에 적었다.")


if __name__ == "__main__":
    main()
