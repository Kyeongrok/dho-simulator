"""몸 묶음(md)의 부위를 맞춰 사람 하나를 세워 본다: python figure.py <묶음 번호> out.png [face hair body leg hand]

묶음 안에서 부위는 「모형, 텍스처」 짝으로 잇달아 있다(텍스처 이름 NNN_face#### · hair## · body###_at · leg##_at · hand## · cap##_m).
모형은 팔을 벌린 자세(T 꼴)의 모델 좌표라서 부위를 그대로 겹치면 한 사람이 된다.
팔을 내리는 것은 뼈대 대신 어깨를 축으로 팔 쪽 정점을 돌려서 흉내 낸다.
"""
import re
import sys

import numpy as np

import model
import texture
from pack import Pack


def parts(pack):
    """{부위: [(이름, 모형 항목, 텍스처 항목)]} — 텍스처 바로 앞의 모형이 짝이다."""
    out, last = {}, None
    for i in range(pack.count):
        e = pack.entry(i)
        if len(e) < 8:
            continue
        if bytes(e[:4]) == b"XKMD":
            last = i
        elif bytes(e[:4]) == b"MFTF" and last is not None:
            try:
                name = texture.xftx_images(e[texture.mftf_entries(e)[0][1]:])[0].name
            except Exception:
                continue
            kind = re.sub(r"\d+.*$", "", name.split("_", 1)[-1]).lower()
            out.setdefault(kind, []).append((name, last, i))
    return out


def lower_arms(pos, shoulder_x, shoulder_y, angle=1.25):
    """|x| 가 어깨 너머인 정점을 어깨를 축으로 아래로 돌린다(가까울수록 덜)."""
    p = pos.copy()
    for side in (-1, 1):
        d = (p[:, 0] * side - shoulder_x)
        t = np.clip(d / 8.0, 0, 1) * angle * side
        m = d > 0
        x, y = p[m, 0] - side * shoulder_x, p[m, 1] - shoulder_y
        c, s = np.cos(-t[m]), np.sin(-t[m])
        p[m, 0] = side * shoulder_x + x * c - y * s
        p[m, 1] = shoulder_y + x * s + y * c
    return p


if __name__ == "__main__":
    n = int(sys.argv[1])
    pk = Pack(r"0001\md%04d.bin" % n)
    ps = parts(pk)
    print({k: len(v) for k, v in ps.items()})
    pick = [int(a) for a in sys.argv[3:]] + [0] * 5
    out = []
    shoulder = None
    for kind, k in zip(("body", "face", "hair", "leg", "hand"), (pick[2], pick[0], pick[1], pick[3], pick[4])):
        name, mi, ti = ps[kind][k]
        m = model.Model(pk.entry(mi))
        tex = texture.load(pk.entry(ti))
        tex = texture.decode(tex[0] if isinstance(tex, (list, tuple)) else tex)
        if kind == "body":
            b = m.bbox
            shoulder = (b[3] * 0.27, b[4] - 13)
        for s in m.subsets:
            vb = m.vbs[s["vb"]]
            pos = np.array(vb["pos"])
            # 얼굴·머리·모자는 머리 뼈 기준 좌표다(앞이 +x, 위가 −y). 머리의 경계 상자에 맞게 돌려 올린다
            lo, hi = np.array(m.bbox[:3]), np.array(m.bbox[3:])
            if abs(pos[:, 1].max() - hi[1]) > 5:
                pos = np.stack([pos[:, 2], -pos[:, 1], pos[:, 0]], 1)
                pos += (lo + hi) / 2 - (pos.min(0) + pos.max(0)) / 2
            pos = lower_arms(pos, *shoulder)
            out.append((pos, vb.get("uv0"), m.triangles(s), tex, (200, 180, 150)))
        print(kind, name, m.bbox[1], m.bbox[4])
    model.render(out, sys.argv[2], 700, "front")
