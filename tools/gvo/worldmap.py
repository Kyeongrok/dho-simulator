"""세계지도(0003) 읽개 — PRES 묶음, LGB 격자, SLAM 지형 조각, 도시 자리, 해역 격자.

쓰기:
    python worldmap.py relief  out.png [x0 y0 x1 y1]   # 세계 좌표 네모의 지형 그림
    python worldmap.py zones   out.png                 # 해역 격자 + 도시 점
    python worldmap.py cities                          # 도시 id 와 자리 찍기

세계 좌표: 16384 x 8192, x 는 동쪽으로 돌고(0 과 16384 가 붙는다) y 는 남쪽으로 는다.
조각(cell) = 256 x 256, 꼭짓점 사이 = 4, 지형 단위 = 세계 좌표 x 100.
"""
import os
import re
import struct
import sys

import numpy as np

import gvo

WORLD_W, WORLD_H = 16384, 8192
CELL = 256            # 조각 한 변 (세계 좌표)
GRID_W, GRID_H = 64, 32
VERTS = 65            # 조각 한 변의 꼭짓점 수
UNIT = 100.0          # 세계 좌표 1 = 지형 단위 100 (꼭짓점 사이 400)

VERTEX = np.dtype([("h", "<f4"), ("n", "<f4", 3), ("c", "<u4")])   # 20바이트


# ── PRES: 이름 붙은 묶음 ──────────────────────────────────────────────────────
# "PRES", u32 파일 크기, u32 개수, u32 자리 × 개수, (u32 이름 길이, 이름) × 개수, 자료들.
# 이름은 바이트마다 0x5A 를 더해 놓았다.

def pres(data):
    """[(이름, bytes)]"""
    assert data[:4] == b"PRES"
    size, n = struct.unpack_from("<II", data, 4)
    offs = list(struct.unpack_from("<%dI" % n, data, 12)) + [size]
    p, out = 12 + 4 * n, []
    for i in range(n):
        ln = struct.unpack_from("<I", data, p)[0]
        name = bytes((c - 0x5A) & 0xFF for c in data[p + 4:p + 4 + ln]).decode("latin1")
        p += 4 + ln
        out.append((name, data[offs[i]:offs[i + 1]]))
    return out


def pres_set(prefix, base=0):
    """LPRB(…0.bin)가 말하는 묶음 수만큼 …1.bin, …2.bin 을 이어 읽는다.
    prefix 는 'gm0000' 처럼 끝 두 자리를 뺀 이름, base 는 끝에서 둘째 자리(0 또는 1)."""
    with open(gvo.game_path(r"0003\%s%d0.bin" % (prefix, base)), "rb") as f:
        head = f.read()
    assert head[:4] == b"LPRB"
    files = struct.unpack_from("<I", head, 8)[0]
    out = []
    for k in range(1, files + 1):
        with open(gvo.game_path(r"0003\%s%d%d.bin" % (prefix, base, k)), "rb") as f:
            out += pres(f.read())
    return out


# ── LGB: 조각 격자 ────────────────────────────────────────────────────────────
# "LGB\0", u32 크기, u32 가로 조각, u32 세로 조각, u32 조각 가로 칸(64), u32 세로 칸(64),
# f32 칸 크기 x(400), f32 칸 크기 z(400), u32 개수, i32 색인 × 개수 (-1 = 조각 없음 = 난바다)

def lgb(rel):
    with open(gvo.game_path(rel), "rb") as f:
        b = f.read()
    assert b[:3] == b"LGB"
    w, h, cw, ch = struct.unpack_from("<4I", b, 8)
    sx, sz = struct.unpack_from("<2f", b, 24)
    n = struct.unpack_from("<I", b, 32)[0]
    return w, h, cw, ch, sx, sz, struct.unpack_from("<%di" % n, b, 36)


# ── SLAM: 지형 조각 (.lgm) ────────────────────────────────────────────────────
# 0x00 "SLAM", u32 크기, u32 0x10, u32 크기-12
# 0x10 "LODG", u32 크기-28
# 0x18 u32 겹 수 n, u32 섞는 칸 수 a, u32 덧칠 수 a2
# 0x24 (u32 겹 번호(1부터), u32 텍스처 번호(gt 묶음 차례), u8×4 {0,1,1,0}) × n
#      꼭짓점 65×65 × 20바이트 (f32 높이, f32×3 법선, u32 색 BGRA)  — 줄 먼저(북→남), 줄 안은 서→동
#      칸 64×64 × (u32 바탕 겹 번호, u32 깃발)
#      (u16 칸x, u16 칸y, u16 덧칠 수) × a
#      (u32 겹 번호, u32 모서리 비트 1~15) × a2
#      "IOPL", u32 크기, u32 개수, (u32 모형, u32 모형, f32 x, y, z, f32 회전 x, y, z) × 개수

