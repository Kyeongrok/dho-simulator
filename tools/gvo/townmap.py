"""시내 지도와 시설 자리: python townmap.py <도시 id> [out.png]

지도 그림  0010\\0000\\tm000000.bin — u32 장 수(247), (u32 자리, u32 크기) × 장 수, 자리마다 MWC 덩이.
            풀면 180 × 140 쯤의 BGRA(높이는 바이트 수 ÷ 720).
시설 자리  0000\\bin\\00000008.bin (MWC) — u32 지도 수(247), 지도마다 u32 지도 id, u32 표식 수,
            표식 25바이트: u32 장소 번호(자료 표 40), u32 지도 x, u32 지도 y, u32 갈래(3·4), u32 장면 X, u32 장면 Z(÷ 10 꼴), u8 ?
지도 id 는 도시 id 이고(1 ~ 225), 그보다 큰 것(1001 ~, 3001 ~)은 상륙지·개척지 따위다. 장 차례는 00000008 의 차례와 같다.
"""
import struct
import sys

import numpy as np

import gvo
import tables

WIDTH = 180


def marks():
    """[(지도 id, [(장소, x, y, 갈래, X, Z, 끝 바이트)])] — 파일 차례대로."""
    c = gvo.mwc_chunks(open(gvo.game_path(r"0000\bin\00000008.bin"), "rb").read())[0]
    out, o = [], 4
    for _ in range(struct.unpack_from("<I", c, 0)[0]):
        map_id, n = struct.unpack_from("<II", c, o)
        o += 8
        out.append((map_id, [struct.unpack_from("<5IB", c, o + 4 + 25 * i - 4 + 0) if False else
                             struct.unpack_from("<IIIIIIB", c, o + 25 * i) for i in range(n)]))
        o += 25 * n
    assert o == len(c), (o, len(c))
    return out


def picture(index):
    raw = open(gvo.game_path(r"0010\0000\tm000000.bin"), "rb").read()
    at, size = struct.unpack_from("<II", raw, 4 + index * 8)
    c = gvo.mwc_chunks(raw[at:at + size])[0]
    return np.frombuffer(c, np.uint8).reshape(-1, WIDTH, 4)


def place_names():
    t = gvo.data_tables()[40]
    r = tables.Reader(t)
    out = {}
    for _ in range(r.u32()):
        i = r.u32()
        out[i] = r.text(i)
    return out


if __name__ == "__main__":
    all_marks = marks()
    names = place_names()
    print(len(all_marks), "장. 지도 id:", [m for m, _ in all_marks][:12], "…", [m for m, _ in all_marks][-6:])
    if len(sys.argv) > 1:
        want = int(sys.argv[1])
        index = [m for m, _ in all_marks].index(want)
        for place, x, y, kind, wx, wz, tail in all_marks[index][1]:
            print("%4d %-22s 지도 (%3d, %3d)  갈래 %d  장면 (%d, %d)  끝 %d" % (place, names.get(place, "?"), x, y, kind, wx, wz, tail))
        if len(sys.argv) > 2:
            from PIL import Image, ImageDraw
            p = picture(index)
            im = Image.fromarray(np.ascontiguousarray(p[:, :, [2, 1, 0]])).resize((WIDTH * 5, p.shape[0] * 5), Image.NEAREST)
            d = ImageDraw.Draw(im)
            for place, x, y, kind, wx, wz, tail in all_marks[index][1]:
                d.ellipse([x * 5 - 4, y * 5 - 4, x * 5 + 4, y * 5 + 4], outline=(255, 255, 0))
                d.text((x * 5 + 6, y * 5 - 6), str(place), fill=(255, 255, 255))
            im.save(sys.argv[2])
