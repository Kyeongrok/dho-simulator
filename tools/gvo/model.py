"""XKMD0210 / XKMD0211 모델 읽개.

머리 (0x80 바이트)
  +0x00 "XKMD0210"|"XKMD0211"
  +0x08 u32 파일 크기, u32 0x80(머리 크기), u32 0
  +0x14 f32×3 경계상자 작은 끝, +0x20 f32×3 큰 끝
  +0x2C u16×10 개수: [정점버퍼, 행렬, 마디, 재질, 텍스처단, 0, 부분, 0, 부분B(반투명), 0]
  +0x40 u32 색인 바이트 수, u32 정점 바이트 수, u32 0
  +0x4C u32×13 구역 자리 (0 = 없음):
        0 정점버퍼 설명   1 행렬(4x4 f32)   2 행렬-마디 짝(8바이트)   3 마디(0x2C: u16 번호, i16 부모, f32 배율×3, 사원수×4, 자리×3)
        4 이름            5 재질            6 텍스처단(0x20)          7 ?
        8 부분            9 부분B           10 색인(u16)              11 정점   12 ?

정점버퍼 설명 (0x34 바이트씩, 구역은 16바이트 맞춤)
  u32 FVF, u32 한 정점 바이트, u32 정점 수, u32 정점 구역 안 자리(바이트),
  u32 1, 0, 0, u32 101(D3DFMT_INDEX16), u32 색인 수, u32 색인 구역 안 자리(바이트), u32 1, 0, 0

부분 구역: u32 자리 × 개수 (구역 처음부터), 레코드 0x40 바이트
  +0x00 u16 8, u16 플래그(0x8000 = 반투명 쪽)
  +0x04 u16 ?, u16 정점버퍼 번호
  +0x08 u16 재질 번호, u8 5, u8 6
  +0x0C u32 텍스처단 수, +0x10 u16×8 텍스처단 번호
  +0x28 u32 1, u32 도형(5 = 삼각형 띠, 4 = 삼각형 목록)
  +0x30 u32 정점 시작, u32 정점 수, u32 색인 시작(낱개), u32 도형 수

텍스처단 (0x20): u32 켬, i16 텍스처 번호(-1 없음), i16 -1, u8 U감기, u8 V감기, u16 UV벌, 그 뒤 D3D 단 상태(u16들)
재질: u32 자리 표, 레코드 = u16 크기(0x20/0x30), u16 갈래, f32 확산 RGBA, f32 ...(환경/반사)
"""
import struct
import sys

import numpy as np

SEC = ["vb", "matrix", "bonemap", "node", "name", "material", "stage", "s7",
       "subset", "subsetB", "index", "vertex", "s12"]


def fvf_layout(fvf):
    """{이름: (바이트 자리, 낱값 수)} — pos, weights, normal, diffuse, uv0.."""
    lay = {"pos": (0, 3)}
    o = 12
    nb = {0x2: 0, 0x6: 1, 0x8: 2, 0xA: 3, 0xC: 4, 0xE: 5}.get(fvf & 0xE, 0)
    if nb:
        lay["weights"] = (o, nb)
        o += 4 * nb
    if fvf & 0x10:
        lay["normal"] = (o, 3)
        o += 12
    if fvf & 0x20:
        o += 4
    if fvf & 0x40:
        lay["diffuse"] = (o, 1)
        o += 4
    if fvf & 0x80:
        o += 4
    for t in range((fvf >> 8) & 0xF):
        lay["uv%d" % t] = (o, 2)
        o += 8
    lay["size"] = o
    return lay