class Slam:
    def __init__(self, data):
        assert data[:4] == b"SLAM" and data[16:20] == b"LODG"
        n, a, a2 = struct.unpack_from("<3I", data, 0x18)
        self.layers = {}
        for i in range(n):
            lid, tex = struct.unpack_from("<II", data, 0x24 + 12 * i)
            self.layers[lid] = tex
        p = 0x24 + 12 * n
        self.verts = np.frombuffer(data, VERTEX, VERTS * VERTS, p).reshape(VERTS, VERTS)
        p += VERTS * VERTS * 20
        self.cells = np.frombuffer(data, "<u4", 64 * 64 * 2, p).reshape(64, 64, 2)
        p += 64 * 64 * 8
        heads = [struct.unpack_from("<HHH", data, p + 6 * i) for i in range(a)]
        p += 6 * a
        self.blends = []            # (칸x, 칸y, [(겹 번호, 모서리 비트)])
        for cx, cy, cnt in heads:
            self.blends.append((cx, cy, [struct.unpack_from("<II", data, p + 8 * j) for j in range(cnt)]))
            p += 8 * cnt
        assert data[p:p + 4] == b"IOPL"
        cnt = struct.unpack_from("<I", data, p + 8)[0]
        self.objects = [struct.unpack_from("<II6f", data, p + 12 + 32 * i) for i in range(cnt)]


class World:
    """set_no 0 = 본 세계(wm000000 · gm00000x · gt00000x), 1 = 딴 세계(wm000010 · gm00001x)."""

    def __init__(self, set_no=0):
        self.index = lgb(r"0003\wm0000%d0.bin" % set_no)[6]
        self.entries = pres_set("gm0000", set_no)

    def cell(self, cx, cy):
        i = self.index[(cy % GRID_H) * GRID_W + cx % GRID_W]
        return None if i < 0 else Slam(self.entries[i][1])

    def heights(self, x0, y0, x1, y1):
        """세계 좌표 네모의 높이(꼭짓점 4 좌표마다 하나). 조각 없는 곳은 NaN."""
        cx0, cy0, cx1, cy1 = x0 // CELL, y0 // CELL, (x1 - 1) // CELL, (y1 - 1) // CELL
        out = np.full(((cy1 - cy0 + 1) * 64 + 1, (cx1 - cx0 + 1) * 64 + 1), np.nan, np.float32)
        for cy in range(cy0, cy1 + 1):
            for cx in range(cx0, cx1 + 1):
                s = self.cell(cx, cy) if 0 <= cy < GRID_H else None
                if s is not None:
                    out[(cy - cy0) * 64:(cy - cy0) * 64 + 65, (cx - cx0) * 64:(cx - cx0) * 64 + 65] = s.verts["h"]
        return out


# ── 도시 자리 (sm000001 / sm000002) ───────────────────────────────────────────
# u16 개수, (u16 id, u32 x, u32 y) × 개수.  sm000004/5 는 1/2 와 똑같다.

def city_points(which=1):
    with open(gvo.game_path(r"0003\sm00000%d.bin" % which), "rb") as f:
        b = f.read()
    n = struct.unpack_from("<H", b, 0)[0]
    return {i: (x, y) for i, x, y in (struct.unpack_from("<HII", b, 2 + 10 * k) for k in range(n))}


# ── 해역 격자 (0000\bin\10000000.bin) ─────────────────────────────────────────
# u32 개수, u32 해역 id(위 16비트) × 개수, (u32 자리, u32 크기) × 개수
# 해역: u32 cx, u32 cy, u32 cx*25600, u32 cy*25600, u32 w, u32 h,
#       (u32 방향 깃발, u32 그 칸의 해역 id, u32 그 해역 x*100, u32 y*100, u32 0) × w*h
# (w, h) 는 한 겹 테두리를 낀 크기다. 테두리 칸은 이웃 해역을 가리키고, 안쪽 (w-2)×(h-2) 조각이 그 해역이다.

