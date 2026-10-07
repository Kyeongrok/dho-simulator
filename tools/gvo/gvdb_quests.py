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
    paid = rewards()
    raw = open(os.path.join(ROOT, "gvdb", "quests.csv"), "rb").read().decode("cp932", "replace")
    quests, lost, places = [], 0, [0, 0, 0, 0]
    for r in list(csv.reader(io.StringIO(raw), delimiter="\t"))[1:]:
        if len(r) < 11 or r[4] != "冒険クエスト" or not r[8]:
            continue
        if r[8] not in discoveries:
            lost += 1
            continue
        steps = "\n".join(text(line) for line in re.split(r"<br\s*/?>", r[10])).strip()
        lines = [line for line in steps.split("\n") if re.match(r"\s*\d+[\.．]", line)]
        last = lines[-1] if lines else steps.split("\n")[0] if steps else ""
        spot = re.search(r"(\d{3,5})\s*[\.,，、]\s*(\d{3,5})", steps)
        x = y = landing = town = 0
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
        places[place] += 1
        skills = re.findall(r"([^\s,()（）]+?)\((\d+)\)", r[6])
        reward, advance = paid.get(r[1], (0, 0))
        quests.append({
            "Id": int(r[0]), "Title": r[1], "Kind": r[7], "DiscoveryId": discoveries[r[8]], "Discovery": r[8],
            "Difficulty": int(r[2] or 0), "Cities": [ja_city[t] for t in r[5].split(",") if t in ja_city],
            "Skills": [{"Name": s, "Rank": int(k)} for s, k in skills],
            "Reward": reward, "Advance": advance, "Place": place, "X": x, "Y": y, "LandingId": landing, "TownId": town,
            "Item": r[9], "Steps": steps,
        })
    print(len(quests), "건(발견물을 못 이은 것", lost, ") — 자리: 못 읽음", places[0], "· 바다", places[1], "· 상륙지", places[2], "· 도시", places[3], "· 보수를 아는 것", sum(1 for q in quests if q["Reward"]))
    kinds = {}
    for q in quests:
        kinds[q["Kind"]] = kinds.get(q["Kind"], 0) + 1
    print(kinds)
    if "--write" in sys.argv:
        with open(os.path.join(ROOT, "quest-facts.json"), "w", encoding="utf-8") as out:
            json.dump(quests, out, ensure_ascii=False, indent=1)
        print("quest-facts.json 에 적었다.")


if __name__ == "__main__":
    main()
