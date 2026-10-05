"""대항해시대 온라인(GV Online Kr) 클라이언트 데이터 읽개 — 공용 함수.

게임 폴더의 파일을 읽기만 한다. 풀어낸 자료는 저장소에 넣지 않는다.
"""
import os
import struct
import zlib

GAME_DIR = os.environ.get("GVO_DIR", r"C:\Program Files (x86)\Papaya Play\GV Online KR")

# 언어 묶음 차례 (0000\local\dt*.bin 은 MWC 덩이 5개 = 언어 5개)
LANG_JA, LANG_KO = 0, 1


def game_path(rel):
    return os.path.join(GAME_DIR, rel)


# ── MWC: zlib 덩이 ────────────────────────────────────────────────────────────
# "MWC\x1a", u32 풀린 크기, u32 눌린 크기, zlib 스트림

def mwc_chunks(data):
    """파일 안의 MWC 덩이를 차례로 풀어 bytes 목록으로 돌려준다."""
    res, i = [], 0
    while True:
        i = data.find(b"MWC\x1a", i)
        if i < 0:
            break
        usz, csz = struct.unpack_from("<II", data, i + 4)
        try:
            res.append(zlib.decompress(data[i + 12:i + 12 + csz]))
            i += 12 + csz
        except zlib.error:
            i += 4
    return res


def read_mwc(rel, index=0):
    with open(game_path(rel), "rb") as f:
        return mwc_chunks(f.read())[index]


# ── 문자열 표 (dt000002/3/5, dt00010x) ────────────────────────────────────────
# u32 키 길이, 키(저작권 문구, NUL 포함), u32 개수, u32 ?, (u32 id, u32 자리) × 개수,
# u32 덩이 크기, 덩이(키로 XOR).
# 레코드: u16 길이, u16 조각 수, (u16 갈래, u16 자리, u16 바이트 수) × 조각 수, 글자들.
# 글자는 UTF-16BE, 낱자마다 (id + 조각 안 차례) 를 XOR. 갈래 1 = 끼워 넣는 자리(%s).

def string_table(chunk):
    """{id: [(갈래, 글), ...]} 를 돌려준다."""
    n = struct.unpack_from("<I", chunk, 0)[0]
    key, body = chunk[4:4 + n], chunk[4 + n:]
    cnt = struct.unpack_from("<I", body, 0)[0]
    base = 8 + cnt * 8
    blob = bytes(c ^ key[i % len(key)] for i, c in enumerate(body[base + 4:]))
    out = {}
    for i in range(cnt):
        id_, off = struct.unpack_from("<II", body, 8 + i * 8)
        _, nseg = struct.unpack_from("<HH", blob, off)
        segs = []
        for s in range(nseg):
            kind, so, sl = struct.unpack_from("<HHH", blob, off + 4 + 6 * s)
            raw = blob[off + so:off + so + sl]
            chars = [(raw[p] << 8 | raw[p + 1]) ^ ((id_ + p // 2) & 0xFFFF)
                     for p in range(0, len(raw) - 1, 2)]
            segs.append((kind, "".join(map(chr, chars)).rstrip("\0")))
        out[id_] = segs
    return out


def flat(segs):
    return "".join(text for _, text in segs)


# ── 자료 표 안의 이름 글 (dt000000 / dt000001) ────────────────────────────────
# u16 바이트 수(4의 배수), 기호들. 기호 = v + v//7 (v 는 6비트) — 네 기호가 3바이트.
# 풀면 UTF-16LE 이고 4바이트마다 레코드 id(u32)가 XOR 돼 있다 —
# 짝수째 낱자에 id 아래 16비트, 홀수째 낱자에 위 16비트.

def unpack6(sym):
    out = bytearray()
    for i in range(0, len(sym) - 3, 4):
        v = 0
        for c in sym[i:i + 4]:
            v = (v << 6) | ((c - (c >> 3)) & 0x3F)
        out += v.to_bytes(3, "big")
    return bytes(out)


def packed_string(buf, off, rec_id):
    """(글, 다음 자리). off 는 u16 길이 자리."""
    n = struct.unpack_from("<H", buf, off)[0]
    raw = unpack6(buf[off + 2:off + 2 + n])
    chars = []
    for p in range(0, len(raw) - 1, 2):
        w = raw[p] | raw[p + 1] << 8
        w ^= (rec_id >> 16 if (p // 2) & 1 else rec_id) & 0xFFFF
        if w == 0:
            break
        chars.append(chr(w))
    return "".join(chars), off + 2 + n


# ── dt000001: 표 143개 묶음 ───────────────────────────────────────────────────
# u32 개수, (u32 자리, u32 크기) × 개수

def split_tables(chunk):
    n = struct.unpack_from("<I", chunk, 0)[0]
    return [chunk[o:o + s] for o, s in
            (struct.unpack_from("<II", chunk, 4 + i * 8) for i in range(n))]


def data_tables(lang=LANG_KO):
    return split_tables(read_mwc(r"0000\local\dt000001.bin", lang))
