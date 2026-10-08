"""gvdb 의 quests.csv 에서 해사 의뢰(海事クエスト) 가운데 「어느 바다에서 어떤 배 몇 척과 싸운다」가 차례 글에서 읽히는 것을 뽑는다: python gvdb_seaquests.py [--write]

차례 글의 꼴: 「4.アテネの西、コリント湾にて戦闘 / 討伐対象「アイトリア海賊」 / ソロ時：バルシャ×3」 · 「ルソン島東（5700.4250）偽装漁船団（ソロ：重ガレアス４〜６隻）」 · 「旗艦：耐久211，船員30名」
- 싸우는 배: 배 이름(ship-names.csv 의 일본어 → 한국어) 뒤의 「×N〜M」 · 「N〜M隻」 — 처음 것(혼자일 때의 편제가 먼저 적힌다)
- 자리: 글의 좌표 「(5700.4250)」가 있으면 그것, 없으면 글에 적힌 바다 이름(클라이언트 바다 표 8) — 둘 다 없으면 뺀다
- 상대의 이름(討伐対象「…」)은 일본어뿐이라 그대로 싣는다(게임은 안 쓴다) · 내구 · 선원은 적힌 것만
보수 · 선금은 도시별 쪽(gvdb_quests.rewards)이나 글의 「報酬：N 前金：M」.
→ data\\extracted\\sea-quests-gvdb.json: [{Id, Title, Difficulty, Cities, Ship, CountMin, CountMax, X, Y, SeaZone, Target, Durability, Crew, Reward, Advance, Skills}]
"""
import csv
import json
import os
import re
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_quests
import gvdb_gifts
from wiki_ships import squeeze

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


def number(text):
    return int(text.replace(",", ""))


