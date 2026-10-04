"""캐릭터 만들기에 쓰는 자료 — 나라·직업·작위·호칭·학교 과정·지방, 화면 글, 얼굴 그림.

    python character.py                 # 모두 JSON 으로 찍는다 (표준 출력)
    python character.py summary         # 줄 수와 앞 몇 줄
    python character.py faces out.png   # 얼굴 그림(0010\\0002\\sw) 한 장에 모아 보기

자료 표(dt000001)의 줄 짜임 — 글은 gvo.packed_string (u16 길이 + 기호, 줄 id 로 XOR):
  표 1  지방 32줄      u32 id, 이름
  표 2  권역 20줄      u8 id, 이름
  표 4  나라 11줄      u32 id, 이름, 소개(첫 줄이 "본거지：…")
  표 5  직업 95줄      u32 id, 이름, 설명, u16 계열(0 모험, 1 교역, 2 전투)
  표 7  작위 28줄      u32 id, 이름 셋
  표 35 호칭 91줄      u32 id(0부터), 이름, 설명
  표 93 학교 과정 28줄 u32 id(0부터), 이름, 설명
  표 10 도시 225줄     u32 id, 이름, u32 갈래(0 본거지), u32 나라, u8 지방(표 1)

화면 그림 묶음(0010\\0001, 0010\\0002) — 색인 xx000000.bin + 자료 xx000001.bin …:
  머리 24바이트   u32 id 수, u32 무리 수, u32 너비, u32 높이, u32 그림 수, u32 자료 파일 수
  id 줄 20바이트  u32 무리, u32 id, u32 그림 번호, u32 너비, u32 높이          × id 수
  그림 줄 28바이트 u32 번호, u32 너비, u32 높이, u32 파일(0부터), u32 자리, u32 크기, u32 1   × 그림 수
  자료: 그 자리에 MWC 덩이 하나, 풀면 너비×높이×4 바이트 BGRA.
"""
import json
import struct
import sys
import zlib

import gvo
import tables

T_REGION, T_AREA, T_NATION, T_JOB, T_TITLE, T_HONOR, T_COURSE, T_CITY = 1, 2, 4, 5, 7, 35, 93, 10

JOB_KINDS = {0: "모험", 1: "교역", 2: "전투"}

# 화면 글(dt000002) 가운데 캐릭터 만들기에 쓰는 것
UI_TEXT_IDS = {
    "prompt_new": 5005, "prompt_nation": 5103, "prompt_equipment": 5104, "prompt_name": 5105,
    "prompt_job": 5106, "prompt_look": 5107, "confirm": 5101, "confirm_done": 5102,
    "name_bad": 5108, "name_long": 5109, "name_taken": 5110, "failed": 5111, "done": 5112,
    "cancel": 5113, "registering": 5114, "zoom_face": 5115,
    "label_character": 301, "label_name": 302, "male": 303, "female": 304, "male_type": 307, "female_type": 308,
    "label_nation": 310, "label_start_skill": 351, "label_favored_skill": 352, "label_expert_skill": 355,
    "label_start_gear": 386, "label_face": 405, "label_skin": 406, "label_hair_color": 407, "label_hair_style": 408,
    "label_build": 22098,
    "job_adventure": 2008, "job_trade": 2009, "job_battle": 2010,
}


def _rows(table, id_size, fields):
    """fields: 't' 글, 'H' u16, 'I' u32, 'B' u8. 표 끝과 맞지 않으면 AssertionError."""
    r = tables.Reader(table)
    out = []
    for _ in range(r.u32()):
        rid = r.u32() if id_size == 4 else r.u8()
        row = [rid]
        for f in fields:
            row.append(r.text(rid) if f == "t" else r.u16() if f == "H" else r.u32() if f == "I" else r.u8())
        out.append(row)
    assert r.end, (r.o, len(table))
    return out


