"""서고의 지도(map-facts-gvdb.json — gvdb 의 학문 갈래 줄)에 클라이언트 지도 표(17)의 번호를 잇는다: python maps_client.py [--write]

표 17: 번호(1900001 ~) · 이름 · 설명 — 1,244줄. 설명이 원본의 길잡이다: 「카이로 건너편에 상륙. 안쪽의 기자지방. 유적 내부. 탐색，고고학 랭크 1」.
같은 이름의 지도가 여럿이라(이름 544가지) 일본어판 설명으로 가린다: 학문 스킬과 랭크(「考古学 ランク1」) · gvdb 가 읽은 상륙지/도시 이름이 설명에 있는가.
- MapId: 하나로 가려진 지도의 번호(못 가리면 0) · NameId: 이름이 같은 아무 지도의 번호(이름만 쓴다)
- gvdb 차례 글에서 자리를 못 읽은 지도(Place 0)는 설명의 「○○で上陸」으로 상륙지를 채운다
이름 · 설명 글은 저장소에 안 올린다 — data\\extracted\\map-names.json({번호: [이름, 설명]}, .gitignore 에 걸린다)에 따로 적는다.
"""
import collections
import json
import os
import re
import struct
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_quests
from gvdb_recipes import squeeze

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


def table(lang):
    t = bytes(gvo.data_tables(lang)[17])
    out, at = {}, 4
    for _ in range(struct.unpack_from("<I", t, 0)[0]):
        rid = struct.unpack_from("<I", t, at)[0]
        name, o = gvo.packed_string(t, at + 4, rid)
        note, at = gvo.packed_string(t, o, rid)
        out[rid] = (name, note)
    return out


def main():
    ja, ko = table(gvo.LANG_JA), table(gvo.LANG_KO)
    by = collections.defaultdict(list)
    for rid, (name, _) in ja.items():
        by[squeeze(name)].append(rid)
    landing = gvdb_quests.names(11, gvo.LANG_JA)          # 이름 → 번호
    landing_name = {v: k for k, v in landing.items()}
    city_name = {v: k for k, v in gvdb_quests.names(10, gvo.LANG_JA).items()}
    landing_by_len = sorted((n for n in landing if len(n) >= 3), key=len, reverse=True)
    facts = json.load(open(os.path.join(ROOT, "map-facts-gvdb.json"), encoding="utf-8"))
    scored = []
    for k, fact in enumerate(facts):
        fact["MapId"] = fact["NameId"] = 0
        cands = by.get(squeeze(fact["Title"]), [])
        if not cands:
            continue
        fact["NameId"] = cands[0]
        rank = next((s["Rank"] for s in fact["Skills"] if s["Name"] == fact["Field"]), 0)
        for rid in cands:
            note = unicodedata.normalize("NFKC", ja[rid][1])
            # 학문과 랭크가 설명과 맞아야 같은 지도다 — 상륙지 이름만 맞는 다른 랭크의 지도를 잘못 잇지 않게(같은 이름 · 같은 곳의 지도가 랭크만 다르게 여럿 있다)
            if not (fact["Field"] in note and rank and re.search(rf"ランク{rank}(?!\d)", note.replace(" ", ""))):
                continue
            score = 2
            if fact["LandingId"] and landing_name.get(fact["LandingId"], "\0") in note:
                score += 3
            if fact["TownId"] and city_name.get(fact["TownId"], "\0") in note:
                score += 3
            if any(city_name.get(c, "\0") in note for c in fact["Cities"]):
                score += 1
            scored.append((score + (5 if len(cands) == 1 else 0), k, rid))
    # 점수 높은 것부터 — 한 번호는 한 지도에만
    taken = set()
    for score, k, rid in sorted(scored, reverse=True):
        if score <= 0 or facts[k]["MapId"] or rid in taken:
            continue
        # 같은 점수의 다른 후보가 남아 있으면 못 가린 것이다
        rivals = [r for s, kk, r in scored if kk == k and s == score and r != rid and r not in taken]
        if rivals:
            continue
        facts[k]["MapId"] = rid
        taken.add(rid)
    filled = 0
    for fact in facts:
        if fact["MapId"] and fact["Place"] == 0:
            note = unicodedata.normalize("NFKC", ja[fact["MapId"]][1])
            hit = next((n for n in landing_by_len if n in note and "上陸" in note), None)
            if hit:
                fact["Place"], fact["LandingId"] = 2, landing[hit]
                filled += 1
    print(len(facts), "건 · 이름을 이은 것", sum(1 for f in facts if f["NameId"]), "· 번호까지 가린 것", sum(1 for f in facts if f["MapId"]),
          "· 자리를 새로 채운 것", filled, "· 자리를 아는 것", sum(1 for f in facts if f["Place"]))
    for f in [f for f in facts if f["MapId"]][:4]:
        print(" ", f["Title"], "→", ko[f["MapId"]][0], "|", ko[f["MapId"]][1].replace("\n", " ")[:70])
    if "--write" in sys.argv:
        json.dump(facts, open(os.path.join(ROOT, "map-facts-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        json.dump({str(i): [n, d] for i, (n, d) in sorted(ko.items())}, open(os.path.join(ROOT, "map-names.json"), "w", encoding="utf-8"), ensure_ascii=False)
        print("wrote map-facts-gvdb.json, map-names.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
