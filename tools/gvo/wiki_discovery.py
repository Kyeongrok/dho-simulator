"""위키(wikiwiki.jp/gvo) 사본에서 발견물의 난도 · 찾는 법을 뽑아 data\\extracted\\discovery-facts.json 에 넣는다: python wiki_discovery.py [--write]

받지는 않는다 — data\\extracted\\wiki\\Discovery_*.html 사본만 읽는다.
쪽의 표: 名称 · ランク(별) · ポイント · 説明 · 難度(필요 스킬 랭크) · 経験値 · 発見方法(의뢰 이름, 또는 「…の地図」 = 서고의 지도).
일본어 이름을 클라이언트의 일본어 발견물 표(표 32)로 번호에 잇는다.
"""
import glob
import html
import json
import os
import re
import struct
import sys

import gvo

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "data", "extracted")


def client_names(lang):
    t = gvo.data_tables(lang)[32]
    count = struct.unpack_from("<I", t, 0)[0]
    at, names = 4, {}
    for _ in range(count):
        rid = struct.unpack_from("<I", t, at)[0]
        name, o = gvo.packed_string(t, at + 4, rid)
        _, o = gvo.packed_string(t, o, rid)
        kind = struct.unpack_from("<H", t, o)[0]
        names[rid] = (name, kind)
        at = o + 14
    return names


def cells(row):
    return [html.unescape(re.sub(r"<[^>]+>", "", c)).strip() for c in re.findall(r"<t[hd][^>]*>([\s\S]*?)</t[hd]>", row)]


def main():
    ja = client_names(gvo.LANG_JA)
    by_name = {}
    for rid, (name, kind) in ja.items():
        by_name.setdefault(name, []).append((rid, kind))
    facts, missed = {}, []
    for path in sorted(glob.glob(os.path.join(ROOT, "wiki", "Discovery_*.html"))):
        page = open(path, encoding="utf-8").read()
        for table in re.findall(r"<table[\s\S]*?</table>", page):
            for row in re.findall(r"<tr[\s\S]*?</tr>", table):
                c = cells(row)
                if len(c) < 7 or c[0] == "名称" or not c[4].isdigit():
                    continue
                hit = by_name.get(c[0])
                if not hit:
                    missed.append(c[0])
                    continue
                for rid, _ in hit:
                    facts[rid] = {"Id": rid, "Japanese": c[0], "Difficulty": int(c[4]), "Method": c[6], "Map": c[6].endswith("地図")}
    print(len(facts), "개를 이었다. 못 이은 이름", len(missed), missed[:12])
    by_method = {}
    for f in facts.values():
        by_method[f["Method"]] = by_method.get(f["Method"], 0) + 1
    print("찾는 법", len(by_method), "가지 — 지도", sum(1 for f in facts.values() if f["Map"]), "개")
    if "--write" in sys.argv:
        with open(os.path.join(ROOT, "discovery-facts.json"), "w", encoding="utf-8") as out:
            json.dump(sorted(facts.values(), key=lambda f: f["Id"]), out, ensure_ascii=False, indent=1)
        print("discovery-facts.json 에 적었다.")


if __name__ == "__main__":
    main()
