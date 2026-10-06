"""위키(wikiwiki.jp/gvo) 사본에서 장비가 올려 주는 스킬을 뽑아 data\\extracted\\gear-boosts.json 을 만든다: python wiki_gear.py [--write]

클라이언트의 장비 표(15)에는 공격력 · 방어력 · 정장도 · 변장도 · 내구도뿐이고 스킬 보정이 없다(서버가 주는 것으로 보인다).
사본(data\\extracted\\wiki\\*.html)의 표에서, 첫 칸이 장비의 일본어 이름이고 같은 줄에 「스킬 이름＋n」이 적힌 것을 모은다.
장비 이름과 스킬 이름은 클라이언트 표의 일본어 ↔ 한국어(번호가 같다)로 잇는다. 이 도구는 위키에 접속하지 않는다.
"""
import glob
import json
import os
import re
import struct
import sys

import gvo
import skills as skill_table
from wiki_ships import EXTRACTED, squeeze, tables


def gear_names(lang):
    t = bytes(gvo.data_tables(lang)[15])
    out, off = {}, 4
    for _ in range(struct.unpack_from("<I", t, 0)[0]):
        rid = struct.unpack_from("<I", t, off)[0]
        name, o = gvo.packed_string(t, off + 4, rid)
        _, o = gvo.packed_string(t, o, rid)
        out[rid] = name
        off = o + 26
    return out


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    gear = {}
    for rid, name in gear_names(gvo.LANG_JA).items():
        if name and not name.startswith("※"):
            gear.setdefault(squeeze(name), []).append(rid)
    korean = gear_names(gvo.LANG_KO)
    ja = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_JA)[skill_table.T_SKILL])}
    ko = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_KO)[skill_table.T_SKILL])}
    skill = {squeeze(n): i for i, n in sorted(ja.items(), reverse=True) if n and i < 2000}
    found, unknown = {}, set()
    for path in sorted(glob.glob(os.path.join(EXTRACTED, "wiki", "*.html"))):
        for rows in tables(path):
            for r in rows:
                if not r or squeeze(re.sub(r"[(（].*$", "", r[0])) not in gear:
                    continue
                boosts = {}
                for name, amount in re.findall(r"([^\s、,。/()（）：:＋+\d]+)\s*[＋+]\s*(\d+)", " ".join(r[1:])):
                    if squeeze(name) in skill:
                        boosts[skill[squeeze(name)]] = int(amount)
                    elif not re.search(r"攻撃|防御|正装|変装|耐久|性能|行動力", name):
                        unknown.add(name)
                if boosts:
                    for rid in gear[squeeze(re.sub(r"[(（].*$", "", r[0]))]:
                        found[rid] = boosts
                        print("  %d %s: %s  (%s)" % (rid, korean.get(rid), " · ".join("%s +%d" % (ko.get(s), a) for s, a in boosts.items()), os.path.basename(path)))
    print("장비", len(found), "· 못 이은 스킬 이름", sorted(unknown))
    if "--write" in sys.argv:
        target = os.path.join(EXTRACTED, "gear-boosts.json")
        json.dump({str(k): {str(s): a for s, a in v.items()} for k, v in sorted(found.items())}, open(target, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("적었다:", target)