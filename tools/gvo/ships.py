"""조선 자료 읽개 — 배 표와 부품 표(dt000001), 배 모형 표(0001\\0002.bin), 모형 이름 찾기.

    python ships.py                 # 표마다 줄 수와 앞 몇 줄
    python ships.py json out.json   # 전부 JSON 으로
    python ships.py models          # 모형 번호 → (묶음, 항목) 찾기 (sh 묶음을 훑어 오래 걸린다)

줄 짜임 (모두 u32 id, 이름, 설명 뒤에 붙는 것. 글은 gvo.packed_string)
  표 28 배(814)      u16 모형 번호, u32 높이, u32 ?, u32 폭, u32 길이, u32 크기 등급(0~4), u32 갈래(0 범선, 2 갤리 …),
                     u32 돛대 수, f32×3 (물보라·항적 자리로 추측), f32×3 방향 벡터
  표 22 대포(687)    u32 문 수, 관통력, 자리(0 측면, 2 선미 …), 사정거리, 탄속, 폭발 범위, 장전, ?, 내구, 탄 갈래
  표 23 장갑(107)    u16 장갑, u16 속도 줄임, u32 내구
  표 24 선수 장비(140) u32 갈래(0 충각 …), u32 세기, u32 내구, u32 ?, u8×6
  표 25 보조돛(80)   u32 가로돛, u32 세로돛, u32 ?, u32 내구
  표 27 선수상(149)  u32×4 효과 값, u32 내구
  표 41 배 이름(51)  이름만 한 글, u32 나라, u32 ?
  표 29 돛(43) · 30 재료(88) · 89 용도(7) · 90 강화(32) · 26 문장(156)   이름과 설명뿐 — 수치가 없다
"""
import json
import re
import struct
import sys

import gvo

SHIP, CANNON, ARMOR, BOW, AUX_SAIL, EMBLEM, FIGUREHEAD = 28, 22, 23, 24, 25, 26, 27
SAIL, MATERIAL, SHIP_NAME, USAGE, UPGRADE = 29, 30, 41, 89, 90


def _rows(t, strings, tail_fmt, names):
    size = struct.calcsize(tail_fmt)
    o, out = 4, []
    for _ in range(struct.unpack_from("<I", t, 0)[0]):
        rid = struct.unpack_from("<I", t, o)[0]
        o += 4
        row = {"id": rid}
        for key in strings:
            row[key], o = gvo.packed_string(t, o, rid)
        vals = struct.unpack_from(tail_fmt, t, o)
        o += size
        i = 0
        for name, n in names:
            row[name] = vals[i] if n == 1 else list(vals[i:i + n])
            i += n
        out.append(row)
    assert o == len(t), (o, len(t))
    return out


ND = ("name", "desc")
LAYOUT = {
    SHIP: (ND, "<H7I6f", [("model", 1), ("height", 1), ("b", 1), ("beam", 1), ("length", 1), ("size", 1),
                          ("kind", 1), ("masts", 1), ("spray", 3), ("dir", 3)]),
    CANNON: (ND, "<10I", [("guns", 1), ("pierce", 1), ("place", 1), ("range", 1), ("speed", 1), ("blast", 1),
                          ("reload", 1), ("a", 1), ("durability", 1), ("shot", 1)]),
    ARMOR: (ND, "<HHI", [("armor", 1), ("slow", 1), ("durability", 1)]),
    BOW: (ND, "<4I6B", [("kind", 1), ("power", 1), ("durability", 1), ("a", 1), ("extra", 6)]),
    AUX_SAIL: (ND, "<4I", [("square", 1), ("fore_aft", 1), ("a", 1), ("durability", 1)]),
    FIGUREHEAD: (ND, "<5I", [("effect", 4), ("durability", 1)]),
    SHIP_NAME: (("name",), "<2I", [("nation", 1), ("a", 1)]),
    SAIL: (ND, "<", []), MATERIAL: (ND, "<", []), USAGE: (ND, "<", []), UPGRADE: (ND, "<", []), EMBLEM: (ND, "<", []),
}


def table(tabs, number):
    strings, fmt, names = LAYOUT[number]
    return _rows(tabs[number], strings, fmt, names)


# ── 배 모형 표 0001\0002.bin ──────────────────────────────────────────────────
# u32 2, u32 1, u16 91, u16 71, u32 1, u32 1, u32 줄 수(218), 이어서 64바이트 줄:
#   u16 선체 자원 번호, u16 모형 번호 - 1, u16 NL 자원 번호, 6바이트 0, f32 크기 비(SHIP01 = 1.0), u32,
#   u16×5 텍스처 벌, u16×5 돛대 갈래, u16×5 뼈대 자원 번호, u16×5 돛 자원 번호, u16×2
# 자원 번호는 묶음 안 차례가 아니다(.tbl 을 거친다 — 못 풀었다). 처음 17척만 sh0000 의 차례와 같다.

def model_table():
    with open(gvo.game_path(r"0001\0002.bin"), "rb") as f:
        b = f.read()
    n = struct.unpack_from("<I", b, 0x14)[0]
    out = {}
    for k in range(n):
        r = struct.unpack_from("<3H6xfI5H5H5H5H2H", b, 0x18 + 64 * k)
        out[r[1] + 1] = {"hull_res": r[0], "nl_res": r[2], "scale": round(r[3], 3), "textures": r[5:10],
                         "mast_kinds": r[10:15], "bone_res": r[15:20], "sail_res": r[20:25]}
    return out


def model_entries():
    """{모형 번호: (묶음 파일, 선체 항목 번호)} — sh0000~sh0003 을 훑어 이름 SHIPnn_01 을 찾는다."""
    from pack import Pack
    out = {}
    for k in range(4):
        rel = r"0001\sh000%d.bin" % k
        p = Pack(rel)
        for i in range(p.count):
            e = p.entry(i)
            if len(e) < 0x80 or bytes(e[:4]) != b"XKMD":
                continue
            o = struct.unpack_from("<13I", e, 0x4C)[4]
            if not o:
                continue
            m = re.match(rb"SHIP(\d+)_01", bytes(e[o + 4:o + 36]))
            if m:
                out.setdefault(int(m.group(1)), (rel, i))
    return out


if __name__ == "__main__":
    tabs = gvo.data_tables()
    if len(sys.argv) > 2 and sys.argv[1] == "json":
        data = {str(n): table(tabs, n) for n in LAYOUT}
        data["models"] = {str(k): v for k, v in model_table().items()}
        with open(sys.argv[2], "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
    elif len(sys.argv) > 1 and sys.argv[1] == "models":
        for m, loc in sorted(model_entries().items()):
            print(m, loc)
    else:
        for n in LAYOUT:
            rows = table(tabs, n)
            print(n, len(rows))
            for row in rows[:3]:
                print("   ", {k: (v[:24] if isinstance(v, str) else v) for k, v in row.items()})
