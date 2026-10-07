"""위키(wikiwiki.jp/gvo) 사본에서 배마다 붙는 옵션 스킬을 뽑아 data\\extracted\\shipdetail-facts.json 에 넣는다: python wiki_ships.py [--write]

사본은 data\\extracted\\wiki\\Ship_FreeStyle_*.html (한 쪽씩 받아 둔 것 — 이 도구는 받지 않는다).
표의 칸: 船名 · … · ★(강화 횟수) · 再強化 · 付加スキル(「/」로 나뉜 스킬 이름, 끝의 * 는 옛 공동건조에서 못 붙이던 것) · ….
배 이름은 ship-names.csv 의 日本語, 스킬 이름은 클라이언트 스킬 표(6)의 일본어 ↔ 한국어(번호가 같다)로 잇는다.
이미 스킬 목록이 있는 배(원본 화면 · 다른 출처로 채운 것)는 건드리지 않는다.
"""
import csv
import glob
import html
import json
import os
import re
import sys

import gvo
import skills as skill_table

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXTRACTED = os.path.join(ROOT, "data", "extracted")


def tables(path):
    t = re.sub(r"<(script|style)[\s\S]*?</\1>", "", open(path, encoding="utf-8").read())
    for m in re.finditer(r"<table[\s\S]*?</table>", t):
        rows = []
        for r in re.finditer(r"<tr[\s\S]*?</tr>", m.group(0)):
            rows.append([html.unescape(re.sub(r"<[^>]+>", "", re.sub(r"<br[^>]*>", "/", c))).strip()
                         for c in re.findall(r"<t[dh][^>]*>([\s\S]*?)</t[dh]>", r.group(0))])
        yield rows


