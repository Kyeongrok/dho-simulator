"""gvdb 의뢰 표의 「釣り（発見物）」 줄 — 낚시로 발견하는 해양생물: python gvdb_fishfinds.py [--write]

줄: 이름 · 난이도 · 필요 스킬 「釣り(5)」 · 발견물 · 차례 글의 좌표(「座標13989,4950付近」 · 「(590,3349)付近」 — 여럿이면 처음 것).
발견물은 클라이언트 발견물 표의 일본어 이름으로 번호에 잇는다.
→ data\\extracted\\fish-finds-gvdb.json: [{DiscoveryId, Name(일본어), Rank, X, Y}]
"""
import json
import os
import re
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import wiki_discovery
import gvdb_gifts

# --wrecks: 같은 꼴의 「沈没船（発見物）」 줄(인양으로 발견하는 침몰선 — 자리만 쓴다)을 wreck-finds-gvdb.json 에
WRECKS = "--wrecks" in sys.argv
KIND = "沈没船（発見物）" if WRECKS else "釣り（発見物）"
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


def main():
    found = {}
    for rid, (name, _) in wiki_discovery.client_names(gvo.LANG_JA).items():
        found.setdefault(name, rid)
    raw = open(os.path.join(ROOT, "gvdb", "quests.csv"), "rb").read().decode("cp932", "replace")
    rows = [r.split("\t") for r in re.split(r"\r?\n(?=\d+\t)", raw)][1:]
    out, lost, nowhere, total = [], [], [], 0
    for r in rows:
        if len(r) < 11 or r[4] != KIND:
            continue
        total += 1
        if r[8] not in found:
            lost.append(r[8])
            continue
        steps = unicodedata.normalize("NFKC", re.sub(r"<br ?/?>", "\n", r[10]))
        spot = re.search(r"(?<![\d,.])(\d{2,5})\s*[.,、]\s*(\d{3,4})(?!\d)", steps)
        if not spot or int(spot.group(1)) >= 16384 or int(spot.group(2)) >= 8192:
            nowhere.append(r[8])
            continue
        rank = re.search(r"釣り\((\d+)\)", unicodedata.normalize("NFKC", r[6]))
        out.append(dict(DiscoveryId=found[r[8]], Name=r[8], Rank=int(rank.group(1)) if rank else int(r[2] or 1), X=int(spot.group(1)), Y=int(spot.group(2)), **({"Gifts": gvdb_gifts.gifts(r[9])[0]} if WRECKS else {})))      # 침몰선은 보상 칸 = 인양품
    print(total, "건 가운데", len(out), "건 · 발견물을 못 이은 것", lost, "· 좌표가 없는 것", nowhere)
    print("랭크", sorted({q["Rank"] for q in out}), "· 보기", out[:4])
    if "--write" in sys.argv:
        json.dump(out, open(os.path.join(ROOT, "wreck-finds-gvdb.json" if WRECKS else "fish-finds-gvdb.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
        print("wrote", "wreck-finds-gvdb.json" if WRECKS else "fish-finds-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
