"""gvdb 의 아이템 목록(items.csv)에서 요리(消耗品（料理）)의 효과를 뽑는다: python gvdb_food.py [--write]

설명 칸: 「…<br/>使用時効果：行動力・疲労回復（複数）<br/>行動力：+20<br/>疲労度：-12」
→ data\\extracted\\food-effects-gvdb.json: {아이템 번호: [행동력, 피로를 푸는 양]}
게임은 행동력을 클라이언트 설명 글의 「행동력+n」에서 읽는다 — 여기서는 피로를 푸는 양(클라이언트 글에는 「피로 회복」이라는 말뿐이다)을 쓴다.
"""
import json
import os
import re
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_recipes

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


def main():
    sq = gvdb_recipes.squeeze
    ja = gvdb_recipes.names(gvo.LANG_JA, 14, 0)
    ko = gvdb_recipes.names(gvo.LANG_KO, 14, 0)
    by_name = {}
    for i, n in ja.items():
        if n:
            by_name.setdefault(sq(n), i)
    out, lost, tired = {}, [], 0
    for line in open(os.path.join(ROOT, "gvdb", "items.csv"), encoding="cp932", errors="replace"):
        c = line.rstrip("\n").split("\t")
        if len(c) < 8 or c[2] != "消耗品（料理）":
            continue
        text = unicodedata.normalize("NFKC", c[7])
        vigour = re.search(r"行動力:\s*\+?(\d+)", text)
        fatigue = re.search(r"疲労度?:\s*-(\d+)", text)
        if not vigour and not fatigue:
            continue
        item = by_name.get(sq(c[1]))
        if item is None:
            lost.append(c[1])
            continue
        out[item] = [int(vigour.group(1)) if vigour else 0, int(fatigue.group(1)) if fatigue else 0]
        tired += fatigue is not None
    print(len(out), "가지 · 피로를 푸는 것", tired, "· 이름을 못 이은 것", len(lost), lost[:10])
    # 게임이 읽는 클라이언트 글의 행동력과 견준다
    same = differ = 0
    for item, (vigour, _) in out.items():
        pass
    print("보기", [(ko.get(i), v) for i, v in list(out.items())[:6]], "· 피로:", [(ko.get(i), v) for i, v in out.items() if v[1]][:8])
    if "--write" in sys.argv:
        json.dump(dict(sorted(out.items())), open(os.path.join(ROOT, "food-effects-gvdb.json"), "w", encoding="utf-8"), indent=0)
        print("wrote food-effects-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