def sea_zones(rel=r"0000\bin\10000000.bin"):
    """{해역 id: (x0, y0, x1, y1)} — 세계 좌표, x 는 16384 를 넘을 수 있다(돌아감)."""
    c = gvo.read_mwc(rel)
    n = struct.unpack_from("<I", c, 0)[0]
    ids = struct.unpack_from("<%dI" % n, c, 4)
    out = {}
    for k in range(n):
        off = struct.unpack_from("<I", c, 4 + 4 * n + 8 * k)[0]
        cx, cy, _, _, w, h = struct.unpack_from("<6I", c, off)
        out[ids[k] >> 16] = ((cx + 1) * CELL, (cy + 1) * CELL, (cx + w - 1) * CELL, (cy + h - 1) * CELL)
    return out


def zone_at(zones, x, y):
    x %= WORLD_W
    for zid, (x0, y0, x1, y1) in zones.items():
        if y0 <= y < y1 and (x0 <= x < x1 or x0 <= x + WORLD_W < x1):
            return zid
    return None


# ── 바다 빛깔 격자 (cm000000.bin, "PSCD") ─────────────────────────────────────
# "PSCD", u32 가로 64, u32 세로 32, u32 벌 수 13, u32 벌 크기 152, u32 벌 번호 × 2048, 벌 × 13

def sea_presets(rel=r"0003\cm000000.bin"):
    with open(gvo.game_path(rel), "rb") as f:
        b = f.read()
    w, h, n, size = struct.unpack_from("<4I", b, 4)
    idx = struct.unpack_from("<%dI" % (w * h), b, 20)
    base = 20 + 4 * w * h
    return idx, [b[base + i * size:base + (i + 1) * size] for i in range(n)]


# ── 그리기 ────────────────────────────────────────────────────────────────────

def shade(h):
    """높이 배열 → RGB. 0 아래는 바다(-50 은 바닥), 위는 뭍."""
    c = np.zeros(h.shape + (3,), np.uint8)
    hh = np.nan_to_num(h, nan=-50.0)
    c[...] = (25, 55, 120)
    c[(hh > -49.9) & (hh <= 0)] = (40, 80, 150)
    land = hh > 0
    v = np.clip(hh / 600, 0, 1)
    c[land, 0] = (110 + 120 * v[land]).astype(np.uint8)
    c[land, 1] = (150 + 60 * v[land]).astype(np.uint8)
    c[land, 2] = (80 + 100 * v[land]).astype(np.uint8)
    gy, gx = np.gradient(hh)
    return np.clip(c * np.clip(1 + (gx - gy) / 60, 0.6, 1.4)[..., None], 0, 255).astype(np.uint8)


def main(argv):
    from PIL import Image, ImageDraw
    if argv[1] == "cities":
        a, b = city_points(1), city_points(2)
        zones = sea_zones()
        for i in sorted(b):
            p = a.get(i, b[i])
            print(i, a.get(i), b[i], "해역 %X" % (zone_at(zones, *p) or 0))
    elif argv[1] == "relief":
        x0, y0, x1, y1 = map(int, argv[3:7]) if len(argv) > 6 else (0, 2560, 2560, 3840)
        Image.fromarray(shade(World().heights(x0, y0, x1, y1))).save(argv[2])
    elif argv[1] == "zones":
        with open(gvo.game_path(r"0003\sm000000.bin"), "rb") as f:
            bits = np.unpackbits(np.frombuffer(f.read(), np.uint8), bitorder="little").reshape(1024, 2048)
        img = Image.fromarray(np.where(bits[..., None] > 0, np.uint8([200, 190, 150]), np.uint8([40, 70, 140])))
        d = ImageDraw.Draw(img)
        for zid, (zx0, zy0, zx1, zy1) in sea_zones().items():
            for shift in (0, -WORLD_W):
                d.rectangle([(zx0 + shift) / 8, zy0 / 8, (zx1 + shift) / 8, zy1 / 8], outline=(255, 255, 0))
                d.text(((zx0 + shift) / 8 + 2, zy0 / 8 + 1), "%X" % zid, fill=(255, 255, 0))
        for x, y in city_points(1).values():
            d.point((x / 8, y / 8), fill=(255, 0, 0))
        img.save(argv[2])


if __name__ == "__main__":
    main(sys.argv)
