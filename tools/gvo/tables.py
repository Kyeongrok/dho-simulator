"""dt000001 의 자료 표 가운데 항구·바다·모험에 쓰는 것을 푼다.

    python tables.py            # 표마다 줄 수와 앞 몇 줄
    python tables.py 32 카르타고  # 표 32 에서 글이 든 줄 찾기
"""
import struct
import sys

import gvo

T_REGION, T_SEA, T_OCEAN, T_CITY, T_LANDING = 1, 8, 9, 10, 11
T_DISCOVERY_KIND, T_DISCOVERY = 31, 32


class Reader:
    def __init__(self, buf):
        self.b, self.o = buf, 0

    def u8(self):
        v = self.b[self.o]; self.o += 1; return v

    def u16(self):
        v = struct.unpack_from("<H", self.b, self.o)[0]; self.o += 2; return v

    def u32(self):
        v = struct.unpack_from("<I", self.b, self.o)[0]; self.o += 4; return v

    def text(self, rec_id):
        s, self.o = gvo.packed_string(self.b, self.o, rec_id)
        return s

    @property
    def end(self):
        return self.o >= len(self.b)


def cities(t):
    """id, 이름, 소속 갈래(0 본거지 …), 나라, 문화권"""
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32(); out.append(dict(id=i, name=r.text(i), kind=r.u32(), nation=r.u32(), culture=r.u8()))
    assert r.end, (r.o, len(t))
    return out


def named(t):
    """id, 이름 뿐인 표 (지방·문화권·발견물 갈래 …)"""
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32(); out.append(dict(id=i, name=r.text(i)))
    assert r.end, (r.o, len(t))
    return out


def seas(t):
    """해역: id, 이름, 큰 바다 id(표 9)"""
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32(); out.append(dict(id=i, name=r.text(i), ocean=r.u32()))
    assert r.end, (r.o, len(t))
    return out


def oceans(t):
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32(); out.append(dict(id=i, name=r.text(i)))
        if not r.end and r.b[r.o + 1:r.o + 4] != b"\0\0\0":
            out[-1]["a"] = r.u16()
    return out


def landings(t):
    """상륙지: id(1001~), 이름, 해역?, ?"""
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32(); out.append(dict(id=i, name=r.text(i), a=r.u8(), b=r.u8()))
    assert r.end, (r.o, len(t))
    return out


def discoveries(t):
    """발견물: id, 이름, 설명, 갈래(표 31), 별 수, 경험치, 명성, ?"""
    r = Reader(t); out = []
    for _ in range(r.u32()):
        i = r.u32()
        out.append(dict(id=i, name=r.text(i), desc=r.text(i), kind=r.u16(), stars=r.u16(),
                        exp=r.u32(), fame=r.u32(), a=r.u16()))
    assert r.end, (r.o, len(t))
    return out


PARSERS = {T_CITY: cities, T_SEA: seas, T_LANDING: landings, T_DISCOVERY: discoveries,
           T_DISCOVERY_KIND: named}

if __name__ == "__main__":
    tabs = gvo.data_tables()
    if len(sys.argv) > 2:
        for row in PARSERS[int(sys.argv[1])](tabs[int(sys.argv[1])]):
            if any(sys.argv[2] in str(v) for v in row.values()):
                print(row)
    else:
        for ti, fn in PARSERS.items():
            try:
                rows = fn(tabs[ti])
                print(ti, fn.__name__, len(rows))
                for row in rows[:6]:
                    print("   ", {k: (v[:30] if isinstance(v, str) else v) for k, v in row.items()})
            except Exception as e:
                print(ti, fn.__name__, "ERR", repr(e))
