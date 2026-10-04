"""항구 장면에서 배 댈 자리 찾기: python berth.py <장면 번호 …|all> [그림 폴더]

장면(GRM)을 위에서 내려다본 높이 격자로 만들고, 물(아무것도 없거나 y <= 0) 가운데
뭍에서 배 한 척 길이만큼 떨어진 칸 중 집들에 가장 가까운 칸을 고른다.
뱃머리 각은 가장 가까운 뭍과 나란하게. 결과는 settings.json 의 Berths 에 넣을 JSON.
"""
import glob
import json
import os
import sys

import numpy as np
from scipy import ndimage

import gvo
import town

CELL = 500.0          # 격자 한 칸
CLEAR = 6000.0        # 뭍에서 띄울 거리 (배 길이 ~12000 의 반 + 여유)
LAND_Y = 30.0         # 이보다 높으면 뭍
PAD = 24              # 장면 바깥으로 넓혀 보는 칸 수


def heightmap(scene):
    g = town.Grm(scene)
    tris = []
    for _, d in g.draws():
        vb = g.vbs[d["vb"]]
        idx = g.ibs[d["ib"]][d["istart"]:d["istart"] + 3 * d["tris"]].astype(np.int64).reshape(-1, 3)
        if idx.size == 0 or idx.max() >= len(vb["pos"]):
            continue
        tris.append(vb["pos"][idx])
    p = np.concatenate(tris)                      # (n, 3, 3)
    x0, z0 = p[:, :, 0].min(), p[:, :, 2].min()
    x1, z1 = p[:, :, 0].max(), p[:, :, 2].max()
    x0, z0 = x0 - PAD * CELL, z0 - PAD * CELL
    w, h = int((x1 - x0) / CELL) + 2 + PAD, int((z1 - z0) / CELL) + 2 + PAD
    grid = np.full((h, w), -1e9, np.float32)
    # 삼각형마다 고르게 점을 뿌려 가장 높은 값을 칸에 적는다
    for a in np.linspace(0, 1, 7):
        for b in np.linspace(0, 1 - a, max(1, int(round((1 - a) * 6)) + 1)):
            q = p[:, 0] * (1 - a - b) + p[:, 1] * a + p[:, 2] * b
            ix = ((q[:, 0] - x0) / CELL).astype(int)
            iz = ((q[:, 2] - z0) / CELL).astype(int)
            np.maximum.at(grid, (iz, ix), q[:, 1])
    return grid, x0, z0


def find(scene):
    grid, x0, z0 = heightmap(scene)
    land = grid > LAND_Y
    land = ndimage.binary_opening(land, iterations=1) | ndimage.binary_dilation(land & (grid > 300), iterations=1)
    if not land.any():
        return None
    dist = ndimage.distance_transform_edt(~land) * CELL
    h, w = land.shape
    # 바다 쪽: 뭍 가까이에 물밑(y <= 0) 메시가 있는 곳. 뭍 안쪽 가장자리는 반듯하게 잘려 있고 물밑 메시가 없다.
    shore = (grid > -1e8) & ~land & (dist <= 4 * CELL)
    if shore.sum() >= 3:
        zz, xx = np.nonzero(shore)
    else:
        pl = town.placements(scene)
        xx = np.array([(m[3, 0] - x0) / CELL for _, m in pl])
        zz = np.array([(m[3, 2] - z0) / CELL for _, m in pl])
    cx, cz = np.median(xx), np.median(zz)
    zz, xx = np.nonzero((dist >= CLEAR) & (dist <= CLEAR + 2 * CELL))
    if len(zz) == 0:
        return None
    k = np.argmin((xx - cx) ** 2 + (zz - cz) ** 2)
    bx, bz = xx[k], zz[k]
    # 가장 가까운 뭍 쪽
    gz, gx = np.gradient(dist)
    nx, nz = -gx[bz, bx], -gz[bz, bx]            # 뭍을 향하는 방향
    # 뱃머리는 뭍과 나란하게: 배 모형은 +Z 가 앞이고 RotationY(yaw) 로 돌린다
    yaw = float(np.arctan2(nz, -nx))
    return {"Scene": scene, "X": round(float(x0 + (bx + 0.5) * CELL)), "Z": round(float(z0 + (bz + 0.5) * CELL)),
            "Yaw": round(yaw, 2)}, (grid, land, bx, bz, nx, nz)


def picture(path, info):
    from PIL import Image, ImageDraw
    grid, land, bx, bz, nx, nz = info
    h, w = land.shape
    shade = np.clip((grid - LAND_Y) / 3000.0, 0, 1)
    img = np.zeros((h, w, 3), np.uint8)
    img[...] = (30, 60, 120)
    img[(grid > -1e8) & ~land] = (60, 110, 170)
    img[land] = np.stack([120 + 100 * shade, 150 + 60 * shade, 90 + 60 * shade], -1)[land].astype(np.uint8)
    im = Image.fromarray(img[::-1]).resize((w * 3, h * 3), Image.NEAREST)
    d = ImageDraw.Draw(im)
    px, py = bx * 3 + 1, (h - 1 - bz) * 3 + 1
    n = np.hypot(nx, nz) or 1
    tx, tz = -nz / n, nx / n                       # 뭍과 나란한 쪽
    L = 6000 / CELL * 3
    d.line([(px - tx * L, py + tz * L), (px + tx * L, py - tz * L)], fill=(255, 220, 0), width=5)
    d.ellipse([px - 4, py - 4, px + 4, py + 4], fill=(255, 0, 0))
    im.save(path)


if __name__ == "__main__":
    args = sys.argv[1:]
    out_dir = None
    if args and not args[-1].isdigit() and args[-1] != "all":
        out_dir = args.pop()
        os.makedirs(out_dir, exist_ok=True)
    if args == ["all"]:
        scenes = sorted(int(os.path.basename(f)[:8]) - 0x20000 for f in glob.glob(gvo.game_path(r"0002\001[3-9]*.bin"))
                        if 0x20000 + 7000 <= int(os.path.basename(f)[:8]) < 0x20000 + 7100)
    else:
        scenes = [int(a) for a in args]
    berths = []
    for s in scenes:
        try:
            r = find(s)
        except Exception as e:                      # 장면 파일이 없거나 짜임이 다르다
            print("#", s, type(e).__name__, e, file=sys.stderr)
            continue
        if r is None:
            print("#", s, "자리를 못 찾음", file=sys.stderr)
            continue
        berths.append(r[0])
        print("#", s, "shore", int(((r[1][0] > -1e8) & ~r[1][1]).sum()), file=sys.stderr)
        if out_dir:
            picture(os.path.join(out_dir, "%d.png" % s), r[1])
    print(json.dumps(berths, ensure_ascii=False))
