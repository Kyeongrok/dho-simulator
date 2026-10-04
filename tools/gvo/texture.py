"""MFTF0100 / XFTX0200 텍스처 읽개.

MFTF0100 (텍스처 묶음 — 같은 그림의 다른 형식판을 나란히 담는다)
  +0  "MFTF0100"
  +8  u32 파일 크기, u32 0, u32 개수 N
  +20 N × { u32 형식(D3DFORMAT 또는 FourCC), u32 자리, u32 크기, u16 너비, u16 높이 }
  자리에는 XFTX0200 이 통째로 들어 있다.

XFTX0200
  +0  "XFTX0200"
  +8  u32 크기, u32 0, u32 그림 수 M
  +20 u32 그림 레코드 자리 × M            (XFTX 처음부터)
  그림 레코드 (자리 R):
    +0 u16 너비, u16 높이, u32 형식
    +8 u8 0, u8 밉 수, u8 1, u8 벌 수 K
    +12 u32 자리 × n                       (R 부터)
        P8(41)    : n = 밉 수 + K   — 밉들 다음에 팔레트(256×BGRA) K 벌
        그 밖     : n = 밉 수 × max(K,1) — K 벌의 그림이 밉째로 차례로
    그 뒤 이름(NUL 로 4바이트 맞춤), 그 뒤 자료.
"""
import struct, sys, os
import numpy as np

D3DFMT = {20: "R8G8B8", 21: "A8R8G8B8", 22: "X8R8G8B8", 23: "R5G6B5", 25: "A1R5G5B5", 26: "A4R4G4B4",
          28: "A8", 41: "P8", 50: "L8", 51: "A8L8"}


def fmt_name(f):
    if f > 0xFFFF:
        return struct.pack("<I", f).decode("latin1")
    return D3DFMT.get(f, "fmt%d" % f)


def mftf_entries(b):
    assert b[:8] == b"MFTF0100", b[:8]
    cnt = struct.unpack_from("<I", b, 16)[0]
    out = []
    for i in range(cnt):
        fmt, off, sz, w, h = struct.unpack_from("<IIIHH", b, 20 + i * 16)
        out.append((fmt, off, sz, w, h))
    return out


class Image:
    pass


def xftx_images(b):
    """XFTX 안의 그림 레코드 목록."""
    assert b[:8] == b"XFTX0200", b[:8]
    m = struct.unpack_from("<I", b, 16)[0]
    out = []
    for i in range(m):
        r = struct.unpack_from("<I", b, 20 + i * 4)[0]
        im = Image()
        im.w, im.h, im.fmt = struct.unpack_from("<HHI", b, r)
        _, im.nmip, im.one, im.k = struct.unpack_from("<BBBB", b, r + 8)
        n = im.nmip + im.k if im.fmt == 41 else im.nmip * max(im.k, 1)
        im.offs = [r + o for o in struct.unpack_from("<%dI" % n, b, r + 12)]
        ne = r + 12 + n * 4
        im.name = b[ne:im.offs[0]].split(b"\0")[0].decode("latin1")
        im.data = b
        out.append(im)
    return out


def _c565(c):
    r = ((c >> 11) & 31) * 255 // 31
    g = ((c >> 5) & 63) * 255 // 63
    bl = (c & 31) * 255 // 31
    return np.stack([r, g, bl], -1).astype(np.uint8)


