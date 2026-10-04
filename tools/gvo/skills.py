"""스킬과 그 둘레의 표(dt000001)를 푼다. 칸 뜻은 분석 글 30~33 번.

    python skills.py                # 표마다 줄 수와 앞 몇 줄
    python skills.py json out.json  # 스킬·직업·지도·활동을 JSON 하나로
    python skills.py find 돛        # 이름·설명에 글이 든 스킬

표 6  스킬 1,313줄 : u32 id, 이름, 설명, u16 갈래, u16 작은 갈래, u16 x, u16 y, u16 쪽, u16 기본, u32 습득 비용, u16 직업
표 5  직업 95줄    : u32 id, 이름, 설명, u16 계열(0 모험 · 1 교역 · 2 전투)
표 17 지도 1,145줄 : u32 id(1900001~), 이름, 설명 — 설명 끝 줄에 「탐색，고고학 랭크 3」
표 64 활동 95줄    : u32 id, 이름, 설명
"""
import json
import re
import struct
import sys

import gvo

T_JOB, T_SKILL, T_MAP, T_ACTIVITY = 5, 6, 17, 64

# 표 6 의 갈래 칸(a). 이름은 표에 없어 줄의 내용을 보고 붙였다.
GROUPS = {0: "모험", 1: "교역", 2: "전투", 3: "언어", 4: "아이템·테크닉 효과", 5: "부관 스킬", 6: "선박 스킬",
          7: "특수 선박 스킬", 8: "직업 연구·전문 스킬", 9: "점치기", 10: "효과(가)", 11: "효과(나)", 12: "오리지널 선박 스킬"}


def skills(t):
    n = struct.unpack_from("<I", t, 0)[0]
    off, out = 4, []
    for _ in range(n):
        rid = struct.unpack_from("<I", t, off)[0]
        name, o = gvo.packed_string(t, off + 4, rid)
        desc, o = gvo.packed_string(t, o, rid)
        group, sub, x, y, page, basic, cost, job = struct.unpack_from("<6HIH", t, o)
        off = o + 18
        out.append(dict(id=rid, name=name, desc=desc, group=group, sub=sub,
                        x=None if x == 0xFFFF else x, y=None if y == 0xFFFF else y,
                        page=None if page == 0xFFFF else page, basic=basic, cost=cost, job=job))
    assert off == len(t), (off, len(t))
    return out


def jobs(t):
    n = struct.unpack_from("<I", t, 0)[0]
    off, out = 4, []
    for _ in range(n):
        rid = struct.unpack_from("<I", t, off)[0]
        name, o = gvo.packed_string(t, off + 4, rid)
        desc, o = gvo.packed_string(t, o, rid)
        out.append(dict(id=rid, name=name, desc=desc, line=struct.unpack_from("<H", t, o)[0]))
        off = o + 2
    assert off == len(t), (off, len(t))
    return out


def named_desc(t):
    """u32 id, 이름, 설명 뿐인 표 (17 지도, 64 활동)."""
    n = struct.unpack_from("<I", t, 0)[0]
    off, out = 4, []
    for _ in range(n):
        rid = struct.unpack_from("<I", t, off)[0]
        name, o = gvo.packed_string(t, off + 4, rid)
        desc, off = gvo.packed_string(t, o, rid)
        out.append(dict(id=rid, name=name, desc=desc))
    assert off == len(t), (off, len(t))
    return out


_RANK = re.compile(r"랭크\s*(\d+)")
_ALIAS = {"생태조사": "생태 조사", "보물감정": "보물 감정"}


def maps(t, skill_names):
    """지도 아이템 — 설명 끝 줄에서 필요한 스킬과 랭크를 뽑는다."""
    out = named_desc(t)
    for m in out:
        last = m["desc"].strip().split("\n")[-1]
        hit = _RANK.search(last)
        m["rank"] = int(hit.group(1)) if hit else None
        need = []
        if hit:
            for word in re.split(r"[，,、.\s]+", last[:hit.start()]):
                word = _ALIAS.get(word, word)
                if word in skill_names:
                    need.append(word)
            if "자물쇠 따기" in last:
                need.append("자물쇠 따기")
            if "생태 조사" in last and "생태 조사" not in need:
                need.append("생태 조사")
            if "보물 감정" in last and "보물 감정" not in need:
                need.append("보물 감정")
        m["skills"] = sorted(set(need))
    return out


if __name__ == "__main__":
    tabs = gvo.data_tables()
    rows = skills(tabs[T_SKILL])
    names = {r["name"] for r in rows if r["group"] <= 2}
    if len(sys.argv) > 2 and sys.argv[1] == "json":
        with open(sys.argv[2], "w", encoding="utf-8") as f:
            json.dump(dict(groups=GROUPS, skills=rows, jobs=jobs(tabs[T_JOB]),
                           maps=maps(tabs[T_MAP], names), activities=named_desc(tabs[T_ACTIVITY])),
                      f, ensure_ascii=False, indent=1)
    elif len(sys.argv) > 2 and sys.argv[1] == "find":
        for r in rows:
            if sys.argv[2] in r["name"] or sys.argv[2] in r["desc"]:
                print(r)
    else:
        print("스킬", len(rows), "직업", len(jobs(tabs[T_JOB])), "지도", len(maps(tabs[T_MAP], names)),
              "활동", len(named_desc(tabs[T_ACTIVITY])))
        for r in rows[:6]:
            print(r)
