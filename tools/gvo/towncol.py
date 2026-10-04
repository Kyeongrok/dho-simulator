"""시내 장면 정보(GRI)의 걷는 면과 충돌: python towncol.py <장면 번호> [out.png]

머리(u32 들):
  +0x28 잔 충돌 (자리, 정점 수, 마디 수) — 정점 셋 = 삼각형 하나, 경계 상자 나무 32바이트 × 마디, 삼각형마다 2바이트
  +0x38 벽      (자리, 정점 수, 마디 수) — 같은 짜임, 2바이트 꼬리 없음
  +0x50 선분    (자리, 선분 수, 마디 수) — 36바이트 + 2D 나무 20바이트
  +0x5C 격자 자리 — u32 가로, u32 세로, f32 400 × 2, f32 높이 × (가로+1) × (세로+1), u8 판 × 가로 × 세로
  +0x68 점      (자리, 점 수) — u32 갈래, f32 × 3
  +0x70 배치    (자리, 수)
그림: 누운 잔 충돌(계단·분수 턱) = 분홍, 선 잔 충돌 = 갈색, 벽 = 검정 선, 선분 = 빨강, 점 = 초록.
"""
import collections
import struct
import sys

import numpy as np

import gvo


def read(scene):
    d = open(gvo.game_path(r"0002\%08d.bin" % (0x10000 + scene)), "rb").read()
    h = struct.unpack_from("<32I", d, 0)
    a_at, a_v, a_n, _shadow, c_at, c_v, c_n = h[10:17]
    seg_at, seg_n, seg_m, grid_at, _g2, _g3, pt_at, pt_n, pl_at, pl_n = h[20:30]
    gw, gh, cell, _ = struct.unpack_from("<IIff", d, grid_at)
    return dict(
        detail=np.frombuffer(d, "<f4", a_v * 3, a_at).reshape(-1, 3, 3),
        detail_kind=np.frombuffer(d, np.uint8, (a_v // 3) * 2, a_at + a_v * 12 + a_n * 32).reshape(-1, 2),
        walls=np.frombuffer(d, "<f4", c_v * 3, c_at).reshape(-1, 3, 3),
        lines=[struct.unpack_from("<I4f4f", d, seg_at + 36 * i) for i in range(seg_n)],
        grid=(gw, gh, cell), heights=np.frombuffer(d, "<f4", (gw + 1) * (gh + 1), grid_at + 16).reshape(gh + 1, gw + 1),
        points=[struct.unpack_from("<Ifff", d, pt_at + 16 * i) for i in range(pt_n)],
        placements=struct.unpack_from("<%dI" % pl_n, d, pl_at) if pl_n else ())


def flat(tris):
    n = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    return np.abs(n[:, 1]) > 0.5 * (np.linalg.norm(n, axis=1) + 1e-9)


if __name__ == "__main__":
    t = read(int(sys.argv[1]))
    gw, gh, cell = t["grid"]
    print("격자 %d x %d (칸 %g)  높이 %.0f ~ %.0f" % (gw, gh, cell, t["heights"].min(), t["heights"].max()))
    f = flat(t["detail"])
    print("잔 충돌 %d (누운 것 %d, 선 것 %d)  벽 %d  선분 %d  점 %s  배치 %s" % (
        len(t["detail"]), f.sum(), (~f).sum(), len(t["walls"]), len(t["lines"]),
        dict(collections.Counter(p[0] for p in t["points"])), dict(collections.Counter(t["placements"]))))
    print("잔 충돌 2바이트:", collections.Counter(map(tuple, t["detail_kind"].tolist())).most_common(8))
    if len(sys.argv) > 2:
        from PIL import Image, ImageDraw
        s = 0.02
        base = np.clip((t["heights"] - 0) / max(1.0, t["heights"].max()) * 120 + 90, 0, 255).astype(np.uint8)
        rgb = np.stack([base, base, (base * 0.8).astype(np.uint8)], -1)
        rgb[t["heights"] <= 20] = (40, 80, 150)
        im = Image.fromarray(rgb).resize((int(gw * cell * s), int(gh * cell * s)), Image.BILINEAR)
        dr = ImageDraw.Draw(im)
        for tri, is_flat in sorted(zip(t["detail"], f), key=lambda x: x[0][:, 1].mean()):
            dr.polygon([(p[0] * s, p[2] * s) for p in tri], fill=(230, 120, 200) if is_flat else (110, 70, 40))
        for tri in t["walls"]:
            dr.line([(p[0] * s, p[2] * s) for p in tri] + [(tri[0][0] * s, tri[0][2] * s)], fill=(0, 0, 0))
        for _, ax, ay, az, aw, bx, by, bz, bw in t["lines"]:
            dr.line([(ax * s, az * s), (bx * s, bz * s)], fill=(255, 0, 0), width=2)
        for _, x, y, z in t["points"]:
            dr.ellipse([x * s - 3, z * s - 3, x * s + 3, z * s + 3], fill=(0, 220, 0))
        im.save(sys.argv[2])
