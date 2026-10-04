"""교역(무역)에 쓰는 표를 푼다.

    python trade.py goods  [out.json]     # 교역품 660줄 (표 19) + 갈래 (표 18)
    python trade.py towns  [out.json]     # 도시 발견물 설명에서 뽑은 「산물」 실마리 (표 32)
    python trade.py text                  # 교역소 화면 글 (dt000002)
    python trade.py check                 # 줄 짜임이 표 끝과 맞는지, 교역품 id 가 다른 표에 쓰이는지

클라이언트에 있는 것: 교역품의 이름·설명·갈래, 갈래 이름, 주점 메뉴, 지방 묶음, 화면 글.
클라이언트에 없는 것: 도시별 판매 목록, 값, 시세, 문화권 배율 — 서버가 내려 주던 것으로 보인다.
"""
import json
import re
import struct
import sys

import gvo
import tables
from tables import Reader

T_REGION_GROUP, T_CATEGORY, T_GOODS, T_TAVERN_MENU = 43, 18, 19, 37
T_FACILITY, T_TOWN_INFO, T_AREA = 98, 99, 112
GOODS_BASE = 1600000


def goods(t):
    """교역품: u32 id(1600001~), 이름, 설명, u16 갈래(표 18 의 id).
    id - 1600000 의 백·오십 자리가 작은 갈래다(1~ 식료품, 101~ 조미료, 151~ 기호품 …, 1001~ 물고기)."""
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32()
        out.append(dict(id=i, no=i - GOODS_BASE, name=r.text(i), desc=r.text(i), category=r.u16()))
    assert r.end, (r.o, len(t))
    return out


def tavern_menu(t):
    """주점 메뉴(술·요리): u32 id, 이름, 설명, u16 ?"""
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32()
        out.append(dict(id=i, name=r.text(i), desc=r.text(i), a=r.u16()))
    assert r.end, (r.o, len(t))
    return out


def region_groups(t):
    """지방 묶음 18개: u32 id, 이름, u32 × 8 (이웃 지방 번호로 보인다 — 추측. 0 은 빈 칸)."""
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32()
        out.append(dict(id=i, name=r.text(i).strip(), links=[r.u32() for _ in range(8)]))
    assert r.end, (r.o, len(t))
    return out


# ── 도시 발견물의 「산물」 글 ────────────────────────────────────────────────

PRODUCT = re.compile(r"산물[은는]?\s*([^.。]*)")


def town_products(tabs):
    """도시와 이름이 같은 발견물(갈래 '항구·도시')의 설명에서 「산물은 …」 구절을 뽑고,
    그 구절에 든 교역품 이름·갈래 이름을 찾는다."""
    cities = {c["name"]: c for c in tables.cities(tabs[tables.T_CITY])}
    cats = {c["id"]: c["name"] for c in tables.named(tabs[T_CATEGORY])}
    gs = goods(tabs[T_GOODS])
    out = []
    for d in tables.discoveries(tabs[tables.T_DISCOVERY]):
        if d["name"] not in cities:
            continue
        m = PRODUCT.search(d["desc"])
        if not m:
            continue
        phrase = m.group(1)
        named = [g["no"] for g in gs if len(g["name"]) >= 2 and g["name"] in phrase]
        kinds = [k for k, n in cats.items() if n in phrase]
        out.append(dict(city=cities[d["name"]]["id"], name=d["name"], discovery=d["id"], phrase=phrase.strip(),
                        goods=named, categories=kinds))
    return out


# ── 화면 글 ──────────────────────────────────────────────────────────────────

TRADE_WORDS = ("교역소", "매입", "매각", "구입", "매도", "흥정", "시세", "적재", "명산품", "폭락", "폭등", "유행", "교역품")


def trade_text():
    st = gvo.string_table(gvo.read_mwc(r"0000\local\dt000002.bin", gvo.LANG_KO))
    return [(i, gvo.flat(s)) for i, s in sorted(st.items()) if any(w in gvo.flat(s) for w in TRADE_WORDS)]


# ── 확인 ─────────────────────────────────────────────────────────────────────

def check(tabs):
    import glob
    import os
    import numpy as np
    gs = goods(tabs[T_GOODS])
    print("표 19 교역품", len(gs), "줄 — 표 끝과 맞음")
    print("표 37 주점 메뉴", len(tavern_menu(tabs[T_TAVERN_MENU])), "줄 — 표 끝과 맞음")
    print("표 43 지방 묶음", len(region_groups(tabs[T_REGION_GROUP])), "줄 — 표 끝과 맞음")
    lo, hi = gs[0]["id"], gs[-1]["id"]

    def hits(b):
        b = bytes(b)
        return max(int(((a >= lo) & (a <= hi)).sum()) for a in
                   (np.frombuffer(b[al:al + (len(b) - al) // 4 * 4], "<u4") for al in range(4)))

    print("교역품 id(u32) 가 나오는 곳:")
    for i, t in enumerate(tabs):
        if hits(t) > 3:
            print("   dt000001 표", i, hits(t))
    for p in sorted(glob.glob(gvo.game_path(r"0000\bin\*.bin"))):
        raw = open(p, "rb").read()
        for k, c in enumerate(gvo.mwc_chunks(raw) or [raw]):
            if hits(c) > 3:
                print("  ", os.path.basename(p), k, hits(c))
    print("   dt000000", hits(gvo.read_mwc(r"0000\local\dt000000.bin", gvo.LANG_KO)))


def main(argv):
    tabs = gvo.data_tables()
    cmd = argv[1] if len(argv) > 1 else "goods"
    if cmd == "goods":
        data = dict(categories=tables.named(tabs[T_CATEGORY]), goods=goods(tabs[T_GOODS]))
    elif cmd == "towns":
        data = town_products(tabs)
    elif cmd == "text":
        for i, s in trade_text():
            print(i, s.replace("\n", "\\n"))
        return
    elif cmd == "check":
        check(tabs)
        return
    else:
        raise SystemExit(__doc__)
    text = json.dumps(data, ensure_ascii=False, indent=1)
    if len(argv) > 2:
        with open(argv[2], "w", encoding="utf-8") as f:
            f.write(text)
        print(len(data if isinstance(data, list) else data["goods"]), "줄 →", argv[2])
    else:
        sys.stdout.reconfigure(encoding="utf-8")
        print(text)


if __name__ == "__main__":
    main(sys.argv)
