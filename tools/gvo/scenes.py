"""장면 표(dt000000) 읽개: python scenes.py [도시 id …]

줄: u32 장면 id, u32 어미 장면 id, u32 번호, 이름(줄 id 로 XOR 한 글), 자원 이름\\0, 환경 이름\\0, 환경 이름\\0, 꼬리.
꼬리의 길이를 몰라도 되도록 자원 이름(ASCII)을 찾아 거꾸로 머리를 잡는다.
"""
import re
import struct
import sys

import gvo

_RES = re.compile(rb"[A-Za-z][A-Za-z0-9_]{5,}\x00")


def load():
    c = gvo.mwc_chunks(open(gvo.game_path(r"0000\local\dt000000.bin"), "rb").read())[gvo.LANG_KO]
    rows, last_end = [], 6
    for m in _RES.finditer(c):
        s = m.start()
        if s < last_end:
            continue
        for length in range(0, 400, 4):
            p = s - 2 - length
            if p - 12 < last_end - 0:
                break
            if struct.unpack_from("<H", c, p)[0] != length:
                continue
            sid, parent, number = struct.unpack_from("<3I", c, p - 12)
            if sid >> 24 not in (0x04, 0x08, 0x0C, 0x10, 0x11, 0x12, 0x13, 0x14, 0x1C, 0x30) and sid >> 28 != 1:
                continue
            try:
                name, o = gvo.packed_string(c, p, sid)
            except Exception:
                continue
            if o != s:
                continue
            e1 = c.index(b"\0", m.end()); e2 = c.index(b"\0", e1 + 1)
            rows.append(dict(at=p - 12, id=sid, parent=parent, number=number, name=name,
                             res=c[s:m.end() - 1].decode(), env=c[m.end():e1].decode("latin1"),
                             env2=c[e1 + 1:e2].decode("latin1"), tail_at=e2 + 1))
            last_end = e2 + 1
            break
    for a, b in zip(rows, rows[1:] + [dict(at=len(c))]):
        a["tail"] = c[a["tail_at"]:b["at"]]
    return rows


def show(r):
    t = r["tail"]
    nums = struct.unpack_from("<%dI" % (len(t) // 4), t) if len(t) >= 4 else ()
    return "%08X ^%08X #%-5d %-14s %-28s %-8s %s" % (r["id"], r["parent"], r["number"], r["name"], r["res"], r["env"],
                                                    " ".join("%X" % n for n in nums[:10]))


if __name__ == "__main__":
    rows = load()
    print(len(rows), "줄")
    for cid in map(int, sys.argv[1:]):
        mine = {r["id"] for r in rows if r["number"] == cid and r["id"] >> 24 in (0x08, 0x1C)}
        for r in rows:
            if r["id"] in mine or r["parent"] in mine:
                print(show(r))
        print()
