"""gvdb 의 도시 쪽(data\\extracted\\gvdb\\town_N.html — 받아 둔 것만)에서 교역품 말고 적힌 것을 뽑는다: python gvdb_towns.py [--write]

쪽의 「都市・港の詳細」: 種類 · 地域名 · 海域名 · 必要言語 · 投資報酬 · 施設／ＮＰＦ(이름 줄과 ○ 줄이 번갈아) · 備考(文化圏：…)
→ data\\extracted\\town-facts-gvdb.json: [{CityId, Kind, Language(언어 스킬 번호들), LanguageName, Facilities: [일본어 이름…], Culture}]
언어 이름은 클라이언트 스킬 표의 일본어 이름으로 번호에 잇는다. 도시는 쪽 제목의 이름으로 클라이언트 도시 표(10)에 잇는다.
"""
import glob
import html
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_quests
import gvdb_recipes
import skills as skill_table

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


# 발견물 종류(gvdb 의 種別) — 보고를 받는 사람 표의 둘째 칸
KINDS = ("史跡", "宗教建築", "歴史遺物", "宗教遺物", "美術品", "財宝", "化石", "植物", "虫類", "鳥類", "小型生物", "中型生物", "大型生物", "海洋生物", "港・集落", "地理", "天文", "気象・現象", "伝承", "レリック")


def text(cell):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", cell))).strip()


def main():
    sq = gvdb_recipes.squeeze
    city_by = {sq(n): i for n, i in gvdb_quests.names(10, gvo.LANG_JA).items()}
    skill_by = {sq(r["name"]): r["id"] for r in skill_table.skills(gvo.data_tables(gvo.LANG_JA)[skill_table.T_SKILL]) if r["name"] and r["id"] < 2000}
    item_by = {sq(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 14, 0).items() if n}
    rows, lost, tongues = [], [], {}
    for path in sorted(glob.glob(os.path.join(ROOT, "gvdb", "town_*.html"))):
        raw = open(path, "rb").read()
        page = raw.decode("utf-8", "replace") if "都市" in raw.decode("utf-8", "replace") else raw.decode("cp932", "replace")
        m = re.search(r"都市・港【(.+?)】の詳細", page)
        if not m or sq(m.group(1)) not in city_by:
            lost.append((os.path.basename(path), m.group(1) if m else "?"))
            continue
        cells = [text(c) for c in re.findall(r"<t[dh][^>]*>(.*?)</t[dh]>", page, re.S)]

        def after(label):
            return next((cells[i + 1] for i, c in enumerate(cells[:-1]) if c == label), "")

        # 시설 표 — 이름 여덟 칸 다음 줄에 ○ 여덟 칸
        facilities = []
        start = next((i for i, c in enumerate(cells) if c == "施設／ＮＰＣ"), None)
        if start is not None:
            i = start + 1
            while i + 16 <= len(cells) and all(c in ("○", "") for c in cells[i + 8:i + 16]) and not any(c in ("○", "") for c in cells[i:i + 8]):
                facilities += [n for n, on in zip(cells[i:i + 8], cells[i + 8:i + 16]) if on == "○"]
                i += 16
        # 발견물 보고를 받는 사람 — 「名前 | 発見物報告 | 居場所」 머리 줄 다음에 셋씩
        reporters = []
        head = next((i for i in range(len(cells) - 2) if cells[i:i + 3] == ["名前", "発見物報告", "居場所"]), None)
        if head is not None:
            i = head + 3
            while i + 3 <= len(cells) and cells[i] and cells[i + 1] in KINDS:
                reporters.append([cells[i], cells[i + 1]])
                i += 3
        language = after("必要言語")
        ids = [skill_by[sq(language)]] if sq(language) in skill_by else [skill_by[sq(part)] for part in re.split(r"[、,／/\s]+", language) if sq(part) in skill_by]
        tongues[language] = tongues.get(language, 0) + 1
        culture = re.search(r"文化圏：([^\s<／/]+)", page)
        # 投資報酬 — 「北海の名物料理集 （必要投資額：1,000,000ドゥカード）」: 그만큼 투자하면 받는 물건
        reward = re.match(r"(.+?)\s*（必要投資額：([\d,]+)", after("投資報酬"))
        gift = dict(InvestReward=item_by.get(sq(reward.group(1)), 0), InvestRewardName=reward.group(1), InvestNeed=int(reward.group(2).replace(",", ""))) if reward else {}
        rows.append(dict(CityId=city_by[sq(m.group(1))], Kind=after("都市・港の種類"), Language=ids, LanguageName=language, Facilities=facilities,
                         Culture=culture.group(1) if culture else "", Reporters=reporters, **gift))
    print(len(rows), "도시 · 못 이은 쪽", lost[:8])
    print("언어", sorted(tongues.items(), key=lambda kv: -kv[1]))
    print("언어 번호를 못 이은 도시", [(r["CityId"], r["LanguageName"]) for r in rows if r["LanguageName"] and not r["Language"]][:12])
    print("투자 보수", sum(1 for r in rows if r.get("InvestNeed")), "도시 · 아이템 번호를 못 이은 것", [r["InvestRewardName"] for r in rows if r.get("InvestNeed") and not r["InvestReward"]])
    import collections
    took = collections.Counter(k for r in rows for _, k in r["Reporters"])
    print("발견물 보고를 받는 사람", sum(len(r["Reporters"]) for r in rows), "명 ·", sum(1 for r in rows if r["Reporters"]), "도시 ·", took.most_common())
    for r in rows[:3]:
        print(r)
    if "--write" in sys.argv:
        json.dump(sorted(rows, key=lambda r: r["CityId"]), open(os.path.join(ROOT, "town-facts-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
        print("wrote town-facts-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
