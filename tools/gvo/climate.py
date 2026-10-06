"""해역마다 바람 · 해류 · 날씨의 기본값을 지어 data\\sea-climates.json 을 만든다: python climate.py

클라이언트에는 바람 · 해류 자료가 없다. 해역 격자(0000\\bin\\10000000.bin)의 네모 가운데로 위도 · 경도를 어림하고
(도시 몇 곳의 세계 좌표와 실제 위도를 이어 붙인 것), 실제 지구의 바람띠와 해류를 본떠 값을 짓는다.
방향은 나침반 각도(0 북, 90 동)이고 「불어 가는 쪽 / 흘러가는 쪽」이다. 이미 있는 파일은 덮어쓴다.
"""
import json
import os
import struct
import sys

import gvo

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
# (세계 좌표 y, 위도) — 스톡홀름 · 런던 · 리스본 · 알렉산드리아 · 산토도밍고 · 캘리컷 · 시에라리온 · 자카르타 · 리마 · 케이프타운
ANCHORS = [(1980, 59.3), (2418, 51.5), (3204, 38.7), (3534, 31.2), (4038, 18.5), (4316, 11.25), (4443, 8.5),
           (4981, -6.2), (5260, -12), (6004, -33.9)]


def latitude(y):
    if y <= ANCHORS[0][0]:
        return ANCHORS[0][1] + (ANCHORS[0][0] - y) * 0.018
    if y >= ANCHORS[-1][0]:
        return ANCHORS[-1][1] - (y - ANCHORS[-1][0]) * 0.03
    for (y0, a0), (y1, a1) in zip(ANCHORS, ANCHORS[1:]):
        if y0 <= y <= y1:
            return a0 + (a1 - a0) * (y - y0) / (y1 - y0)


def longitude(x):
    v = (x - 16330) / 45.51          # 런던이 x = 16324
    while v < -180:
        v += 360
    while v > 180:
        v -= 360
    return v


def zone_centres():
    d = gvo.read_mwc(r"0000\bin\10000000.bin")
    n = struct.unpack_from("<I", d, 0)[0]
    out = {}
    for k in range(n):
        zid = (struct.unpack_from("<I", d, 4 + 4 * k)[0] >> 16) & 0xFF
        at = struct.unpack_from("<I", d, 4 + 4 * n + 8 * k)[0]
        cx, cy, _, _, w, h = struct.unpack_from("<6I", d, at)
        out[zid] = ((cx + cx + w) * 128 % 16384, (cy + cy + h) * 128)
    return out


def climate(sea, x, y):
    la, lo = latitude(y), longitude(x)
    a, north = abs(la), la >= 0
    c = dict(Id=sea["Id"], Name=sea["Name"], WindDirection=0, WindKnots=9.0, WindSwing=40, Seasonal=False,
             CurrentDirection=0, CurrentKnots=0.0, Storm=1.0, Rain=0.12, Wave=1.0)
    if a < 5:        # 적도 무풍대
        c.update(WindDirection=270, WindKnots=5, WindSwing=90, Rain=0.30, Storm=0.6, Wave=0.7)
    elif a < 30:     # 무역풍
        c.update(WindDirection=225 if north else 315, WindKnots=11, WindSwing=20, Rain=0.10, Storm=0.9)
    elif a < 60:     # 편서풍 — 남반구 40도 아래는 사납다
        strong = not north and a > 38
        c.update(WindDirection=70 if north else 110, WindKnots=17 if strong else 13, WindSwing=45, Rain=0.18,
                 Storm=2.0 if strong else 1.3, Wave=1.8 if strong else 1.3)
    else:            # 극동풍
        c.update(WindDirection=260, WindKnots=10, WindSwing=50, Rain=0.15, Storm=1.5, Wave=1.4)
    med = 30 < la < 46 and -6 < lo < 42
    if med:          # 지중해 · 흑해: 약하고 변덕스럽다
        c.update(WindDirection=135, WindKnots=8, WindSwing=70, Rain=0.08, Storm=0.7, Wave=0.7)
    if 50 < la < 68 and -5 < lo < 32:      # 북해 · 발트해
        c.update(WindKnots=12, Storm=1.5, Rain=0.22, Wave=1.2 if lo < 10 else 0.9)
    if 0 <= la < 27 and 42 < lo < 122:     # 몬순 — 여름엔 북동으로 불고 겨울엔 뒤집힌다
        c.update(WindDirection=45, WindKnots=12, WindSwing=25, Seasonal=True, Rain=0.22, Storm=1.2)
    if 8 < la < 28 and -98 < lo < -58:     # 카리브 — 허리케인
        c.update(Storm=1.6, Rain=0.20)
    if 8 < la < 35 and 118 < lo < 150:     # 동아시아 — 태풍
        c.update(Storm=1.7, Rain=0.20)

    def current(direction, knots):
        c.update(CurrentDirection=direction, CurrentKnots=knots)

    if 15 < la < 42 and -22 < lo < -5 and not med: current(190, 0.6)      # 카나리아 해류
    elif 8 < la < 22 and -60 < lo <= -22: current(270, 0.8)               # 북적도 해류
    elif 20 < la < 42 and -82 < lo < -60: current(45, 1.5)                # 멕시코 만류
    elif 40 < la < 58 and -60 < lo < -8: current(70, 0.8)                 # 북대서양 해류
    elif -35 < la < -5 and 5 < lo < 20: current(350, 0.8)                 # 벵겔라 해류
    elif -10 < la < 5 and -40 < lo < 5: current(275, 0.8)                 # 남적도 해류
    elif -40 < la < -8 and -55 < lo < -30: current(200, 0.7)              # 브라질 해류
    elif -40 < la < -15 and 22 < lo < 42: current(225, 1.5)               # 아굴라스 해류
    elif la < -40: current(90, 0.9)                                       # 남극 순환류
    elif 20 < la < 42 and 120 < lo < 150: current(45, 1.3)                # 쿠로시오
    elif -45 < la < -3 and -90 < lo < -70: current(350, 0.8)              # 훔볼트 해류
    elif -15 < la < 15 and (lo > 130 or lo < -90): current(270, 0.7)      # 태평양 적도 해류
    return c, la, lo


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    seas = json.load(open(os.path.join(ROOT, "data", "extracted", "seas.json"), encoding="utf-8-sig"))
    centres = zone_centres()
    out = []
    for sea in seas:
        if sea["Id"] not in centres:
            continue
        c, la, lo = climate(sea, *centres[sea["Id"]])
        out.append(c)
        print("%3d %-14s 위도 %4.0f 경도 %5.0f  바람 %3d° %4.1f%s  해류 %3d° %.1f  폭풍 ×%.1f" % (
            c["Id"], c["Name"], la, lo, c["WindDirection"], c["WindKnots"], " 철" if c["Seasonal"] else "",
            c["CurrentDirection"], c["CurrentKnots"], c["Storm"]))
    json.dump(out, open(os.path.join(ROOT, "data", "sea-climates.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(len(out), "/", len(seas))