def squeeze(s):
    # 위키가 새 배 이름 뒤에 붙이는 「new!」 딱지는 뗀다
    return re.sub(r"[\s・･·\-－ー]", "", re.sub(r"new!$", "", s.strip()))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    ja = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_JA)[skill_table.T_SKILL])}
    ko = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_KO)[skill_table.T_SKILL])}
    skill_ko = {squeeze(n): ko[i] for i, n in ja.items() if 2000 <= i < 2200 and n and ko.get(i)}
    ships = {squeeze(r["日本語"]): r["한국어"] for r in csv.DictReader(open(os.path.join(EXTRACTED, "ship-names.csv"), encoding="utf-8-sig")) if r["日本語"] not in ("", "※")}
    found, lost_ships, lost_skills = {}, set(), set()
    build_days = {}
    for path in sorted(glob.glob(os.path.join(EXTRACTED, "wiki", "Ship_FreeStyle_*.html"))):
        for rows in tables(path):
            head = next((r for r in rows[:2] if any("スキル" in c for c in r) and any("船" in c for c in r)), None)
            if head is None or "船名" not in head[0]:
                continue
            at = next(i for i, c in enumerate(head) if "スキル" in c)
            star = next((i for i, c in enumerate(head) if c in ("★", "強化/限界")), -1)
            again = next((i for i, c in enumerate(head) if c == "再/強化"), -1)
            days_at = next((i for i, c in enumerate(head) if c == "日数"), -1)
            for r in rows:
                if len(r) != len(head) or r[0] in ("船名", "") or not r[at]:
                    continue
                name = ships.get(squeeze(r[0]))
                if name is None:
                    lost_ships.add(r[0])
                    continue
                names = []
                for s in r[at].split("/"):
                    s = re.sub(r"[(（][^)）]*[)）]$", "", s.strip().rstrip("*＊")).strip().rstrip("*＊")      # 끝의 「(PB)」 따위 덧말은 뗀다
                    if not s:
                        continue
                    if squeeze(s) in skill_ko:
                        names.append(skill_ko[squeeze(s)])
                    else:
                        lost_skills.add(s)
                def number(i):
                    return int(r[i]) if i >= 0 and r[i].isdigit() else 0
                if days_at >= 0 and r[days_at].isdigit():
                    build_days[name] = int(r[days_at])
                if names:
                    found.setdefault(name, (names, number(star), number(again), os.path.basename(path)))
    print("배", len(found), "· 이름을 못 이은 배", sorted(lost_ships), "· 못 이은 스킬", sorted(lost_skills))
    target = os.path.join(EXTRACTED, "shipdetail-facts.json")
    details = json.load(open(target, encoding="utf-8-sig"))
    by_name = {d["Name"]: d for d in details}
    added = filled = 0
    for name, (names, times, again, source) in found.items():
        d = by_name.get(name)
        if d is None:
            d = dict(No=0, Name=name, Times=times, Retimes=again, Caps=[], Slots=[], Skills=[], Hull="", Special=[], Borrowed="")
            details.append(d)
            by_name[name] = d
            added += 1
        # 다른 출처(ssjoy · 원본 화면)로 채운 배(No 가 있다)는 안 건드린다. 이 도구가 만든 항목(No 0)은 다시 맞춘다 — 적어 둔 재료 조합은 지킨다
        if d.get("Skills") and (d.get("No") or [s["Name"] for s in d["Skills"]] == names):
            continue
        kept = {s["Name"]: s.get("Parts", []) for s in d.get("Skills", [])}
        d["Skills"] = [dict(Name=n, Parts=kept.get(n, [])) for n in names]
        filled += 1
        print("  %s: %s  (%s)" % (name, " · ".join(names), source))
    for name, days in build_days.items():
        if name in by_name:
            by_name[name]["Days"] = days
    print("새 항목", added, "· 스킬을 채운 배", filled, "· 건조 일수를 아는 배", len(build_days))
    if "--write" in sys.argv:
        json.dump(details, open(target, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("적었다:", target)

    # ── 특수 건조: 어느 선체로 · 어느 항구에서 · 무슨 재질 · 조선 랭크로 짓는가 ──
    # 표의 칸: 船名 · 船体部品 · 新造港 · 材質 · 造船ランク · …. 선체는 아이템 표(14), 항구는 도시 표, 재질은 재료 표(30)의 일본어 ↔ 한국어로 잇는다.
    # 「各国本拠地」는 본거지 도시들(「イスタンブールを除く」면 이스탄불을 뺀다), 「開拓地 · 開拓街」는 게임에 없어 건너뛴다. 이미 자료가 있는 배는 안 건드린다
    import struct
    import tables as table_tools
    import ships as ship_tools

    def item_names(lang):
        t = bytes(gvo.data_tables(lang)[14])
        out, off = {}, 4
        for _ in range(struct.unpack_from("<I", t, 0)[0]):
            rid = struct.unpack_from("<I", t, off)[0]
            name, o = gvo.packed_string(t, off + 4, rid)
            _, off = gvo.packed_string(t, o, rid)
            out[rid] = name
        return out

    def pair(ja_rows, ko_rows, key="id"):
        ko_by = {r[key]: r["name"] for r in ko_rows}
        return {squeeze(r["name"]): ko_by[r[key]] for r in ja_rows if r["name"] and ko_by.get(r[key])}

    ja_tabs, ko_tabs = gvo.data_tables(gvo.LANG_JA), gvo.data_tables(gvo.LANG_KO)
    ja_items, ko_items = item_names(gvo.LANG_JA), item_names(gvo.LANG_KO)
    hull_ko = {squeeze(n): ko_items[i] for i, n in ja_items.items() if 2200000 <= i < 2300000 and n and ko_items.get(i)}
    ko_cities = table_tools.cities(ko_tabs[table_tools.T_CITY])
    city_ko = pair(table_tools.cities(ja_tabs[table_tools.T_CITY]), ko_cities)
    # 일본판의 본거지 일곱(리스본 · 세비야 · 런던 · 암스테르담 · 마르세이유 · 베네치아 · 이스탄불) — 한국판에 더 있는 본거지는 넣지 않는다
    capitals = [city_ko[squeeze(n)] for n in ("リスボン", "セビリア", "ロンドン", "アムステルダム", "マルセイユ", "ヴェネツィア", "イスタンブール") if squeeze(n) in city_ko]
    wood_ko = pair(ship_tools.table(ja_tabs, 30), ship_tools.table(ko_tabs, 30))
    built, lost = 0, set()
    for path in sorted(glob.glob(os.path.join(EXTRACTED, "wiki", "Ship_FreeStyle_*.html"))):
        for rows in tables(path):
            head = rows[0] if rows else []
            if len(head) < 5 or head[0] != "船名" or "船体" not in head[1]:
                continue
            rank_at = next((i for i, c in enumerate(head) if "ランク" in c), -1)
            for r in rows:
                if len(r) != len(head) or r[0] == "船名":
                    continue
                d = by_name.get(ships.get(squeeze(r[0])))
                hull, wood = hull_ko.get(squeeze(r[1])), wood_ko.get(squeeze(r[3]), "")
                if d is None or d.get("Special"):
                    continue
                if hull is None:
                    lost.add(r[1])
                    continue
                places = []
                for part in re.sub(r"[(（]\?[)）]", "", r[2]).split("/"):
                    part = part.strip()
                    if part.startswith("各国本拠地"):
                        places += capitals
                    elif "イスタンブールを除く" in part:
                        places = [c for c in places if c != city_ko.get(squeeze("イスタンブール"))]
                    elif squeeze(part) in city_ko:
                        places.append(city_ko[squeeze(part)])
                    elif part and "開拓" not in part:
                        lost.add(part)
                if not places:
                    continue
                rank = int(r[rank_at]) if rank_at >= 0 and r[rank_at].isdigit() else 1
                d["Special"] = [dict(Hull=hull, Rank=rank, Material=wood, City=c) for c in dict.fromkeys(places)]
                d["Hull"] = hull
                built += 1
    # ── 스킬이 붙는 재료 조합: 「スキル付加例」 표(船種 · 付加スキル · 主帆 · 砲門 · 兵装1 · 兵装2 · …) ──
    # 이용자들이 적어 둔 성공 보기라 한 스킬에 여러 줄이 있다 — 첫 줄을 쓴다. 「A/B」는 앞의 것, 괄호 글은 뺀다. 재료 이름은 아이템 표(14)로 잇는다.
    # 이미 조합이 적힌 스킬은 안 건드린다
    part_ko = {squeeze(n): ko_items[i] for i, n in ja_items.items() if n and ko_items.get(i)}
    combos, lost_parts = 0, set()
    for path in sorted(glob.glob(os.path.join(EXTRACTED, "wiki", "Ship_FreeStyle_*.html"))):
        for rows in tables(path):
            head = rows[0] if rows else []
            if len(head) < 6 or head[0] != "船種" or "スキル" not in head[1] or head[2] != "主帆":
                continue
            for r in rows:
                if len(r) < 6 or r[0] == "船種":
                    continue
                d = by_name.get(ships.get(squeeze(r[0])))
                skill = skill_ko.get(squeeze(r[1].strip().rstrip("*＊")))
                entry = next((s for s in d["Skills"] if s["Name"] == skill), None) if d and skill else None
                if entry is None or entry.get("Parts"):
                    continue
                parts, whole = [], True
                for cell in r[2:6]:
                    cell = re.sub(r"[(（][^)）]*[)）]", "", cell).split("/")[0].strip()
                    if cell in ("", "-", "－", "ー"):
                        continue
                    if squeeze(cell) in part_ko:
                        parts.append(part_ko[squeeze(cell)])
                    else:
                        lost_parts.add(cell)
                        whole = False
                if parts and whole:
                    entry["Parts"] = parts
                    combos += 1
    print("재료 조합을 넣은 스킬", combos, "· 못 이은 재료", sorted(lost_parts)[:30])
    print("특수 건조 자료를 넣은 배", built, "· 못 이은 이름", sorted(lost), "· 본거지", capitals)
    if "--write" in sys.argv:
        json.dump(details, open(target, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    # ── 능력치: Ship_Adventure · Ship_Merchant · Ship_Soldier (상점에서 파는 배) ──
    # 칸 26개: 船種 · 船型式 · クラス · 基本価格 · 冒 交 戦 · 縦 横 漕 旋 波 装 · 耐久力 · 必 室 砲 倉 · 容量 · 補 特 追 側 首 尾 · 備考
    # 자료에 없는 배는 줄째로 넣고, 있는 배는 빈 칸(값 · 조력 · 필요 선원 · 대포)만 채운다 — 있는 값은 덮어쓰지 않는다
    facts_path = os.path.join(EXTRACTED, "ship-facts.json")
    facts = json.load(open(facts_path, encoding="utf-8-sig"))
    known = {f["Name"]: f for f in facts}
    new = patched = 0
    missing = set()
    for page in ("Ship_Adventure.html", "Ship_Merchant.html", "Ship_Soldier.html"):
        path = os.path.join(EXTRACTED, "wiki", page)
        if not os.path.exists(path):
            continue
        for rows in tables(path):
            for r in rows:
                if len(r) != 26 or not r[3].replace(",", "").isdigit():
                    continue
                name = ships.get(squeeze(r[0]))
                if name is None:
                    missing.add(r[0])
                    continue
                def n(i):
                    v = r[i].replace(",", "")
                    return int(v) if v.lstrip("-").isdigit() else 0
                extra = dict(Price=n(3), Rowing=n(9), MinCrew=n(14), Guns=n(16), Slots=[n(i) for i in range(19, 25)])      # 補 特 追 側 首 尾
                f = known.get(name)
                if f is None:
                    f = dict(No=0, Name=name, Doc=0, Adventure=n(4), Trade=n(5), Battle=n(6), Durability=n(13), VerticalSail=n(7), HorizontalSail=n(8),
                             Turn=n(10), WaveResist=n(11), Armor=n(12), Cabin=n(15), Hold=n(17), **extra)
                    facts.append(f)
                    known[name] = f
                    new += 1
                    print("  새 배 %s: 縦%d 横%d 漕%d 旋%d 波%d 装%d 耐%d 室%d 砲%d 倉%d  (%s)" % (name, n(7), n(8), n(9), n(10), n(11), n(12), n(13), n(15), n(16), n(17), page))
                else:
                    before = dict(f)
                    for key, value in extra.items():
                        f.setdefault(key, value)
                    # --base: 위키의 기본 성능으로 덮어쓴다. ssjoy 에서 모은 값은 돛 · 내구가 몇 % 씩 다르다(재질이 얹힌 값으로 보인다 —
                    # 원본 카드로 확인한 월광 티클리퍼가 위키 쪽 적는 법과 맞았다)
                    if "--base" in sys.argv:
                        f.update(Adventure=n(4), Trade=n(5), Battle=n(6), Durability=n(13), VerticalSail=n(7), HorizontalSail=n(8),
                                 Turn=n(10), WaveResist=n(11), Armor=n(12), Cabin=n(15), Hold=n(17))
                    patched += f != before
    print("능력치 — 새로 넣은 배", new, "· 빈 칸을 채운 배", patched, "· 이름을 못 이은 배", sorted(missing))
    if "--write" in sys.argv:
        json.dump(facts, open(facts_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("적었다:", facts_path)