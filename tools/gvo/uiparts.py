"""화면 부품 묶음(0010\\local\\gm00000N.bin): python uiparts.py <N> <out 폴더>

MWC 덩이 하나. u32 조각 수, u32 장 수, u32 장들의 자리.
조각(28바이트): u32 장 번호, f32 u0, v0, u1, v1, u32 너비, u32 높이.
장: u16 너비, u16 높이, u32 21(A8R8G8B8), u32 ?, u32 바이트 수 × 2, BGRA.
"""
import os
import struct
import sys

import numpy as np

import gvo


def load(n):
    c = gvo.mwc_chunks(open(gvo.game_path(r"0010\local\gm00000%d.bin" % n), "rb").read())[0]
    count, pages, off = struct.unpack_from("<III", c, 0)
    sprites = [struct.unpack_from("<I4fII", c, 12 + i * 28) for i in range(count)]
    sheets = []
    for _ in range(pages):
        w, h, fmt, _, size, _ = struct.unpack_from("<HHIIII", c, off)
        sheets.append(np.frombuffer(c, np.uint8, w * h * 4, off + 20).reshape(h, w, 4)[:, :, [2, 1, 0, 3]])
        off += 20 + size
    return sprites, sheets


if __name__ == "__main__":
    from PIL import Image, ImageDraw
    n, out = int(sys.argv[1]), sys.argv[2]
    os.makedirs(out, exist_ok=True)
    sprites, sheets = load(n)
    print(len(sprites), "조각", len(sheets), "장", sheets[0].shape)
    for i, s in enumerate(sheets):
        im = Image.new("RGBA", (s.shape[1], s.shape[0]), (60, 60, 90, 255))
        im.alpha_composite(Image.fromarray(s, "RGBA"))
        scale = max(1, 512 // s.shape[1])
        im = im.resize((s.shape[1] * scale, s.shape[0] * scale), Image.NEAREST)
        d = ImageDraw.Draw(im)
        for k, (page, u0, v0, u1, v1, w, h) in enumerate(sprites):
            if page == i and scale * w >= 24:
                d.text((u0 * s.shape[1] * scale + 1, v0 * s.shape[0] * scale + 1), str(k), fill=(255, 255, 0, 255))
        im.save(os.path.join(out, "gm%d_%02d.png" % (n, i)))
