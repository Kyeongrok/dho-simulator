"""dhoguide.kr 의 배 쪽(사용자가 가리킨 쪽만 받아 둔 사본)에서 배 상세를 뽑는다.

사본: data/extracted/wiki/dhoguide-ship-*.html (한 쪽에 배 하나)
결과: data/extracted/shipdetail-dhoguide.json — shipdetail-facts.json 과 같은 꼴(ShipDetailFact).
게임은 이것을 배 상세에 더한다(없는 배는 새로, 있는 배는 빈 칸만 채운다).

  python tools/gvo/dhoguide_ship.py          # 뽑은 것을 보이기만
  python tools/gvo/dhoguide_ship.py --write  # 파일로 쓴다
"""
import glob, html, json, re, sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def text_of(fragment):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", fragment))).strip()


def tables_by_title(page):
    """머리 글(h4.section-title) → 그 뒤 첫 표의 줄들(칸 글의 목록)."""
    found = {}
    for m in re.finditer(r'<h4 class="section-title"[^>]*>(.*?)</h4>', page, re.S):
        title = text_of(m.group(1))
        end = page.find('<h4 class="section-title"', m.end())
        part = page[m.end():end if end > 0 else len(page)]
        table = re.search(r"<table.*?</table>", part, re.S)
        if not table:
            continue
        rows = [[text_of(c) for c in re.findall(r"<t[hd][^>]*>(.*?)</t[hd]>", row, re.S)] for row in re.findall(r"<tr.*?</tr>", table.group(0), re.S)]
        found.setdefault(title, [r for r in rows if r])
    return found


def numbers(row):
    return [int(c.replace(",", "")) if re.fullmatch(r"[\d,]+", c) else 0 for c in row]


def parse(path):
    page = open(path, encoding="utf-8").read()
    plain = text_of(re.sub(r"(?s)<script.*?</script>|<style.*?</style>", "", page))
    name = re.search(r"닫기 × 선박 (.+?) 히스토리", plain)
    times = re.search(r"강화 횟수 \d+ \(기본 강화 : (\d+), 재강화 : (\d+)\)", plain)
    days = re.search(r"건조 일수 (\d+) 일", plain)
    tables = tables_by_title(page)
    caps = numbers(tables["강화 상한"][1]) if len(tables.get("강화 상한", [])) > 1 else []
    # 쪽의 차례: 보조돛 · 선수상 · 문장 · 특수장비 · 추가장갑 · 선측포 · 선수포 · 선미포 → 게임의 차례: 보조돛 · 특수장비 · 추가장갑 · 선측포 · 선수포 · 선미포
    slots = []
    if len(tables.get("선박 부품", [])) > 1:
        head, row = tables["선박 부품"][0], numbers(tables["선박 부품"][1])
        cell = dict(zip(head, row))
        slots = [cell.get(k, 0) for k in ("보조돛", "특수장비", "추가장갑", "선측포", "선수포", "선미포")]
    skills = []
    for row in tables.get("선박 스킬", [])[1:]:
        if row and row[0] not in ("", "-", "돛", "선박 스킬"):      # 머리 줄이 두 줄이다
            skills.append({"Name": row[0], "Parts": [c for c in row[1:] if c not in ("", "-")]})
    return {
        "No": 0, "Name": name.group(1) if name else "", "Times": int(times.group(1)) if times else 0, "Retimes": int(times.group(2)) if times else 0,
        "Days": int(days.group(1)) if days else 0, "Caps": caps, "Slots": slots, "Skills": skills, "Hull": "", "Special": [], "Borrowed": "",
    }


def fetch(name):
    """요청받은 배 한 척의 쪽을 받는다 — 이름으로 찾기 한 번 + 배 쪽 한 번, 그 사이 20초.

    막히지 않게(사용자, 2026-10-09): 게임에서 「위키값 요청」을 누른 배만, 한 번에 한 척,
    앞에 받은 dhoguide 사본과 12분 넘게 띄운다. 200 이 아니면 그 자리에서 그만둔다(다시 해 보지 않는다).
    """
    import os, time, urllib.parse, urllib.request
    copies = glob.glob("data/extracted/wiki/dhoguide-*.html")
    newest = max((os.path.getmtime(p) for p in copies), default=0)
    if time.time() - newest < 12 * 60:
        print(f"앞에 받은 지 {int((time.time() - newest) / 60)}분 — 12분이 지나야 받는다"); return
    agent = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}

    def get(url, out):
        with urllib.request.urlopen(urllib.request.Request(url, headers=agent), timeout=30) as r:
            if r.status != 200:
                print("받지 못함", r.status); return None
            body = r.read().decode("utf-8", errors="replace")
        open(out, "w", encoding="utf-8").write(body)
        return body

    safe = re.sub(r"[^\w가-힣]+", "_", name)
    found = get("https://www.dhoguide.kr/dho/list?type=ship&stx=" + urllib.parse.quote(name), f"data/extracted/wiki/dhoguide-search-{safe}.html")
    if found is None:
        return
    # 목록의 줄: <a href="/dho/ship/번호">이름</a> — 이름이 똑같은 줄만 고른다
    link = next((m.group(1) for m in re.finditer(r'href="/dho/ship/(\d+)"[^>]*>(.*?)</a>', found, re.S) if text_of(m.group(2)) == name), None)
    if link is None:
        print("찾기 결과에 그 이름의 배가 없다:", name); return
    time.sleep(20)
    if get(f"https://www.dhoguide.kr/dho/ship/{link}", f"data/extracted/wiki/dhoguide-ship-{link}.html") is not None:
        print("받음", name, link)


if "--fetch" in sys.argv:
    fetch(sys.argv[sys.argv.index("--fetch") + 1])

ships = [parse(p) for p in sorted(glob.glob("data/extracted/wiki/dhoguide-ship-*.html"))]
ships = [s for s in ships if s["Name"]]
for s in ships:
    print(s["Name"], "강화", s["Times"], "+", s["Retimes"], "건조", s["Days"], "일")
    print("  상한", s["Caps"], "칸", s["Slots"])
    for k in s["Skills"]:
        print("  ", k["Name"], "=", " + ".join(k["Parts"]))
if "--write" in sys.argv:
    json.dump(ships, open("data/extracted/shipdetail-dhoguide.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("씀", len(ships))
