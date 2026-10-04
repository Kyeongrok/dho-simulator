"""상륙지의 바다 자리 고르기: python landspot.py <도시 id> <dx> <dy>

도시 앞바다(cities.json 의 SeaX, SeaY)에서 배로 닿는 바다 칸 가운데, 뭍에 붙어 있고
(앞바다 + (dx, dy))에 가장 가까운 칸의 세계 좌표를 낸다. 통행 판정 지도는 비트 하나가 4 x 4.
"""
import json
import os
import sys
from collections import deque

import numpy as np

import seamask

DATA = os.path.join(os.path.dirname(__file__), "..", "..", "data")
_land = None


def land():
    global _land
    if _land is None:
        _land = seamask.load()[1].astype(bool)
    return _land


def spot(city, dx, dy, reach=220):
    """(X, Y, 겨냥한 곳과의 거리) — reach 는 앞바다에서 뒤질 범위(비트)."""
    m = land()
    h, w = m.shape
    sx, sy = city["SeaX"] // 4, city["SeaY"] // 4
    tx, ty = sx + dx / 4, sy + dy / 4
    seen = {(sx, sy)}
    queue = deque([(sx, sy)])
    best = None
    while queue:
        x, y = queue.popleft()
        near = [((x + ax) % w, y + ay) for ax, ay in ((1, 0), (-1, 0), (0, 1), (0, -1)) if 0 <= y + ay < h]
        if any(m[q[1], q[0]] for q in near):
            d = (x - tx) ** 2 + (y - ty) ** 2
            if best is None or d < best[0]:
                best = (d, x, y)
        for q in near:
            if q not in seen and not m[q[1], q[0]] and abs(q[0] - sx) <= reach and abs(q[1] - sy) <= reach:
                seen.add(q)
                queue.append(q)
    if best is None:
        return None
    return best[1] * 4 + 2, best[2] * 4 + 2, round(float(np.sqrt(best[0])) * 4)


def cities():
    return {c["Id"]: c for c in json.load(open(os.path.join(DATA, "extracted", "cities.json"), encoding="utf-8-sig"))}


if __name__ == "__main__":
    c = cities()[int(sys.argv[1])]
    print(c["Name"], "앞바다", c["SeaX"], c["SeaY"], "→", spot(c, float(sys.argv[2]), float(sys.argv[3])))