def main():
    ships = {squeeze(r["日本語"]): r["한국어"] for r in csv.DictReader(open(os.path.join(ROOT, "ship-names.csv"), encoding="utf-8-sig")) if r["日本語"] not in ("", "※")}
    ship_names = sorted((n for n in ships if len(n) >= 3), key=len, reverse=True)
    ja_city = gvdb_quests.names(10, gvo.LANG_JA)
    ja_sea = gvdb_quests.names(8, gvo.LANG_JA)
    sea_names = sorted((n for n in ja_sea if len(n) >= 3), key=len, reverse=True)
    paid = gvdb_quests.rewards()
    raw = open(os.path.join(ROOT, "gvdb", "quests.csv"), "rb").read().decode("cp932", "replace")
    rows = [r.split("\t") for r in re.split(r"\r?\n(?=\d+\t)", raw)][1:]
    out, skipped, total, guessed = [], {"배 없음": 0, "자리 없음": 0, "내는 도시 없음": 0, "선행 의뢰": 0}, 0, 0
    for r in rows:
        if len(r) < 11 or r[4] != "海事クエスト":
            continue
        total += 1
        steps = unicodedata.normalize("NFKC", re.sub(r"<br ?/?>", "\n", r[10]))
        flat = squeeze(steps)
        # 배 이름 바로 뒤의 수 — 「バルシャ×3」 · 「フリュート2~3隻」 · 「重ガレアス4~6隻」
        fleet = None
        for name in ship_names:
            m = re.search(re.escape(name) + r"(?:[×x*]|\()?(\d+)(?:[~〜\-](\d+))?(?:隻)?", flat)
            if m and (fleet is None or m.start() < fleet[3]):
                low = int(m.group(1))
                high = int(m.group(2) or low)
                if 1 <= low <= high <= 12:
                    fleet = (ships[name], low, high, m.start())
        # 수가 조금 떨어져 적힌 것 — 「大型ガレオン(ソロで4隻~6隻)」 · 「1等戦列艦?3隻」
        if fleet is None:
            for name in ship_names:
                m = re.search(re.escape(name) + r"[^\d]{1,7}(\d{1,2})隻?(?:[~〜\-](\d{1,2})隻?)?(?!\d)", flat)
                if m and (fleet is None or m.start() < fleet[3]):
                    low = int(m.group(1))
                    high = int(m.group(2) or low)
                    if 1 <= low <= high <= 12:
                        fleet = (ships[name], low, high, m.start())
        # 배 이름만 있고 수가 없는 것 — 한 척으로 본다(짐작)
        if fleet is None:
            at = min(((flat.find(name), name) for name in ship_names if name in flat), default=None)
            if at:
                fleet = (ships[at[1]], 1, 1, at[0])
                guessed += 1
        if fleet is None:
            skipped["배 없음"] += 1
            continue
        # 좌표 — 괄호 안의 것 · 점으로 가른 것 · 「付近 / 辺り」가 붙은 것만(「報酬：109,600」 같은 돈을 좌표로 읽지 않게)
        spot = (re.search(r"\((\d{2,5})\s*[.,、]\s*(\d{3,4})\)", steps) or re.search(r"(?<![\d,])(\d{3,5})\s*[.、]\s*(\d{4})(?!\d)", steps)
                or re.search(r"(?<![\d,])(\d{3,5}),\s*(\d{4})(?:付近|辺り|あたり)", steps))
        x, y = (int(spot.group(1)), int(spot.group(2))) if spot and int(spot.group(1)) < 16384 and int(spot.group(2)) < 8192 else (0, 0)
        sea = next((n for n in sea_names if n in steps), None)
        if not x and sea is None:
            skipped["자리 없음"] += 1
            continue
        givers = [ja_city[t] for t in r[5].split(",") if t in ja_city]
        if not givers:
            skipped["내는 도시 없음"] += 1
            continue
        if r[8].strip():
            skipped["선행 의뢰"] += 1
            continue
        reward, advance = paid.get(r[1], (0, 0))
        m = re.search(r"報酬[::]\s*([\d,]+).{0,6}前金[::]\s*([\d,]+)", steps)
        if m and not reward:
            reward, advance = number(m.group(1)), number(m.group(2))
        target = re.search(r"(?:討伐対象|対象)[::]?\s*「?([^\n」(]{2,24})」?", steps) or re.search(r"「([^」\n]{2,20}(?:海賊|艦隊|船団|私掠|商船隊))」", steps)
        hull = re.search(r"耐久[::]?\s*(\d{2,4})", steps)
        crew = re.search(r"船員[::]?\s*(\d{1,3})", steps)
        out.append(dict(Id=int(r[0]), Title=r[1], Difficulty=int(r[2] or 0), Cities=givers, Ship=fleet[0], CountMin=fleet[1], CountMax=fleet[2], X=x, Y=y,
                        SeaZone=ja_sea[sea] if sea else 0, Target=target.group(1).strip() if target else "", Durability=int(hull.group(1)) if hull else 0, Crew=int(crew.group(1)) if crew else 0,
                        Reward=reward, Advance=advance, Gifts=gvdb_gifts.gifts(r[9])[0], Skills=[{"Name": s, "Rank": int(k)} for s, k in re.findall(r"([^\s,()()]+?)\((\d+)\)", unicodedata.normalize("NFKC", r[6]))]))
    print(total, "건 가운데", len(out), "건 · 뺀 것", skipped, "· 수가 없어 한 척으로 본 것(자리 없는 것 포함)", guessed)
    print("좌표가 있는 것", sum(1 for q in out if q["X"]), "· 바다만", sum(1 for q in out if not q["X"]), "· 보수를 아는 것", sum(1 for q in out if q["Reward"]),
          "· 상대 이름", sum(1 for q in out if q["Target"]), "· 내구", sum(1 for q in out if q["Durability"]), "· 선원", sum(1 for q in out if q["Crew"]))
    import collections
    print("배", collections.Counter(q["Ship"] for q in out).most_common(12))
    print("난이도", sorted(collections.Counter(q["Difficulty"] for q in out).items()))
    for q in out[:6]:
        print(" ", q)
    if "--write" in sys.argv:
        json.dump(out, open(os.path.join(ROOT, "sea-quests-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
        print("wrote sea-quests-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
