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
import difflib
import unicodedata
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
    # 전각 · 반각(괄호 · 숫자 · 영문)이 섞여 적힌 이름도 같게 본다 — 「副官の航海日誌(交易)」 = 「…（交易）」
    return re.sub(r"[\s　・･]", "", unicodedata.normalize("NFKC", s))


def pairs(cell):
    """「이름 xN」들 — (이름, 수)"""
    out = []
    # 수가 안 적힌 생산물(나오는 수가 그때그때 다른 것)은 1 로 친다
    for name, count in re.findall(r"<a [^>]*>([^<]+)</a>\s*(?:x(\d+))?", cell):
        out.append((html.unescape(name).strip(), int(count or 1)))
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
    # gvdb 쪽에 제 이름 그대로 적힌 레시피 — 비슷한 이름으로 이을 때 이것들의 번호는 건드리지 않는다
    exact_titles = set()
    for path in glob.glob(os.path.join(ROOT, "gvdb", "recipe_*.html")):
        for cell in re.findall(r"<tr><td><a href=\"[^\"]*RecipeShow\?id=\d+\">([\s\S]*?)</a>", open(path, encoding="utf-8").read()):
            exact_titles.add(squeeze(html.unescape(re.sub(r"<[^>]+>", "", cell)).strip()))
    recipe_keys = [k for k in recipe_by if k not in exact_titles]
    # 스킬 이름(일본어 → 우리말) — 표 6
    try:
        import skills as skill_table
        ja_skill = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_JA)[skill_table.T_SKILL])}
        ko_skill = {r["id"]: r["name"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_KO)[skill_table.T_SKILL])}
    except Exception as e:
        print("skill table:", e)
        ja_skill, ko_skill = skill_names(gvo.LANG_JA), skill_names(gvo.LANG_KO)
    skill_by = {squeeze(n): ko_skill.get(i, "") for i, n in ja_skill.items()}
    # 스킬 표를 어림으로 읽어 빠지는 줄이 있다 — 생산 스킬은 손으로도 적어 둔다
    skill_by.update({"縫製": "봉제", "鋳造": "주조", "工芸": "공예", "調理": "조리", "保管": "보관", "錬金術": "연금술", "言語学": "언어학", "造船": "조선"})

    # 레시피 책: 쪽이 책마다 묶여 있다(■책 이름 줄 아래에 그 책의 레시피들). 책 = 아이템 표(14)의 아이템, 값은 items.csv(있으면)
    ja_item, ko_item = names(gvo.LANG_JA, 14, 0), names(gvo.LANG_KO, 14, 0)
    item_by = {squeeze(n): i for i, n in ja_item.items()}
    # 장비(옷 · 무기 — 표 15)도 아이템으로 친다: 소지품에 같은 번호로 들어간다. 이름이 겹치면 아이템 표가 먼저
    try:
        import wiki_gear
        for gid, gname in wiki_gear.gear_names(gvo.LANG_JA).items():
            item_by.setdefault(squeeze(gname), gid)
    except Exception as e:
        print("gear names:", e)
    # 선박 부품(돛 25 · 장갑 23 · 선수상 27 · 문장 26 · 대포 22 · 특수장비 24)도 생산물 · 재료가 된다 — 번호가 부품 표의 것이라 게임이 부품 창고로 넣고 뺀다.
    # 이름이 아이템 · 장비와 겹치면 그쪽이 먼저(줄 꼬리 길이는 DataTables.cs 의 읽는 법과 같다)
    for table, tail in ((25, 16), (23, 8), (27, 20), (26, 0), (22, 40), (24, 22)):
        try:
            for pid, pname in names(gvo.LANG_JA, table, tail).items():
                if pname and not pname.startswith("※"):
                    item_by.setdefault(squeeze(pname), pid)
        except Exception as e:
            print("part table", table, e)
    prices = {}
    csv_path = os.path.join(ROOT, "gvdb", "items.csv")
    if os.path.exists(csv_path):
        for line in open(csv_path, encoding="cp932", errors="replace").read().splitlines():
            c = line.split("\t")
            if len(c) > 5 and c[2] == "レシピ帳" and c[5].isdigit():
                prices[squeeze(c[1])] = int(c[5])
    books, book_miss = {}, set()

    out, stats = [], dict(rows=0, no_recipe=0, not_goods=0, no_output=0, twice=0)
    seen, booked = set(), set()
    for path in sorted(glob.glob(os.path.join(ROOT, "gvdb", "recipe_*.html"))):
        page = open(path, encoding="utf-8").read()
        book, facility = None, ""
        for row in re.findall(r"<tr><th class=\"group_name\"[\s\S]*?</tr>|<tr><td><a href=\"[^\"]*RecipeShow\?id=\d+\">[\s\S]*?</tr>", page):
            if row.startswith("<tr><th"):
                name = html.unescape(re.sub(r"<[^>]+>", "", row)).replace("■", "").strip()
                book = item_by.get(squeeze(name))
                # 책이 아니라 실험 설비로 하는 묶음: 「上級錬金術（実験台）」 · 「…（実験炉）」
                facility = "Bench" if "実験台" in name else "Furnace" if "実験炉" in name else ""
                if book is None:
                    book_miss.add(name)
                else:
                    books.setdefault(book, dict(ItemId=book, Name=ko_item.get(book, name), Price=prices.get(squeeze(name), 0), Recipes=[]))
                continue
            cells = re.findall(r"<td[^>]*>([\s\S]*?)</td>", row)
            if len(cells) < 4:
                continue
            stats["rows"] += 1
            title = html.unescape(re.sub(r"<[^>]+>", "", cells[0])).strip()
            ids = recipe_by.get(squeeze(title), [])
            near = False
            if not ids:
                # gvdb 쪽의 오타 · 조사 차이(「練成法」 ↔ 「錬成法」 · 「装材製法」 ↔ 「装材の製法」)로 안 맞는 이름 — 수가 같은 가장 비슷한 이름 하나(0.85 이상)에 잇고 NameGuessed 로 알린다
                key = squeeze(title)
                for cand in difflib.get_close_matches(key, recipe_keys, n=3, cutoff=0.85):
                    if re.findall(r"\d+", cand) == re.findall(r"\d+", key):
                        ids, near = recipe_by[cand], True
                        break
            if not ids:
                stats["no_recipe"] += 1
                continue
            if book is not None:
                # 같은 이름의 레시피가 여러 책에 있으면 번호를 차례로 나눠 준다
                in_book = next((i for i in ids if i not in booked), ids[0])
                booked.add(in_book)
                books[book]["Recipes"].append(in_book)
            skills = [(skill_by.get(squeeze(html.unescape(n)), ""), int(r)) for n, r in re.findall(r">([^<]+)</a>\((\d+)\)", cells[1])]
            # 랭크가 안 적힌 줄(135건 — 「調理」뿐)은 스킬만 잇고 랭크는 1 로 둔다(모르는 값 — RankGuessed 로 알린다)
            guessed = not skills
            if guessed:
                skills = [(skill_by.get(squeeze(n), ""), 1) for n in re.split(r"[\s,、]+", html.unescape(re.sub(r"<[^>]+>", " ", cells[1])).strip()) if n]
            made, used = pairs(cells[2]), pairs(cells[3])
            if not made or not used:
                stats["no_output"] += 1
                continue
            # 생산물이 교역품이 아니라 아이템(요리 · 도구 …)이면 OutputItem 으로 적는다 — 재료는 여전히 교역품뿐이어야 한다
            if len(made) > 1:
                stats["multi"] = stats.get("multi", 0) + 1
                if "--multi" in sys.argv:
                    print("여럿:", title, "|", made, "|", used, "|", [s for s in skills])
            made_key = squeeze(made[0][0])
            # 재료에 아이템(재봉도구 · 자수실 …)이 끼는 레시피도 적는다 — 번호가 교역품(16…)이 아니면 게임이 소지품에서 뺀다
            if any(squeeze(n) not in good_by and squeeze(n) not in item_by for n, _ in used) or (made_key not in good_by and made_key not in item_by):
                stats["not_goods"] += 1
                continue
            # 같은 이름의 레시피가 여럿이면(책마다 하나씩) 아직 안 쓴 번호에 차례로 붙인다
            rid = next((i for i in ids if i not in seen), None)
            if rid is None:
                stats["twice"] += 1
                continue
            if not any(n for n, _ in skills):
                stats["no_skill"] = stats.get("no_skill", 0) + 1
                continue
            skills = [s for s in skills if s[0]]
            seen.add(rid)
            out.append(dict(
                RecipeId=rid, Name=ko_recipes.get(rid, title),
                Output=good_by.get(made_key, 0), OutputItem=0 if made_key in good_by else item_by[made_key], OutputCount=made[0][1],
                Inputs=",".join(f"{good_by.get(squeeze(n)) or item_by[squeeze(n)]}:{c}" for n, c in used),
                Skill=" ".join(f"{n} {r}" for n, r in skills[:1] if n), Facility=facility, **({"RankGuessed": True} if guessed else {}), **({"NameGuessed": True} if near else {}),
                # 생산물이 둘 적힌 줄(72건 — 「キャノン砲12門 / 名匠キャノン砲12門」 · 「ガーネット / ルビー」 · 「高級ガーネット×5 / 最高級ガーネット×1」)의 둘째 = 대성공 때 나오는 것(짐작 —
                # gvdb 의 다른 줄에 「成功１　大成功２」가 있고 둘째가 늘 윗급이다). 교역품끼리일 때만 싣는다
                **({"GreatOutput": good_by[squeeze(made[1][0])], "GreatCount": made[1][1]} if len(made) > 1 and made_key in good_by and squeeze(made[1][0]) in good_by else {})))
    out.sort(key=lambda r: r["RecipeId"])
    print(stats, "→", len(out))
    for r in out[:12]:
        print(r)
    if "--write" in sys.argv:
        with open(os.path.join(ROOT, "recipe-inputs.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
        with open(os.path.join(ROOT, "recipe-books.json"), "w", encoding="utf-8") as f:
            json.dump(sorted(books.values(), key=lambda b: b["ItemId"]), f, ensure_ascii=False, indent=1)
        print("wrote recipe-inputs.json, recipe-books.json")
    print("books", len(books), "recipes in books", sum(len(b["Recipes"]) for b in books.values()), "| book names not found", len(book_miss), sorted(book_miss)[:8])


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
