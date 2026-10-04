"""항구 장면(GRM 지형·부두 + GRI 배치 물체)을 비스듬히 내려다본 PNG 로: python portshot.py <장면 id> <PNG> [방위각]"""
import sys
import numpy as np
import town
from model import render

tid, out = int(sys.argv[1]), sys.argv[2]
az = np.radians(float(sys.argv[3])) if len(sys.argv) > 3 else 0.0
g = town.Grm(tid)
pl = town.placements(tid)
cx = np.mean([m[3, 0] for _, m in pl]) if pl else None
cz = np.mean([m[3, 2] for _, m in pl]) if pl else None
R = 9000.0


def rot(p):
    c, s = np.cos(az), np.sin(az)
    q = p.copy()
    q[:, 0] = p[:, 0] * c - p[:, 2] * s
    q[:, 2] = p[:, 0] * s + p[:, 2] * c
    return q


def add(parts, scene, xform=None):
    tex = {}
    for _, d in scene.draws():
        vb = scene.vbs[d["vb"]]
        idx = scene.ibs[d["ib"]][d["istart"]:d["istart"] + 3 * d["tris"]].astype(np.int64).reshape(-1, 3)
        pos = vb["pos"]
        if xform is not None:
            pos = pos @ xform[:3, :3] + xform[3, :3]
        if cx is not None:
            c = pos[idx].mean(1)
            idx = idx[(abs(c[:, 0] - cx) < R) & (abs(c[:, 2] - cz) < R)]
        if not len(idx):
            continue
        if d["tex"] not in tex and d["tex"] < scene.gtex.count:
            tex[d["tex"]] = scene.gtex.image(d["tex"], 1)
        parts.append((rot(pos), vb["uv"], idx, tex.get(d["tex"]), (200, 190, 150)))


parts = []
add(parts, g)
objs = {}
for oid, m in pl:
    if oid not in objs:
        objs[oid] = town.Grd(oid)
    add(parts, objs[oid], m)
if cx is not None:      # 바다 면 (y = 0)
    w = np.array([[cx - R, 0, cz - R], [cx + R, 0, cz - R], [cx + R, 0, cz + R], [cx - R, 0, cz + R]], np.float32)
    parts.append((rot(w), None, np.array([[0, 1, 2], [0, 2, 3]]), None, (30, 60, 120)))
render(parts, out, 1400, "iso")
print("ok", len(parts))
