"""dhoguide.kr 의 부관 목록(사용자가 가리킨 쪽: /dho/list?type=aide&page=N)에서 부관을 뽑는다.

사본: data/extracted/wiki/dhoguide-aide-list-N.html (한 쪽에 스무 명)
결과: data/extracted/aides-dhoguide.json — [{No, Doc, Name, Kind, Job, Sex, Nation, City, Rescue}]

  python tools/gvo/dhoguide_aides.py            # 받아 둔 사본에서 뽑아 보이기만
  python tools/gvo/dhoguide_aides.py --write    # 파일로 쓴다
  python tools/gvo/dhoguide_aides.py --fetch    # 아직 안 받은 목록 쪽을 차례로 받는다(쪽과 쪽 사이 1분)

막히지 않게: 배 자료 받기(dhoguide_ship.py --fill)와 같은 사이트라, 가장 새 사본에서 30초가 지난 뒤에 받는다
(배 받기는 가장 새 사본에서 1분을 기다리니 서로 30초는 떨어진다). 200 이 아니면 그 자리에서 끝낸다.
"""
import glob, html, json, os, re, sys, time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
COPY = "data/extracted/wiki/dhoguide-aide-list-%d.html"


def text_of(fragment):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", fragment))).strip()


def parse(path):
    page = open(path, encoding="utf-8").read()
    table = re.search(r"<table.*?</table>", page, re.S)
    out = []
    for row in re.findall(r"<tr.*?</tr>", table.group(0) if table else "", re.S):
        cells = re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)
        link = re.search(r'href="/dho/aide/(\d+)"', row)
        if len(cells) < 7 or not link:
            continue
        no, name, kind, job, sex, nation, city = [text_of(c) for c in cells[:7]]
        out.append({"No": int(no) if no.isdigit() else 0, "Doc": int(link.group(1)), "Name": name, "Kind": kind, "Job": job,
                    "Sex": sex, "Nation": nation, "City": city, "Rescue": text_of(cells[7]) if len(cells) > 7 else ""})
    return out


def pages(path):
    return sorted({int(p) for p in re.findall(r"type=aide&(?:amp;)?page=(\d+)", open(path, encoding="utf-8").read())})


def collect():
    seen, out = set(), []
    for path in sorted(glob.glob("data/extracted/wiki/dhoguide-aide-list-*.html")):
        for aide in parse(path):
            if aide["Doc"] not in seen:
                seen.add(aide["Doc"]); out.append(aide)
    return sorted(out, key=lambda a: a["No"])


def detail(path):
    """부관 한 명의 쪽에서 「스킬」 마디를 읽는다: 최대 필요 레벨 · 최대 필요 특성 · 스킬 표(분류 · 스킬명 · 모험 · 교역 · 전투 · 특성).

    스킬 표의 모험 · 교역 · 전투는 그 스킬을 익히는 데 드는 부관의 레벨, 특성은 드는 특성 값(없으면 「-」)이다.
    """
    page = open(path, encoding="utf-8").read()
    start, end = page.find("히스토리"), page.find("dho-comments-title")
    body = page[start:end if end > 0 else len(page)]
    out = {}
    for label, value in re.findall(r'<span class="item-label">(.*?)</span>\s*</div>\s*<div class="m-td">(.*?)</div>', body, re.S):
        key = {"최대 필요 레벨": "NeedLevel", "최대 필요 특성": "NeedTrait"}.get(text_of(label))
        if key:
            out[key] = text_of(value)
    skills = []
    table = re.search(r"<table.*?</table>", body, re.S)
    for row in re.findall(r"<tr.*?</tr>", table.group(0) if table else "", re.S):
        cells = [text_of(c) for c in re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)]
        if len(cells) < 6:
            continue
        level = lambda s: int(s) if s.isdigit() else 0
        skills.append({"Kind": cells[0], "Name": cells[1], "Adventure": level(cells[2]), "Trade": level(cells[3]),
                       "Battle": level(cells[4]), "Trait": "" if cells[5] == "-" else cells[5]})
    out["Skills"] = skills
    return out


