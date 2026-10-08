"""gvdb 의 discovery_items.csv(발견물 — 이용자 사이트)에서 발견물의 랭크(★ 1 ~ 5)를 뽑는다: python gvdb_discovery.py [--write]

줄(탭, cp932): ID · 種別 · 発見物名 · ランク · 説明 · 時代 · ポイント · 難易度 · 経験値 · 備考 · 最終更新日時 · 関連クエスト
클라이언트의 발견물 표(32)와 이름으로 견준 것(2026-10-08):
  経験値 = 표의 경험치(3,555 / 3,651 같다), 難易度 = 표에서 종류 다음의 값(3,067 / 3,336 같다 — 게임이 Stars 라고 부르던 값은 난이도다),
  ランク 는 표에 없다(줄 끝의 2바이트와도 안 맞는다) → 이것만 gvdb 에서 가져온다.
→ data\\extracted\\discovery-ranks-gvdb.json: {발견물 번호(클라이언트): 랭크}
"""
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import wiki_discovery
from wiki_ships import squeeze

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted")


def main():
    by_name = collections.defaultdict(list)
    for rid, (name, _) in wiki_discovery.client_names(gvo.LANG_JA).items():
        by_name[squeeze(name)].append(rid)
    ranks, lost, twice = {}, 0, 0
    for line in open(os.path.join(ROOT, "gvdb", "discovery_items.csv"), encoding="cp932", errors="replace"):
        c = line.rstrip("\n").split("\t")
        if len(c) != 12 or not c[0].isdigit() or not c[3].isdigit():
            continue
        ids = by_name.get(squeeze(c[2]))
        if not ids:
            lost += 1
        elif len(ids) > 1:
            twice += 1          # 이름이 같은 발견물이 둘 — 어느 것인지 몰라 뺀다
        else:
            ranks[ids[0]] = int(c[3])
    print(len(ranks), "발견물의 랭크 · 이름을 못 이은 것", lost, "· 이름이 겹친 것", twice, "·", sorted(collections.Counter(ranks.values()).items()))
    if "--write" in sys.argv:
        json.dump(dict(sorted(ranks.items())), open(os.path.join(ROOT, "discovery-ranks-gvdb.json"), "w", encoding="utf-8"), indent=0)
        print("wrote discovery-ranks-gvdb.json")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
