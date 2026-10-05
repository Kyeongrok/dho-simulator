"""화면 그림 묶음(0010\\000N\\xx): python imageset.py <0001\\sf> <out.png> [시작 그림] [장 수]

색인 xx000000.bin: u32 id 수 n, u32 무리 수, u32 너비, u32 높이, u32 그림 수 m, u32 파일 수,
  id 줄 20바이트 × n (무리, id, 그림 번호, 너비, 높이), 그림 줄 28바이트 × m (번호, 너비, 높이, 파일, 자리, 크기, 1).
자료 xx00000(k+1).bin 의 자리에 MWC 덩이 — 풀면 BGRA.
"""
import struct
import sys
import zlib

import numpy as np

import gvo


class ImageSet:
    def __init__(self, prefix):
        self.prefix = gvo.game_path("0010\\" + prefix)
        b = open(self.prefix + "000000.bin", "rb").read()
        n, self.groups, self.w, self.h, m, self.files = struct.unpack_from("<6I", b, 0)
        self.ids = [struct.unpack_from("<5I", b, 24 + i * 20) for i in range(n)]
        self.images = {r[0]: r for r in (struct.unpack_from("<7I", b, 24 + n * 20 + i * 28) for i in range(m))}

    def pixels(self, image):
        _, w, h, f, at, size, _ = self.images[image]
        with open("%s%06d.bin" % (self.prefix, f + 1), "rb") as fh:
            fh.seek(at)
            d = fh.read(size)
        return np.frombuffer(zlib.decompress(d[12:]), np.uint8).reshape(h, w, 4)[:, :, [2, 1, 0, 3]]


if __name__ == "__main__":
    from PIL import Image, ImageDraw
    s = ImageSet(sys.argv[1])
    start = int(sys.argv[3]) if len(sys.argv) > 3 else 0
    count = int(sys.argv[4]) if len(sys.argv) > 4 else 120
    print("id", len(s.ids), "무리", s.groups, "그림", len(s.images), s.w, "x", s.h)
    shown = [r for r in s.ids if r[2] >= start][:count]
    cols = max(1, 1500 // (s.w * 2 + 8))
    sheet = Image.new("RGB", (cols * (s.w * 2 + 8), ((len(shown) + cols - 1) // cols) * (s.h * 2 + 18)), (50, 50, 80))
    d = ImageDraw.Draw(sheet)
    for k, (group, rid, image, w, h) in enumerate(shown):
        t = Image.new("RGBA", (w, h), (50, 50, 80, 255))
        t.alpha_composite(Image.fromarray(np.ascontiguousarray(s.pixels(image)), "RGBA"))
        x, y = (k % cols) * (s.w * 2 + 8), (k // cols) * (s.h * 2 + 18)
        sheet.paste(t.resize((w * 2, h * 2), Image.NEAREST).convert("RGB"), (x, y + 12))
        d.text((x, y), "%d:%d" % (group, rid), fill=(255, 255, 0))
    sheet.save(sys.argv[2])