class Model:
    def __init__(self, b):
        b = bytes(b)
        assert b[:6] == b"XKMD02", b[:8]
        self.raw = b
        self.bbox = struct.unpack_from("<6f", b, 0x14)
        self.counts = struct.unpack_from("<10H", b, 0x2C)
        self.index_bytes, self.vertex_bytes = struct.unpack_from("<II", b, 0x40)
        self.sec = dict(zip(SEC, struct.unpack_from("<13I", b, 0x4C)))
        nvb, nmat, nnode, nmtl, nstage, _, nsub, _, nsub_b, _ = self.counts
        o = self.sec["name"]
        self.name = b[o + 4:o + 36].split(b"\0")[0].decode("latin1") if o else ""
        self.vbs = []
        for i in range(nvb):
            f = struct.unpack_from("<13I", b, self.sec["vb"] + i * 0x34)
            fvf, stride, vcount, voff, _, _, _, _ifmt, icount, ioff = f[:10]
            lay = fvf_layout(fvf)
            va = np.frombuffer(b, np.uint8, vcount * stride, self.sec["vertex"] + voff).reshape(vcount, stride)
            fl = va.view("<f4") if stride % 4 == 0 else None
            vb = {"fvf": fvf, "stride": stride, "count": vcount, "layout": lay, "raw": va, "f": fl,
                  "index": np.frombuffer(b, "<u2", icount, self.sec["index"] + ioff)}
            for k in ("pos", "normal", "uv0", "uv1", "uv2", "weights"):
                if k in lay and fl is not None:
                    a, n = lay[k]
                    vb[k] = fl[:, a // 4:a // 4 + n]
            self.vbs.append(vb)
        self.matrices = [np.frombuffer(b, "<f4", 16, self.sec["matrix"] + i * 64).reshape(4, 4)
                         for i in range(nmat)] if self.sec["matrix"] else []
        self.nodes = []
        if self.sec["node"]:
            for i in range(nnode):
                o = self.sec["node"] + i * 0x2C
                idx, parent = struct.unpack_from("<Hh", b, o)
                self.nodes.append((idx, parent, struct.unpack_from("<10f", b, o + 4)))
        self.materials = []
        if self.sec["material"]:
            m0 = self.sec["material"]
            for i in range(nmtl):
                o = m0 + struct.unpack_from("<I", b, m0 + 4 * i)[0]
                size, kind = struct.unpack_from("<HH", b, o)
                self.materials.append((kind, struct.unpack_from("<%df" % ((size - 4) // 4), b, o + 4)))
        self.stages = []
        if self.sec["stage"]:
            for i in range(nstage):
                o = self.sec["stage"] + i * 0x20
                on, tex, t2, au, av, uvset = struct.unpack_from("<IhhBBH", b, o)
                self.stages.append({"on": on, "tex": tex, "tex2": t2, "wrap": (au, av), "uvset": uvset,
                                    "state": struct.unpack_from("<10H", b, o + 12)})
        self.subsets = self._subsets("subset", nsub) + self._subsets("subsetB", nsub_b)

    def _subsets(self, key, n):
        b, s0, out = self.raw, self.sec[key], []
        if not s0:
            return out
        for i in range(n):
            o = s0 + struct.unpack_from("<I", b, s0 + 4 * i)[0]
            _eight, flag, _unk, vb, mtl, _c5, _c6, nst = struct.unpack_from("<HHHHHBBI", b, o)
            stages = struct.unpack_from("<8H", b, o + 0x10)[:nst]
            _one, prim, vstart, vcount, istart, pcount = struct.unpack_from("<6I", b, o + 0x28)
            out.append({"flag": flag, "vb": vb, "material": mtl, "stages": stages, "prim": prim,
                        "vstart": vstart, "vcount": vcount, "istart": istart, "pcount": pcount,
                        "alpha": key == "subsetB"})
        return out

    def triangles(self, s):
        """부분 하나의 삼각형 색인 (n,3) — 정점버퍼 처음부터 센 번호."""
        idx = self.vbs[s["vb"]]["index"]
        if s["prim"] == 5:
            st = idx[s["istart"]:s["istart"] + s["pcount"] + 2].astype(np.int64)
            t = np.stack([st[:-2], st[1:-1], st[2:]], 1)
            t[1::2] = t[1::2][:, [1, 0, 2]]
            ok = (t[:, 0] != t[:, 1]) & (t[:, 1] != t[:, 2]) & (t[:, 0] != t[:, 2])
            t = t[ok]
        else:
            t = idx[s["istart"]:s["istart"] + s["pcount"] * 3].astype(np.int64).reshape(-1, 3)
        return t

    def describe(self):
        print("name", repr(self.name), "bbox", ["%.0f" % v for v in self.bbox], "counts", self.counts)
        for i, vb in enumerate(self.vbs):
            print("  vb%d fvf=%#x stride=%d n=%d idx=%d" % (i, vb["fvf"], vb["stride"], vb["count"], len(vb["index"])))
        for i, m in enumerate(self.materials):
            print("  mtl%d kind=%d %s" % (i, m[0], ["%.2f" % v for v in m[1]]))
        for i, st in enumerate(self.stages):
            print("  stage%d" % i, st)
        for i, n in enumerate(self.nodes):
            print("  node%d" % i, n[0], n[1], ["%.2f" % v for v in n[2]])
        for i, s in enumerate(self.subsets):
            t = self.triangles(s)
            print("  sub%d" % i, s, "tri", len(t), "idx", (int(t.min()), int(t.max())) if len(t) else None)

    def write_obj(self, path):
        with open(path, "w") as f:
            base = 1
            for vi, vb in enumerate(self.vbs):
                if "pos" not in vb:
                    continue
                for p in vb["pos"]:
                    f.write("v %f %f %f\n" % tuple(p))
                uv = vb.get("uv0")
                for i in range(vb["count"]):
                    f.write("vt %f %f\n" % ((uv[i, 0], 1 - uv[i, 1]) if uv is not None else (0, 0)))
                for si, s in enumerate(self.subsets):
                    if s["vb"] != vi:
                        continue
                    f.write("g sub%d_mtl%d\n" % (si, s["material"]))
                    for a, b_, c in self.triangles(s) + base:
                        f.write("f %d/%d %d/%d %d/%d\n" % (a, a, b_, b_, c, c))
                base += vb["count"]


def render(parts, path, size=900, view="side", bg=(40, 60, 90)):
    """parts = [(pos(n,3), uv(n,2)|None, tris(m,3), tex RGBA|None, 색)] 을 정사영으로 그린다."""
    from PIL import Image
    allp = np.concatenate([p[0] for p in parts])
    ax = {"side": (2, 1, 0), "top": (0, 2, 1), "front": (0, 1, 2), "iso": None}[view]

    def proj(p):
        if ax is None:
            a, e = np.radians(35), np.radians(25)
            x = p[:, 0] * np.cos(a) + p[:, 2] * np.sin(a)
            z = -p[:, 0] * np.sin(a) + p[:, 2] * np.cos(a)
            y = p[:, 1] * np.cos(e) - z * np.sin(e)
            d = p[:, 1] * np.sin(e) + z * np.cos(e)
            return np.stack([x, y, d], 1)
        return np.stack([p[:, ax[0]], p[:, ax[1]], p[:, ax[2]]], 1)

    q = proj(allp)
    mn, mx = q[:, :2].min(0), q[:, :2].max(0)
    sc = (size - 20) / max(mx - mn)
    W, H = int((mx[0] - mn[0]) * sc) + 20, int((mx[1] - mn[1]) * sc) + 20
    img = np.zeros((H, W, 3), np.uint8)
    img[:] = bg
    zb = np.full((H, W), -1e30)
    light = np.array([0.4, 0.8, 0.45])
    light /= np.linalg.norm(light)
    for pos, uv, tris, tex, col in parts:
        q = proj(pos)
        sx = (q[:, 0] - mn[0]) * sc + 10
        sy = H - ((q[:, 1] - mn[1]) * sc + 10)
        for t in tris:
            p3 = pos[t]
            n = np.cross(p3[1] - p3[0], p3[2] - p3[0])
            ln = np.linalg.norm(n)
            if ln == 0:
                continue
            sh = 0.45 + 0.55 * abs(float(n @ light) / ln)
            xs, ys, zs = sx[t], sy[t], q[t, 2]
            x0, x1 = int(max(0, np.floor(xs.min()))), int(min(W - 1, np.ceil(xs.max())))
            y0, y1 = int(max(0, np.floor(ys.min()))), int(min(H - 1, np.ceil(ys.max())))
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            d = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
            if abs(d) < 1e-9:
                continue
            w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / d
            w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / d
            w2 = 1 - w0 - w1
            m = (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
            z = w0 * zs[0] + w1 * zs[1] + w2 * zs[2]
            sub = zb[y0:y1 + 1, x0:x1 + 1]
            m &= z > sub
            if not m.any():
                continue
            if tex is not None and uv is not None:
                u = w0 * uv[t[0], 0] + w1 * uv[t[1], 0] + w2 * uv[t[2], 0]
                v = w0 * uv[t[0], 1] + w1 * uv[t[1], 1] + w2 * uv[t[2], 1]
                th, tw = tex.shape[:2]
                c = tex[np.floor(v * th).astype(int) % th, np.floor(u * tw).astype(int) % tw]
                m &= c[..., 3] > 40
                rgb = c[..., :3].astype(float) * sh
            else:
                rgb = np.broadcast_to(np.array(col, float) * sh, gx.shape + (3,))
            sub[m] = z[m]
            img[y0:y1 + 1, x0:x1 + 1][m] = np.clip(rgb[m], 0, 255).astype(np.uint8)
    Image.fromarray(img).save(path)


if __name__ == "__main__":
    from pack import Pack
    mdl = Model(Pack(sys.argv[1]).entry(int(sys.argv[2])))
    mdl.describe()
    if len(sys.argv) > 3:
        mdl.write_obj(sys.argv[3])
