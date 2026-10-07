"""gvdb 의 레시피 쪽(data\\extracted\\gvdb\\recipe_*.html)에서 레시피의 재료 · 생산물 · 수량을 뽑는다.

쪽의 줄: 레시피 이름 | 스킬(랭크) … | 생산물 xN … | 재료 xN …
일본어 이름을 클라이언트의 일본어판 표(레시피 16 · 교역품 19 · 아이템 14)로 번호에 잇는다.
이 게임의 생산은 창고의 교역품만 재료로 쓰므로, 재료가 모두 교역품인 레시피만 적는다.
결과: data\\extracted\\recipe-inputs.json — [{RecipeId, Name, Output, OutputItem, OutputCount, Inputs, Skill}]
"""
import glob
import html
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "data", "extracted")


def names(lang, table, tail):
    """(id, 이름, 설명, tail 바이트) 꼴의 표에서 id → 이름."""
    t = bytes(gvo.data_tables(lang)[table])
    out, at = {}, 4
    for _ in range(struct.unpack_from("<I", t, 0)[0]):
        rid = struct.unpack_from("<I", t, at)[0]
        name, o = gvo.packed_string(t, at + 4, rid)
        _, o = gvo.packed_string(t, o, rid)
        out[rid] = name
        at = o + tail
    return out


def squeeze(s):
    return re.sub(r"[\s　・･]", "", s)


def pairs(cell):
    """「이름 xN」들 — (이름, 수)"""
    out = []
    for name, count in re.findall(r"<a [^>]*>([^<]+)</a>\s*x(\d+)", cell):
        out.append((html.unescape(name).strip(), int(count)))
    return out


def main():
    ja_recipes, ko_recipes = names(gvo.LANG_JA, 16, 0), names(gvo.LANG_KO, 16, 0)
    ja_goods, ko_skills_ja = names(gvo.LANG_JA, 19, 2), None
    ja_items = {}
    t = bytes(gvo.data_tables(gvo.LANG_JA)[14])
    at = 4
    try:
        import wiki_ships  # noqa: F401 — 아이템 표의 줄 길이는 거기서 읽는 법을 따른다
    except Exception:
        pass
    recipe_by = {}
    for rid, name in ja_recipes.items():
        recipe_by.setdefault(squeeze(name), []).append(rid)
    good_by = {squeeze(n): i for i, n in ja_goods.items()}
    # 스킬 이름(일본어 → 우리말) — 표 6
    ja_skill, ko_skill = skill_names(gvo.LANG_JA), skill_names(gvo.LANG_KO)
    skill_by = {squeeze(n): ko_skill.get(i, "") for i, n in ja_skill.items()}

    out, stats = [], dict(rows=0, no_recipe=0, not_goods=0, no_output=0, twice=0)
    seen = set()
    for path in sorted(glob.glob(os.path.join(ROOT, "gvdb", "recipe_*.html"))):
        page = open(path, encoding="utf-8").read()
        for row in re.findall(r"<tr><td><a href=\"[^\"]*RecipeShow\?id=\d+\">[\s\S]*?</tr>", page):
            cells = re.findall(r"<td[^>]*>([\s\S]*?)</td>", row)
            if len(cells) < 4:
                continue
            stats["rows"] += 1
            title = html.unescape(re.sub(r"<[^>]+>", "", cells[0])).strip()
            ids = recipe_by.get(squeeze(title), [])
            if not ids:
                stats["no_recipe"] += 1
                continue
            skills = [(skill_by.get(squeeze(html.unescape(n)), ""), int(r)) for n, r in re.findall(r">([^<]+)</a>\((\d+)\)", cells[1])]
            made, used = pairs(cells[2]), pairs(cells[3])
            if not made or not used:
                stats["no_output"] += 1
                continue
            if any(squeeze(n) not in good_by for n, _ in used) or squeeze(made[0][0]) not in good_by:
                stats["not_goods"] += 1
                continue
            # 같은 이름의 레시피가 여럿이면(책마다 하나씩) 아직 안 쓴 번호에 차례로 붙인다
            rid = next((i for i in ids if i not in seen), None)
            if rid is None:
                stats["twice"] += 1
                continue
            seen.add(rid)
            out.append(dict(
                RecipeId=rid, Name=ko_recipes.get(rid, title),
                Output=good_by[squeeze(made[0][0])], OutputCount=made[0][1],
                Inputs=",".join(f"{good_by[squeeze(n)]}:{c}" for n, c in used),
                Skill=" ".join(f"{n} {r}" for n, r in skills[:1] if n)))
    out.sort(key=lambda r: r["RecipeId"])
    print(stats, "→", len(out))
    for r in out[:12]:
        print(r)
    if "--write" in sys.argv:
        with open(os.path.join(ROOT, "recipe-inputs.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
        print("wrote recipe-inputs.json")


def skill_names(lang):
    """스킬 표(6): id, 이름 — 뒤는 줄마다 달라 이름만 순서대로 못 읽는다. 뽑아 둔 skills.json 과 일본어판을 같은 법으로 읽는다."""
    t = bytes(gvo.data_tables(lang)[6])
    out, at = {}, 4
    count = struct.unpack_from("<I", t, 0)[0]
    # 줄 길이를 모르므로 다음 줄의 번호(앞 줄 + 1 … 가까운 수)를 찾아 건너뛴다
    for _ in range(count):
        rid = struct.unpack_from("<I", t, at)[0]
        name, o = gvo.packed_string(t, at + 4, rid)
        _, o = gvo.packed_string(t, o, rid)
        out[rid] = name
        nxt = None
        for skip in range(0, 64):
            if o + skip + 6 > len(t):
                break
            cand = struct.unpack_from("<I", t, o + skip)[0]
            if rid < cand <= rid + 40:
                ln = struct.unpack_from("<H", t, o + skip + 4)[0]
                if 0 < ln < 200:
                    nxt = o + skip
                    break
        if nxt is None:
            break
        at = nxt
    return out


if __name__ == "__main__":
    main()
