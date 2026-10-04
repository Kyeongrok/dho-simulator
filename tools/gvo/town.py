"""도시(항구) 지형 읽개 — 0002\\NNNNNNNN.bin

파일 이름(10진수) = (갈래 << 16) | 도시 id.   갈래 1 = GRI(정보), 2 = GRM(메시).
예) 도시 2001 → 00067537.bin (GRI), 00133073.bin (GRM)

GRM
  +0x00 "GRM ", u32 1, u32 도시 id
  +0x0C u32 덩이 표 자리(0x34), u32 덩이 수
  +0x14 u32 나무(경계상자 트리) 자리, u32 마디 수      마디 28바이트 = f32×6 상자, u16, u16
  +0x1C u32 GTEX 자리
  +0x20 u32 ? (0x3FAF0 바이트짜리 덩어리 — 풀지 못함)
  +0x24 u32 정점버퍼 표 자리, u32 정점버퍼 수           표 16바이트 = u32 자리, u32 크기, u32 FVF, u32 한 정점 바이트
  +0x2C u32 색인버퍼 표 자리, u32 색인버퍼 수           표 8바이트 = u32 자리, u32 크기 (u16 색인)
  덩이 표: (u32 자리, u32 크기) × 덩이 수
  덩이: +0x08 u16 그리기 레코드 수 …, +0x18 f32×6 경계상자(×2), +0x60 4x4 행렬 ×2, +0xE0 그리기 레코드
  그리기 레코드 24바이트:
    u16 플래그, u16 텍스처 번호(0xFFFF 없음), u8 갈래(0x11/0x13/0x21/0x31…; 0xFF 는 그리지 않는 것),
    u8 0xFF, u8 정점버퍼 번호, u8 색인버퍼 번호, u8 0, u8 텍스처 묶음(1 = 이 파일의 GTEX, 0xFF = 공용),
    u16 0xFFFF, u32 0, u16 정점 시작, u16 정점 수, u16 색인 시작, u16 삼각형 수   (삼각형 목록, 색인은 정점버퍼 처음부터)
  정점: FVF 0x142 = 자리 f32×3, 색 BGRA, UV f32×2 (24바이트) / 0x146 = 자리, 가중치 f32, 색, UV (28바이트)

GTEX
  "GTEX", u32 0, u32 개수, u32 자리 × 개수 (GTEX 처음부터)
  텍스처: u32 D3DFORMAT(21 = A8R8G8B8, 20 = R8G8B8), u16 너비, u16 높이, u8 ?, u8 밉 수, u8 ?, u8 0, 그 뒤 밉 사슬
"""
import struct
import sys

import numpy as np

import gvo


def town_path(kind, town_id):
    return gvo.game_path(r"0002\%08d.bin" % ((kind << 16) | town_id))


