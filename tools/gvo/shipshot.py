"""배 한 척(선체 + 뒤따르는 돛 파트)을 PNG 로 그려 본다: python shipshot.py <묶음> <선체 번호> <파트 수> <나올 PNG> [보기]"""
import sys
import numpy as np
import texture as T
from pack import Pack
from model import Model, render

rel, first, n, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
view = sys.argv[5] if len(sys.argv) > 5 else "iso"
p = Pack(rel)
base = T.decode(T.load(bytes(Pack("0001/sh0004.bin").entry(0)))[0])
sail = T.decode(T.load(bytes(Pack("0001/sa0000.bin").entry(0)))[0])
parts = []
for i in range(first, first + n):
    e = bytes(p.entry(i))
    if e[:4] != b"XKMD":
        continue
    m = Model(e)
    for s in m.subsets:
        vb = m.vbs[s["vb"]]
        if "pos" not in vb:
            continue
        tris = m.triangles(s)
        if i == first:
            parts.append((vb["pos"], vb.get("uv0"), tris, base, (150, 120, 80)))
        elif vb["fvf"] == 0x316:
            parts.append((vb["pos"], vb.get("uv0"), tris, sail, (230, 225, 210)))
        elif "uv0" in vb:
            parts.append((vb["pos"], None, tris, None, (120, 95, 60)))
render(parts, out, 900, view)
print("ok", out, len(parts))
