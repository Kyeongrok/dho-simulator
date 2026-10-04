"""바다 통행 판정 지도 — 0000\\bin\\10000001.bin / 10000011.bin.

MWC 덩이 하나. u32 조각 수(2048 = 64열 x 32행), 조각마다 1060바이트:
  u32  갈래 (0/1/2)
  512  64x64 비트맵 A (한 줄 8바이트, 낮은 비트가 왼쪽)
  512  64x64 비트맵 B (A 를 조금 부풀린 것)
  32   16x16 비트맵 (거친 것)
조각 하나가 세계 좌표 256x256, 비트 하나가 4x4 이다. 1 = 뭍.
"""
import struct
import sys

import numpy as np

import gvo

TILES_X, TILES_Y, TILE = 64, 32, 64


def load(name="10000001"):
    """(갈래[32,64], A[2048,4096], B[2048,4096]) — 값 1 이 뭍."""
    c = gvo.read_mwc(rf"0000\bin\{name}.bin")
    n = struct.unpack_from("<I", c, 0)[0]
    a = np.frombuffer(c[4:], dtype=np.uint8).reshape(n, 1060)
    kinds = a[:, :4].copy().view("<u4").reshape(TILES_Y, TILES_X)

    def plane(off):
        bits = np.unpackbits(a[:, off:off + 512].reshape(n, TILE, 8), axis=2, bitorder="little")
        return (bits.reshape(TILES_Y, TILES_X, TILE, TILE)
                .transpose(0, 2, 1, 3).reshape(TILES_Y * TILE, TILES_X * TILE))

    return kinds, plane(4), plane(516)


if __name__ == "__main__":
    from PIL import Image
    out = sys.argv[1]
    for name in ("10000001", "10000011"):
        kinds, pa, pb = load(name)
        rgb = np.zeros(pa.shape + (3,), np.uint8)
        rgb[..., 2] = 90
        rgb[pb == 1] = (200, 170, 60)
        rgb[pa == 1] = (60, 140, 60)
        Image.fromarray(rgb).save(rf"{out}\seamask_{name}.png")
        print(name, pa.shape, "뭍 비율", pa.mean(), pb.mean())