class Gtex:
    def __init__(self, b, off):
        assert b[off:off + 4] == b"GTEX"
        self.b, self.off = b, off
        self.count = struct.unpack_from("<I", b, off + 8)[0]
        self.offs = struct.unpack_from("<%dI" % self.count, b, off + 12)

    def info(self, i):
        o = self.off + self.offs[i]
        fmt, w, h, _a, nmip, _c, _d = struct.unpack_from("<IHHBBBB", self.b, o)
        return fmt, w, h, nmip, o + 12

    def image(self, i, mip=0):
        """RGBA uint8 (h, w, 4)."""
        fmt, w, h, nmip, o = self.info(i)
        bpp = 4 if fmt == 21 else 3
        mip = min(mip, nmip - 1)
        for _ in range(mip):
            o += w * h * bpp
            w, h = max(1, w // 2), max(1, h // 2)
        a = np.frombuffer(self.b, np.uint8, w * h * bpp, o).reshape(h, w, bpp)
        if bpp == 4:
            return a[..., [2, 1, 0, 3]]
        return np.dstack([a[..., ::-1], np.full((h, w), 255, np.uint8)])


class Grm:
    def __init__(self, town_id):
        with open(town_path(2, town_id), "rb") as f:
            b = self.b = f.read()
        assert b[:4] == b"GRM "
        self.id = struct.unpack_from("<I", b, 8)[0]
        (ct, cn, self.tree_off, self.tree_n, gt, self.unk20,
         vt, vn, it, in_) = struct.unpack_from("<10I", b, 0x0C)
        self.chunks = [struct.unpack_from("<II", b, ct + 8 * i) for i in range(cn)]
        self.gtex = Gtex(b, gt)
        self.vbs = []
        for i in range(vn):
            o, s, fvf, stride = struct.unpack_from("<4I", b, vt + 16 * i)
            raw = np.frombuffer(b, np.uint8, (s // stride) * stride, o).reshape(-1, stride)
            col = 16 if fvf == 0x146 else 12
            self.vbs.append({"fvf": fvf, "stride": stride,
                             "pos": raw[:, :12].copy().view("<f4"),
                             "color": raw[:, col:col + 4],          # BGRA
                             "uv": raw[:, col + 4:col + 12].copy().view("<f4")})
        self.ibs = []
        for i in range(in_):
            o, s = struct.unpack_from("<II", b, it + 8 * i)
            self.ibs.append(np.frombuffer(b, "<u2", s // 2, o))

    def draws(self):
        """(덩이 번호, 레코드 dict) 를 차례로."""
        b = self.b
        for ci, (o, s) in enumerate(self.chunks):
            n = (s - 0xE0) // 24
            for r in range(n):
                rec = struct.unpack_from("<HHBBBBBBHIHHHH", b, o + 0xE0 + 24 * r)
                flags, tex, kind, _ff, vb, ib, _z, texset, _w, _d, vs, vc, is_, pc = rec
                if kind in (0, 0xFF) or pc == 0 or vb >= len(self.vbs) or ib >= len(self.ibs):
                    continue
                yield ci, {"flags": flags, "tex": tex, "kind": kind, "vb": vb, "ib": ib, "texset": texset,
                           "vstart": vs, "vcount": vc, "istart": is_, "tris": pc}

    def bbox(self):
        return struct.unpack_from("<6f", self.b, self.tree_off)


def topdown(g, path, size=1600):
    """위에서 본 그림 — 삼각형마다 (텍스처 평균색 × 정점색) 으로 칠한다."""
    from PIL import Image, ImageDraw
    x0, _, z0, x1, _, z1 = g.bbox()
    xs = [vb["pos"][:, 0] for vb in g.vbs]
    zs = [vb["pos"][:, 2] for vb in g.vbs]
    x0, x1 = min(a.min() for a in xs), max(a.max() for a in xs)
    z0, z1 = min(a.min() for a in zs), max(a.max() for a in zs)
    sc = size / max(x1 - x0, z1 - z0)
    W, H = int((x1 - x0) * sc) + 1, int((z1 - z0) * sc) + 1
    avg = {}
    polys = []
    for _, d in g.draws():
        vb = g.vbs[d["vb"]]
        idx = g.ibs[d["ib"]][d["istart"]:d["istart"] + 3 * d["tris"]].astype(np.int64).reshape(-1, 3)
        if idx.size == 0 or idx.max() >= len(vb["pos"]):
            continue
        key = (d["texset"], d["tex"])
        if key not in avg:
            c = np.array([128.0, 128.0, 128.0])
            if d["tex"] < g.gtex.count:
                im = g.gtex.image(d["tex"], 99).reshape(-1, 4).astype(float)
                c = im[:, :3].mean(0)
            avg[key] = c
        p = vb["pos"][idx]                                  # (n,3,3)
        col = vb["color"][idx][..., [2, 1, 0]].astype(float).mean(1) / 255.0
        rgb = np.clip(avg[key][None, :] * col * 2.0, 0, 255).astype(np.uint8)
        y = p[:, :, 1].mean(1)
        sx = (p[:, :, 0] - x0) * sc
        sy = (p[:, :, 2] - z0) * sc
        for k in range(len(idx)):
            polys.append((y[k], sx[k], sy[k], rgb[k]))
    polys.sort(key=lambda t: t[0])
    img = Image.new("RGB", (W, H), (10, 20, 40))
    dr = ImageDraw.Draw(img)
    for _, sx, sy, rgb in polys:
        dr.polygon([(sx[0], H - sy[0]), (sx[1], H - sy[1]), (sx[2], H - sy[2])], fill=tuple(int(v) for v in rgb))
    img.save(path)
    return len(polys)


if __name__ == "__main__":
    g = Grm(int(sys.argv[1]))
    print("id", g.id, "chunks", len(g.chunks), "vb", [(hex(v["fvf"]), len(v["pos"])) for v in g.vbs],
          "ib", len(g.ibs), "tex", g.gtex.count, "bbox", ["%.0f" % v for v in g.bbox()])
    import collections
    c = collections.Counter((d["flags"], d["kind"], d["texset"]) for _, d in g.draws())
    print(sorted(c.items()))
    if len(sys.argv) > 2:
        print("tris", topdown(g, sys.argv[2]))


# ── 공용 물체 묶음 (GRD) ──────────────────────────────────────────────────────
# nt000000.bin : u32 개수, (u32 물체 id, u32 0, u32 자리) × 개수.  id < 0x10000 → no000000.bin, 그 밖 → no100000.bin
# no000000.bin / no100000.bin : u32 물체 수, 그 뒤 GRD 가 죽 이어진다. GRD 안의 자리 값은 모두 파일 처음부터 센다.
# no200000.bin : u32 0x10, u32 크기, 0, 0, 그 뒤 GTEX (물체들이 같이 쓰는 텍스처)
# GRD 머리 (u32): "GRD ", 1, 덩이 표 자리, 덩이 수, -1, 0, 정점버퍼 자리, 정점버퍼 수, 색인버퍼 자리, 색인버퍼 수, …(충돌 자료)
#   정점버퍼 표·색인버퍼 표는 GRM 과 같다. FVF 0x1D2(40바이트) = 자리 f32×3, 법선 f32×3, 색 BGRA, 색2 BGRA, UV f32×2
#   덩이·그리기 레코드는 GRM 과 같다(텍스처 번호는 no200000 의 GTEX 번호).

_lib = {}


def _libfile(name):
    if name not in _lib:
        with open(gvo.game_path("0002\\" + name), "rb") as f:
            _lib[name] = f.read()
    return _lib[name]


def object_index():
    nt = _libfile("nt000000.bin")
    n = struct.unpack_from("<I", nt, 0)[0]
    out = {}
    for i in range(n):
        oid, _, off = struct.unpack_from("<III", nt, 4 + 12 * i)
        out[oid] = ("no000000.bin" if oid < 0x10000 else "no100000.bin", off)
    return out


def shared_gtex():
    return Gtex(_libfile("no200000.bin"), 0x10)


class Grd(Grm):
    def __init__(self, obj_id):
        name, off = object_index()[obj_id]
        b = self.b = _libfile(name)
        assert b[off:off + 4] == b"GRD ", b[off:off + 4]
        self.id = obj_id
        _, _, ct, cn, _, _, vo, vn, io, in_ = struct.unpack_from("<10I", b, off)
        self.chunks = [struct.unpack_from("<II", b, ct + 8 * i) for i in range(cn)]
        self.gtex = shared_gtex()
        self.vbs = []
        for i in range(vn):
            o, size, fvf, stride = struct.unpack_from("<4I", b, vo + 16 * i)
            raw = np.frombuffer(b, np.uint8, (size // stride) * stride, o).reshape(-1, stride)
            col = 24 if fvf & 0x10 else 12
            vb = {"fvf": fvf, "stride": stride, "pos": raw[:, :12].copy().view("<f4"),
                  "color": raw[:, col:col + 4], "uv": raw[:, stride - 8:stride].copy().view("<f4")}
            if fvf & 0x10:
                vb["normal"] = raw[:, 12:24].copy().view("<f4")
            self.vbs.append(vb)
        self.ibs = []
        for i in range(in_):
            o, size = struct.unpack_from("<II", b, io + 8 * i)
            self.ibs.append(np.frombuffer(b, "<u2", size // 2, o))

    def bbox(self):
        p = np.concatenate([v["pos"] for v in self.vbs])
        mn, mx = p.min(0), p.max(0)
        return (mn[0], mn[1], mn[2], mx[0], mx[1], mx[2])


def placements(town_id):
    """GRI 끝의 물체 배치: [(물체 id, 4x4 행렬(행 벡터식, 넷째 줄이 자리))]."""
    with open(town_path(1, town_id), "rb") as f:
        b = f.read()
    # 머리에서 (배치 머리 자리, 1, 번호표 자리, 개수) 를 찾는다 — 번호표 자리 = 머리 자리 + 16
    out = []
    for o in range(0x2C, 0x100, 4):
        a, one, ids, n = struct.unpack_from("<4I", b, o)
        if one == 1 and ids == a + 16 and 0 < n < 10000 and ids + n * 68 <= len(b):
            idv = struct.unpack_from("<%dI" % n, b, ids)
            for i in range(n):
                m = np.frombuffer(b, "<f4", 16, ids + 4 * n + 64 * i).reshape(4, 4)
                out.append((idv[i], m))
            break
    return out