def _dxt(data, w, h, kind):
    bw, bh = max(1, (w + 3) // 4), max(1, (h + 3) // 4)
    bs = 8 if kind == 1 else 16
    a = np.frombuffer(data, np.uint8, bw * bh * bs).reshape(bh, bw, bs)
    col = a[:, :, bs - 8:]
    c0 = col[:, :, 0].astype(np.uint32) | col[:, :, 1].astype(np.uint32) << 8
    c1 = col[:, :, 2].astype(np.uint32) | col[:, :, 3].astype(np.uint32) << 8
    r0, r1 = _c565(c0).astype(np.int32), _c565(c1).astype(np.int32)
    pal = np.zeros((bh, bw, 4, 4), np.uint8)
    pal[..., 3] = 255
    pal[:, :, 0, :3] = r0
    pal[:, :, 1, :3] = r1
    four = (c0 > c1) if kind == 1 else np.ones_like(c0, bool)
    f = four[..., None]
    pal[:, :, 2, :3] = np.where(f, (2 * r0 + r1) // 3, (r0 + r1) // 2)
    pal[:, :, 3, :3] = np.where(f, (r0 + 2 * r1) // 3, 0)
    if kind == 1:
        pal[:, :, 3, 3] = np.where(four, 255, 0)
    bits = col[:, :, 4].astype(np.uint32) | col[:, :, 5].astype(np.uint32) << 8 | \
        col[:, :, 6].astype(np.uint32) << 16 | col[:, :, 7].astype(np.uint32) << 24
    out = np.zeros((bh, 4, bw, 4, 4), np.uint8)
    for py in range(4):
        for px in range(4):
            idx = (bits >> (2 * (py * 4 + px))) & 3
            out[:, py, :, px, :] = np.take_along_axis(pal, idx[..., None, None].repeat(4, -1), 2)[:, :, 0, :]
    if kind == 3:
        al = a[:, :, :8]
        for py in range(4):
            for px in range(4):
                i = py * 4 + px
                v = (al[:, :, i // 2] >> (4 * (i & 1))) & 15
                out[:, py, :, px, 3] = v * 17
    elif kind == 5:
        a0 = a[:, :, 0].astype(np.int32)
        a1 = a[:, :, 1].astype(np.int32)
        ab = np.zeros((bh, bw), np.uint64)
        for i in range(6):
            ab |= a[:, :, 2 + i].astype(np.uint64) << np.uint64(8 * i)
        ap = np.zeros((bh, bw, 8), np.int32)
        ap[..., 0], ap[..., 1] = a0, a1
        gt = a0 > a1
        for i in range(1, 7):
            ap[..., 1 + i] = np.where(gt, ((7 - i) * a0 + i * a1) // 7,
                                      ((5 - i) * a0 + i * a1) // 5 if i < 5 else (0 if i == 5 else 255))
        for py in range(4):
            for px in range(4):
                idx = ((ab >> np.uint64(3 * (py * 4 + px))) & np.uint64(7)).astype(np.int64)
                out[:, py, :, px, 3] = np.take_along_axis(ap, idx[..., None], 2)[..., 0]
    return out.reshape(bh * 4, bw * 4, 4)[:h, :w]


def decode(im, mip=0, variant=0):
    """그림 한 장을 RGBA uint8 (h, w, 4) 로."""
    w, h = max(1, im.w >> mip), max(1, im.h >> mip)
    b = im.data
    if im.fmt == 41:
        idx = np.frombuffer(b, np.uint8, w * h, im.offs[mip]).reshape(h, w)
        pal = np.frombuffer(b, np.uint8, 1024, im.offs[im.nmip + variant]).reshape(256, 4)
        return pal[idx][..., [2, 1, 0, 3]]
    off = im.offs[variant * im.nmip + mip]
    fn = fmt_name(im.fmt)
    if fn in ("DXT1", "DXT3", "DXT5"):
        return _dxt(b[off:], w, h, int(fn[3]))
    if fn in ("A8R8G8B8", "X8R8G8B8"):
        a = np.frombuffer(b, np.uint8, w * h * 4, off).reshape(h, w, 4)[..., [2, 1, 0, 3]].copy()
        if fn[0] == "X":
            a[..., 3] = 255
        return a
    if fn == "R8G8B8":
        a = np.frombuffer(b, np.uint8, w * h * 3, off).reshape(h, w, 3)[..., ::-1]
        return np.dstack([a, np.full((h, w), 255, np.uint8)])
    if fn in ("A4R4G4B4", "A1R5G5B5", "R5G6B5"):
        v = np.frombuffer(b, "<u2", w * h, off).reshape(h, w).astype(np.uint32)
        if fn == "A4R4G4B4":
            ch = [((v >> s) & 15) * 17 for s in (8, 4, 0, 12)]
        elif fn == "A1R5G5B5":
            ch = [((v >> 10) & 31) * 255 // 31, ((v >> 5) & 31) * 255 // 31, (v & 31) * 255 // 31, (v >> 15) * 255]
        else:
            ch = [((v >> 11) & 31) * 255 // 31, ((v >> 5) & 63) * 255 // 63, (v & 31) * 255 // 31, v * 0 + 255]
        return np.stack(ch, -1).astype(np.uint8)
    if fn in ("A8", "L8"):
        v = np.frombuffer(b, np.uint8, w * h, off).reshape(h, w)
        return np.stack([v, v, v, v if fn == "A8" else v * 0 + 255], -1)
    raise ValueError("형식 " + fn)


def load(b, prefer=("DXT3", "DXT1", "DXT5", "A8R8G8B8")):
    """MFTF 또는 XFTX 바이트에서 그림 레코드 목록. MFTF 면 P8 보다 prefer 형식판을 고른다."""
    b = bytes(b)
    if b[:8] == b"XFTX0200":
        return xftx_images(b)
    ents = mftf_entries(b)
    best = None
    for fmt, off, sz, w, h in ents:
        fn = fmt_name(fmt)
        rank = prefer.index(fn) if fn in prefer else 99
        if best is None or rank < best[0]:
            best = (rank, off, sz)
    return xftx_images(b[best[1]:best[1] + best[2]])


def save_png(arr, path):
    from PIL import Image as PI
    PI.fromarray(arr, "RGBA").save(path)


if __name__ == "__main__":
    import gvo
    from pack import Pack
    rel, idx, out = sys.argv[1], sys.argv[2], sys.argv[3]
    b = open(gvo.game_path(rel), "rb").read() if idx == "-" else bytes(Pack(rel).entry(int(idx)))
    for n, im in enumerate(load(b)):
        print(n, im.name, im.w, im.h, fmt_name(im.fmt), "mips", im.nmip, "k", im.k)
        save_png(decode(im), "%s_%d_%s.png" % (out, n, im.name))