def load(lang=gvo.LANG_KO):
    tabs = gvo.data_tables(lang)
    cities = tables.cities(tabs[T_CITY])
    capitals = {c["nation"]: c for c in cities if c["kind"] == 0}

    nations = []
    for rid, name, intro in _rows(tabs[T_NATION], 4, "tt"):
        cap = capitals.get(rid)
        nations.append({"id": rid, "name": name, "intro": intro,
                        "capital": cap["id"] if cap else 0, "capital_name": cap["name"] if cap else "",
                        "cities": sum(1 for c in cities if c["nation"] == rid)})
    return {
        "nations": nations,
        "jobs": [{"id": i, "name": n, "desc": d, "kind": k} for i, n, d, k in _rows(tabs[T_JOB], 4, "ttH")],
        "titles": [{"id": i, "names": [a, b, c]} for i, a, b, c in _rows(tabs[T_TITLE], 4, "ttt")],
        "honors": [{"id": i, "name": n, "desc": d} for i, n, d in _rows(tabs[T_HONOR], 4, "tt")],
        "courses": [{"id": i, "name": n, "desc": d} for i, n, d in _rows(tabs[T_COURSE], 4, "tt")],
        "regions": [{"id": i, "name": n} for i, n in _rows(tabs[T_REGION], 4, "t")],
        "areas": [{"id": i, "name": n} for i, n in _rows(tabs[T_AREA], 1, "t")],
    }


def ui_texts(lang=gvo.LANG_KO):
    table = gvo.string_table(gvo.read_mwc(r"0000\local\dt000002.bin", lang))
    return {key: gvo.flat(table[i]) for key, i in UI_TEXT_IDS.items() if i in table}


# ── 화면 그림 묶음 ────────────────────────────────────────────────────────────

class ImageSet:
    """rel 은 색인 파일(…000000.bin). 예: r"0010\\0002\\sw000000.bin" (80×80 얼굴), sx(256×384 초상), 0010\\0001\\sd(128×128 발견물)."""

    def __init__(self, rel):
        self.rel = rel
        with open(gvo.game_path(rel), "rb") as f:
            b = f.read()
        n, self.groups, self.w, self.h, m, self.files = struct.unpack_from("<6I", b, 0)
        self.ids = {}                       # (무리, id) → 그림 번호
        for i in range(n):
            group, rid, image, _, _ = struct.unpack_from("<5I", b, 24 + 20 * i)
            self.ids[(group, rid)] = image
        base = 24 + 20 * n
        self.images = [struct.unpack_from("<7I", b, base + 28 * i) for i in range(m)]

    def pixels(self, image):
        """BGRA 바이트 (너비×높이×4)."""
        _, w, h, file_no, off, size, _ = self.images[image]
        with open(gvo.game_path(self.rel.replace("000000.bin", "%06d.bin" % (file_no + 1))), "rb") as f:
            f.seek(off)
            chunk = f.read(size)
        assert chunk[:4] == b"MWC\x1a"
        return w, h, zlib.decompress(chunk[12:12 + struct.unpack_from("<I", chunk, 8)[0]])


def face_sheet(out, rel=r"0010\0002\sw000000.bin", cols=12):
    import numpy as np
    from PIL import Image
    s = ImageSet(rel)
    rows = (len(s.images) + cols - 1) // cols
    sheet = np.zeros((rows * s.h, cols * s.w, 3), np.uint8)
    for k in range(len(s.images)):
        w, h, raw = s.pixels(k)
        im = np.frombuffer(raw, np.uint8).reshape(h, w, 4)
        a = im[..., 3:4] / 255.0
        y, x = divmod(k, cols)
        sheet[y * h:(y + 1) * h, x * w:(x + 1) * w] = (im[..., [2, 1, 0]] * a + 50 * (1 - a)).astype(np.uint8)
    Image.fromarray(sheet).save(out)
    return len(s.images)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    if len(sys.argv) > 2 and sys.argv[1] == "faces":
        print(face_sheet(sys.argv[2]), "장")
    elif len(sys.argv) > 1 and sys.argv[1] == "summary":
        data = load()
        for key, rows in data.items():
            print(key, len(rows))
            for row in rows[:4]:
                print("   ", {k: (v[:28] if isinstance(v, str) else v) for k, v in row.items()})
        print("ui", ui_texts())
    else:
        data = load()
        data["ui"] = ui_texts()
        json.dump(data, sys.stdout, ensure_ascii=False, indent=1)
