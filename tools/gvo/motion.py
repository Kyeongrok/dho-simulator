"""움직임 자료 FCVD0022 (0001\\hm*.bin 의 항목): python motion.py <묶음> [항목 …]

파일은 16바이트 레코드의 줄이다(차례는 파일 처음부터 센다).
머리: "FCVD0022", u32 레코드 수, u16 ?(161), u16 곡선 수, u32 × 곡선 수 — 곡선 머리 레코드의 차례
곡선 머리(16바이트): u32 이 곡선의 레코드 수(머리 + 키), u8 3, u8 갈래, u16 마디, u32 키 수, u32 0
키(16바이트): u16 4, u16 프레임, f32 값, f32 기울기(들어옴), f32 기울기(나감) — 에르미트 곡선으로 짐작
갈래: 0x06 · 0x07 · 0x08 = 자리 x y z, 0x22 ~ 0x25 = 회전 사원수 x y z w. 키가 하나뿐인 곡선은 그 값으로 고정이고 프레임이 길이다.
"""
import struct
import sys

from pack import Pack


def curves(e):
    e = bytes(e)
    if e[:8] != b"FCVD0022":
        return None
    _records, _a, count = struct.unpack_from("<IHH", e, 8)
    out = []
    for at in (o * 16 for o in struct.unpack_from("<%dI" % count, e, 16)):
        if at + 16 > len(e):
            break
        _units, three, kind, node, nkeys, _zero = struct.unpack_from("<IBBHII", e, at)
        if three != 3 or at + 16 + nkeys * 16 > len(e):
            continue
        keys = [struct.unpack_from("<HHfff", e, at + 16 + 16 * j)[1:] for j in range(nkeys)]
        out.append(dict(kind=kind, node=node, keys=keys))
    return out


def length(cs):
    return max((k[0] for c in cs for k in c["keys"]), default=0)


if __name__ == "__main__":
    p = Pack(r"0001\%s.bin" % sys.argv[1])
    if len(sys.argv) > 2:
        for i in map(int, sys.argv[2:]):
            for c in curves(p.entry(i)):
                print("%02X node %2d keys %2d" % (c["kind"], c["node"], len(c["keys"])),
                      " ".join("%d:%.3f" % (k[0], k[1]) for k in c["keys"][:9]))
    else:
        for i in range(p.count):
            cs = curves(p.entry(i))
            if not cs:
                continue
            moving = sorted({c["node"] for c in cs if len(c["keys"]) > 1})
            # 뿌리(마디 1 = Hips)가 앞으로 얼마나 가는가
            travel = [c["keys"][-1][1] - c["keys"][0][1] for c in cs if c["node"] in (0, 1) and c["kind"] in (6, 7, 8) and len(c["keys"]) > 1]
            print(i, "len", length(cs), "curves", len(cs), "moving", len(moving), "root travel", ["%.0f" % t for t in travel])