def fetch():
    import urllib.request
    have = [p for p in glob.glob("data/extracted/wiki/dhoguide-aide-list-*.html")]
    if not have:
        print("먼저 받아 둔 목록 쪽이 하나는 있어야 쪽 번호를 안다"); return
    todo = [n for n in pages(have[0]) if not os.path.exists(COPY % n)]
    print("받을 쪽", todo, flush=True)
    for n in todo:
        while True:      # 가장 새 사본(배 쪽 포함)에서 30초가 지날 때까지
            newest = max(os.path.getmtime(p) for p in glob.glob("data/extracted/wiki/dhoguide-*.html"))
            wait = 30 - (time.time() - newest)
            if wait <= 0:
                break
            time.sleep(min(wait, 5))
        request = urllib.request.Request("https://www.dhoguide.kr/dho/list?type=aide&page=%d" % n, headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"})
        try:
            with urllib.request.urlopen(request, timeout=30) as r:
                if r.status != 200:
                    print("받지 못함", r.status, "— 끝", flush=True); return
                body = r.read().decode("utf-8", errors="replace")
        except Exception as e:
            print("오류", n, repr(e)[:120], "— 끝", flush=True); return
        open(COPY % n, "w", encoding="utf-8").write(body)
        print(time.strftime("%H:%M"), "받음", n, flush=True)
        time.sleep(60)
    print("다 받음", flush=True)


def fill():
    """부관 한 명 한 명의 쪽(/dho/aide/번호 — 스킬 · 능력치)을 차례로 받는다(사용자, 2026-10-09: 「1분에 한번」).

    사본: data/extracted/wiki/dhoguide-aide-번호.html. 한 명 받고 1분 쉬며, 가장 새 사본(배 쪽 포함)에서 30초가 지난 뒤에 받는다.
    200 이 아니거나 오류가 나면 그 자리에서 끝낸다(다시 해 보지 않는다). data/extracted/wiki/dhoguide-stop 파일이 생기면 멈춘다.
    """
    import urllib.request
    todo = [a for a in collect() if not os.path.exists("data/extracted/wiki/dhoguide-aide-%d.html" % a["Doc"])]
    print("받을 부관", len(todo), flush=True)
    for n, aide in enumerate(todo):
        while True:
            if os.path.exists("data/extracted/wiki/dhoguide-stop"):
                print("멈춤 파일 — 끝", flush=True); return
            newest = max(os.path.getmtime(p) for p in glob.glob("data/extracted/wiki/dhoguide-*.html"))
            wait = 30 - (time.time() - newest)
            if wait <= 0:
                break
            time.sleep(min(wait, 5))
        request = urllib.request.Request("https://www.dhoguide.kr/dho/aide/%d" % aide["Doc"], headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"})
        try:
            with urllib.request.urlopen(request, timeout=30) as r:
                if r.status != 200:
                    print("받지 못함", r.status, aide["Name"], "— 끝", flush=True); return
                body = r.read().decode("utf-8", errors="replace")
        except Exception as e:
            print("오류", aide["Name"], repr(e)[:120], "— 끝", flush=True); return
        open("data/extracted/wiki/dhoguide-aide-%d.html" % aide["Doc"], "w", encoding="utf-8").write(body)
        print(time.strftime("%H:%M"), "받음", n + 1, "/", len(todo), aide["Name"], flush=True)
        time.sleep(60)
    print("다 받음", flush=True)


if "--fetch" in sys.argv:
    fetch()
if "--fill" in sys.argv:
    fill()
    sys.exit()
aides = collect()
filled = 0
for a in aides:      # 낱낱 쪽을 받아 둔 부관에게는 스킬을 붙인다
    copy = "data/extracted/wiki/dhoguide-aide-%d.html" % a["Doc"]
    if os.path.exists(copy):
        a.update(detail(copy)); filled += 1
print("부관", len(aides), "· 스킬까지", filled)
for a in [a for a in aides if a.get("Skills")][:1]:
    print(" ", a["Name"], a.get("NeedLevel"), "/", a.get("NeedTrait"))
    for s in a["Skills"]:
        print("   ", s["Kind"], s["Name"], s["Adventure"], s["Trade"], s["Battle"], s["Trait"])
for a in aides[:6]:
    print(" ", a["No"], a["Name"], a["Kind"], a["Job"], a["Sex"], a["Nation"], a["City"], a["Rescue"])
if "--write" in sys.argv or "--fetch" in sys.argv:
    json.dump(aides, open("data/extracted/aides-dhoguide.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("씀", len(aides))
