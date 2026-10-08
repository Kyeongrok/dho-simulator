"""gvdb 의 fs_build.csv(배마다의 옵션 스킬 부여 조합 — 이용자 보고)를 푼다: python gvdb_fsbuild.py [--write]

줄(탭, cp932): 번호 · 배 이름 · ? · 재료 넷(주요 돛 · 포문 · 장비 · 장비 — 빈 칸도 있다) · 붙은 스킬 · 날짜 · 메모
→ data\\extracted\\ship-combos-gvdb.json: [{Ship, Parts: [재료 이름…], Skill}] (모두 한국어 — 클라이언트 표의 일본어 ↔ 한국어로 잇는다)
게임은 배 상세(ssjoy)에 조합이 없는 배 · 스킬에 이것을 쓴다.
재료 칸의 차례(돛 · 포문 · 장비 · 장비)는 원본 강화 창의 칸 넷과 같다.
"""
import csv
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import skills as skill_table
from wiki_ships import squeeze

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


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
    ja = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_JA)[skill_table.T_SKILL])}
    ko = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_KO)[skill_table.T_SKILL])}
    skill_ko = {squeeze(n): ko[i] for i, n in ja.items() if 2000 <= i < 2200 and n and ko.get(i)}
    ships = {squeeze(r["日本語"]): r["한국어"] for r in csv.DictReader(open(os.path.join(ROOT, "ship-names.csv"), encoding="utf-8-sig")) if r["日本語"] not in ("", "※")}
    ja_items, ko_items = item_names(gvo.LANG_JA), item_names(gvo.LANG_KO)
    part_ko = {}
    for i, n in ja_items.items():
        if 2200000 <= i < 2300000 and n and ko_items.get(i):
            part_ko.setdefault(squeeze(n), ko_items[i])
    rows, lost = [], {"ship": set(), "part": set(), "skill": set()}
    seen = set()
    for line in open(os.path.join(ROOT, "gvdb", "fs_build.csv"), encoding="cp932", errors="replace"):
        c = line.rstrip("\n").split("\t")
        if len(c) < 8 or not c[7]:
            continue
        ship, skill = ships.get(squeeze(c[1])), skill_ko.get(squeeze(c[7]))
        parts = [p for p in c[3:7] if p]
        named = [part_ko.get(squeeze(p)) for p in parts]
        if ship is None:
            lost["ship"].add(c[1])
        if skill is None:
            lost["skill"].add(c[7])
        for p, n in zip(parts, named):
            if n is None:
                lost["part"].add(p)
        if ship is None or skill is None or None in named or len(named) < 2:
            continue
        key = (ship, skill, tuple(sorted(named)))
        if key in seen:
            continue
        seen.add(key)
        rows.append(dict(Ship=ship, Parts=named, Skill=skill))
    print(len(rows), "조합 ·", len({r["Ship"] for r in rows}), "척 ·", len({r["Skill"] for r in rows}), "스킬")
    print("못 이은 배", len(lost["ship"]), sorted(lost["ship"])[:8], "· 재료", sorted(lost["part"])[:8], "· 스킬", sorted(lost["skill"])[:8])
    for r in rows[:4]:
        print(" ", r)
    if "--write" in sys.argv:
        json.dump(rows, open(os.path.join(ROOT, "ship-combos-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
        print("wrote ship-combos-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
