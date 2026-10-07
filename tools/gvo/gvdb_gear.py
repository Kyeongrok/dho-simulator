"""gvdb 의 items.csv(data\\extracted\\gvdb\\items.csv)에서 장비가 올려 주는 스킬을 뽑는다: python gvdb_gear.py [--write]

줄(탭, cp932): 번호 · 이름 · 갈래(装備品（胴体）…) · … · 값 · 날짜 · 설명(<br/> 로 나뉜 줄 — 「縫製：+1」 꼴이 스킬 보정)
일본어 이름은 클라이언트 일본어판 표(장비 15 · 스킬)로 번호에 잇는다.
결과: data\\extracted\\gear-boosts-gvdb.json — {장비 번호: {스킬 번호: 랭크}}. 위키에서 뽑은 것(gear-boosts.json)과 손으로 적은 것이 이것을 덮는다.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import skills as skill_table
import wiki_gear
from gvdb_recipes import squeeze

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "data", "extracted")


def main():
    gear = {}
    for rid, name in wiki_gear.gear_names(gvo.LANG_JA).items():
        if name and not name.startswith("※"):
            gear.setdefault(squeeze(name), []).append(rid)
    korean = wiki_gear.gear_names(gvo.LANG_KO)
    ja = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_JA)[skill_table.T_SKILL])}
    ko = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_KO)[skill_table.T_SKILL])}
    skill = {squeeze(n): i for i, n in sorted(ja.items(), reverse=True) if n and i < 2000}
    found, unknown, no_gear = {}, {}, 0
    effects, effect_names = {}, {}
    for line in open(os.path.join(ROOT, "gvdb", "items.csv"), encoding="cp932", errors="replace").read().splitlines():
        c = line.split("\t")
        if len(c) < 8 or not c[2].startswith("装備品"):
            continue
        boosts = {}
        for part in re.split(r"<br\s*/?>", c[7]):
            m = re.match(r"\s*([^：:<>]+?)\s*[：:]\s*[+＋]\s*(\d+)\s*$", part)
            if not m:
                continue
            if squeeze(m.group(1)) in skill:
                boosts[skill[squeeze(m.group(1))]] = int(m.group(2))
            else:
                unknown[m.group(1)] = unknown.get(m.group(1), 0) + 1
        # 「装備効果：行動力減少抑制 Rank 5」 — 장비 효과. 랭크가 안 적혀 있으면 1 로 본다
        worn = {}
        for m in re.finditer(r"装備効果\s*[：:]\s*([^<\s]+)(?:\s*(?:Rank|R)\s*(\d+))?", c[7]):
            effect_names[m.group(1)] = effect_names.get(m.group(1), 0) + 1
            if m.group(1) == "行動力減少抑制":
                worn["VigourSave"] = max(worn.get("VigourSave", 0), int(m.group(2) or 1))
        if not boosts and not worn:
            continue
        ids = gear.get(squeeze(c[1]))
        if not ids:
            no_gear += 1
            continue
        for rid in ids:
            if boosts:
                found[rid] = boosts
            if worn:
                effects[rid] = worn
    print("장비", len(found), "· 이름을 못 이은 장비", no_gear, "· 못 이은 스킬 이름", sorted(unknown.items(), key=lambda x: -x[1])[:12])
    for rid in list(found)[:6]:
        print("  %d %s: %s" % (rid, korean.get(rid), " · ".join("%s +%d" % (ko.get(s), a) for s, a in found[rid].items())))
    if "--write" in sys.argv:
        target = os.path.join(ROOT, "gear-boosts-gvdb.json")
        json.dump({str(k): {str(s): a for s, a in v.items()} for k, v in sorted(found.items())}, open(target, "w", encoding="utf-8"), ensure_ascii=False)
        print("적었다:", target)
        target = os.path.join(ROOT, "gear-effects-gvdb.json")
        json.dump({str(k): v for k, v in sorted(effects.items())}, open(target, "w", encoding="utf-8"), ensure_ascii=False)
        print("적었다:", target, len(effects))
    print("장비 효과의 갈래:", sorted(effect_names.items(), key=lambda x: -x[1])[:20])


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
