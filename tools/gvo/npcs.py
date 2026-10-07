"""NPC 의 이름 표를 뽑는다 — data/extracted/npc-names.json.

    python npcs.py

표 41: NPC 배 이름(산타 에우라리아호 …), 72: 이름난 선장(하이레딘 …),
95 · 96: 꾸밈말과 이름씨(「난폭한」 + 「해적」 — 둘을 이어 NPC 의 이름을 짓는 것으로 보인다),
51: 육상전 테크닉(내려 베기 …). 줄의 꼬리 짜임은 풀지 않고 이름만 줍는다.
"""
import json
import os
import struct

import gvo


def names(table):
    """(id, 이름) — id 가 늘어나는 차례로 글을 풀 수 있는 자리만 줍는다."""
    out, at, last = [], 4, 0
    while at + 6 < len(table):
        row, size = struct.unpack_from("<IH", table, at)
        if last < row <= last + 3000 and 2 <= size <= 120 and at + 6 + size <= len(table):
            text, after = gvo.packed_string(table, at + 4, row)
            if text and all(0xAC00 <= ord(c) <= 0xD7A3 or c in " ·-()0123456789" or c.isascii() for c in text):
                out.append((row, text))
                last, at = row, after
                continue
        at += 1
    return out


def missions(table):
    """지방함대의 활동(표 113) — (id, 이름, 잘됐을 때의 글, 안됐을 때의 글). 줄의 나머지는 풀지 않았다."""
    found = []
    at, last = 4, 0
    while at + 6 < len(table):
        row, size = struct.unpack_from("<IH", table, at)
        if last < row <= last + 3000 and 2 <= size <= 120 and at + 6 + size <= len(table):
            text, after = gvo.packed_string(table, at + 4, row)
            if text and all(0xAC00 <= ord(c) <= 0xD7A3 or c in " ·-()0123456789" or c.isascii() for c in text):
                found.append((row, text, at, after))
                last, at = row, after
                continue
        at += 1
    out = []
    for k, (row, name, start, after) in enumerate(found):
        end = found[k + 1][2] if k + 1 < len(found) else len(table)
        texts, o = [], after
        while o + 2 < end and len(texts) < 2:
            size = struct.unpack_from("<H", table, o)[0]
            if size >= 40 and o + 2 + size <= end:
                text, nxt = gvo.packed_string(table, o, row)
                if sum(0xAC00 <= ord(c) <= 0xD7A3 for c in text) > len(text) * 0.3:
                    texts.append(text.replace("\n", " "))
                    o = nxt
                    continue
            o += 1
        if len(texts) == 2:
            out.append(dict(Id=row, Name=name, Success=texts[0], Fail=texts[1]))
    return out


def main():
    tables = gvo.data_tables(gvo.LANG_KO)
    pick = lambda n: [text for _, text in names(tables[n]) if not text.startswith("※")]
    result = {
        "ShipNames": pick(41),
        "Captains": pick(72),
        "Adjectives": pick(95),
        "Nouns": pick(96),
        "Techniques": pick(51),
    }
    path = os.path.join(os.path.dirname(__file__), "..", "..", "data", "extracted", "npc-names.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    jobs = missions(tables[113])
    with open(os.path.join(os.path.dirname(path), "fleet-missions.json"), "w", encoding="utf-8") as f:
        json.dump(jobs, f, ensure_ascii=False, indent=1)
    print("지방함대의 활동", len(jobs))
    print({k: len(v) for k, v in result.items()})
    for k, v in result.items():
        print(k, v[:8])


if __name__ == "__main__":
    main()
