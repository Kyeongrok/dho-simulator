"""gvdb 의 아이템 목록(items.csv)에서 선수상(船部品（船首像）)의 「使用時効果」를 뽑는다: python gvdb_figureheads.py [--write]

설명 칸: 「耐久度：60<br/>使用時効果：セイレーン撃退<br/>災害守護：5<br/>疲労軽減：6<br/>船員掌握：1<br/>砲弾回避：4」
이름은 클라이언트 선수상 표(27)의 일본어 이름으로 번호에 잇는다.
→ data\\extracted\\figurehead-uses-gvdb.json: {선수상 번호: 쓰는 효과} — 효과는 gvdb 의 글 그대로(일본어).
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_recipes

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


def main():
    sq = gvdb_recipes.squeeze
    ja = {sq(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 27, 20).items()}
    ko = gvdb_recipes.names(gvo.LANG_KO, 27, 20)
    out, lost, total = [], [], 0
    for line in open(os.path.join(ROOT, "gvdb", "items.csv"), encoding="cp932", errors="replace"):
        c = line.rstrip("\n").split("\t")
        if len(c) < 8 or c[2] != "船部品（船首像）":
            continue
        total += 1
        use = re.search(r"使用時効果：([^<]+)", c[7])
        if not use:
            continue
        part = ja.get(sq(c[1]))
        if part is None:
            lost.append(c[1])
            continue
        out.append(dict(Id=part, Use=use.group(1).strip()))
    out.sort(key=lambda r: r["Id"])
    print(total, "줄 가운데 쓰는 효과가 적힌 것", len(out), "· 이름을 못 이은 것", lost)
    for r in out:
        print(" ", r["Id"], ko.get(r["Id"]), "—", r["Use"])
    if "--write" in sys.argv:
        json.dump({str(r["Id"]): r["Use"] for r in out}, open(os.path.join(ROOT, "figurehead-uses-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
        print("wrote figurehead-uses-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
