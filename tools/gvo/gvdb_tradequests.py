"""gvdb 의 quests.csv 에서 교역 의뢰(交易クエスト) 가운데 「교역품 N개를 어느 도시에 건넨다」가 차례 글에서 읽히는 것을 뽑는다: python gvdb_tradequests.py [--write]

비고(차례 글)의 꼴이 제각각이다 — 「配達品：毛織生地×30 / 届け先：マルセイユ道具屋の主人」 · 「1.サンジョルジュ 若い男 【マテ茶×５樽 渡す】」.
「교역품 이름 × 수」를 찾고(여럿이면 마지막 것), 그 앞에서 가장 가까이 적힌 도시 이름을 건네는 도시로 본다 — 글에서 읽은 것이라 틀린 것이 섞여 있을 수 있다.
보수 · 선금은 도시별 쪽(quest_town_*.html — gvdb_quests.rewards)이나 글의 「報酬：N 前金：M」 · 「報酬・前金 N/M」에서.
→ data\\extracted\\trade-quests-gvdb.json: [{Id, Title, Difficulty, Cities, GoodId, Count, ToCity, Reward, Advance, Skills}]
"""
import json
import os
import re
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_quests
import gvdb_gifts
import gvdb_recipes

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


def number(text):
    return int(text.replace(",", ""))


def main():
    sq = gvdb_recipes.squeeze
    good_by = {sq(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 19, 2).items()}
    ja_city = gvdb_quests.names(10, gvo.LANG_JA)
    city_names = sorted((n for n in ja_city if len(n) >= 2), key=len, reverse=True)
    paid = gvdb_quests.rewards()
    raw = open(os.path.join(ROOT, "gvdb", "quests.csv"), "rb").read().decode("cp932", "replace")
    rows = [r.split("\t") for r in re.split(r"\r?\n(?=\d+\t)", raw)][1:]
    out, skipped = [], {"교역품 없음": 0, "도시 없음": 0, "내는 도시 없음": 0}
    for r in rows:
        if r[4] != "交易クエスト" or r[8]:
            continue
        steps = unicodedata.normalize("NFKC", re.sub(r"<br ?/?>", "\n", r[10]))
        hits = [(m.group(1), int(m.group(2)), m.start()) for m in re.finditer(r"([^\s\n、。:【「(×x]+?)\s*[×x*]\s*(\d+)", steps)]
        hits = [h for h in hits if sq(h[0]) in good_by]
        if not hits:
            skipped["교역품 없음"] += 1
            continue
        good, count, at = hits[-1]
        where = max(((steps[:at].rfind(name), name) for name in city_names), default=(-1, None))
        if where[0] < 0:
            skipped["도시 없음"] += 1
            continue
        givers = [ja_city[t] for t in r[5].split(",") if t in ja_city]
        if not givers:
            skipped["내는 도시 없음"] += 1
            continue
        reward, advance = paid.get(r[1], (0, 0))
        m = re.search(r"報酬[::]\s*([\d,]+).{0,6}前金[::]\s*([\d,]+)", steps) or re.search(r"報酬・前金\s*([\d,]+)\s*/\s*([\d,]+)", steps)
        if m and not reward:
            reward, advance = number(m.group(1)), number(m.group(2))
        out.append(dict(Id=int(r[0]), Title=r[1], Difficulty=int(r[2] or 0), Cities=givers, GoodId=good_by[sq(good)], Count=count, ToCity=ja_city[where[1]],
                        Reward=reward, Advance=advance, Gifts=gvdb_gifts.gifts(r[9])[0], Skills=[{"Name": s, "Rank": int(k)} for s, k in re.findall(r"([^\s,()()]+?)\((\d+)\)", unicodedata.normalize("NFKC", r[6]))]))
    print(len(out), "건 · 뺀 것", skipped, "· 보수를 아는 것", sum(1 for q in out if q["Reward"]), "· 내는 도시와 건네는 도시가 같은 것", sum(1 for q in out if q["ToCity"] in q["Cities"]))
    for q in out[:5]:
        print(" ", q)
    if "--write" in sys.argv:
        json.dump(out, open(os.path.join(ROOT, "trade-quests-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
        print("wrote trade-quests-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
