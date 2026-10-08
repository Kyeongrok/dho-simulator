"""gvdb 의뢰 표의 보상 칸(열째 칸)에서 받는 아이템 · 교역품을 읽는다 — gvdb_seaquests.py · gvdb_tradequests.py 가 같이 쓴다.

꼴: 「「名工の大工道具」「石油(12)」「金14528D」「アベンチュリン(8)」獲得」 · 「依頼斡旋書×4」 · 「仕入発注書(カテゴリー2)23枚」
「Drop：…」(싸움에서 떨어지는 것) · 「執事「…」獲得」(집사) · 레시피는 보상 아이템이 아니라 뺀다.
→ [[갈래(0 아이템 · 1 교역품), 번호, 수]…]
"""
import os
import re
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(__file__))
import gvo
import gvdb_recipes

_tables = None


def _names():
    global _tables
    if _tables is None:
        nf = lambda s: unicodedata.normalize("NFKC", gvdb_recipes.squeeze(s))
        items = {nf(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 14, 0).items() if n and len(n) >= 2}
        goods = {nf(n): i for i, n in gvdb_recipes.names(gvo.LANG_JA, 19, 2).items() if n and len(n) >= 2}
        both = [(n, 0, i) for n, i in items.items()] + [(n, 1, i) for n, i in goods.items()]
        _tables = sorted(both, key=lambda t: -len(t[0]))
    return _tables


def gifts(note):
    text = unicodedata.normalize("NFKC", gvdb_recipes.squeeze(note))
    if not text or "Drop" in text or "執事" in text or "レシピ" in text:
        return [], bool(text) and "Drop" not in text and "執事" not in text and "レシピ" not in text
    out, used = [], [False] * len(text)
    for name, kind, number in _names():
        at = text.find(name)
        while at >= 0:
            if not any(used[at:at + len(name)]):
                for k in range(at, at + len(name)):
                    used[k] = True
                m = re.match(r"[」]?(?:[×x*(]|\()?(\d{1,3})(?:枚|個|樽|\))?", text[at + len(name):])
                out.append([kind, number, int(m.group(1)) if m and 0 < int(m.group(1)) <= 200 else 1])
            at = text.find(name, at + len(name))
    return out, not out


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raw = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "data", "extracted", "gvdb", "quests.csv"), "rb").read().decode("cp932", "replace")
    rows = [r.split("\t") for r in re.split(r"\r?\n(?=\d+\t)", raw)][1:]
    for kind in ("海事クエスト", "交易クエスト"):
        notes = [r[9] for r in rows if len(r) > 10 and r[4] == kind and r[9].strip()]
        got = [gifts(n) for n in notes]
        print(kind, len(notes), "적힌 것 · 읽은 것", sum(1 for g, _ in got if g), "· 못 읽은 것", sum(1 for g, lost in got if lost), [n[:30] for n, (g, lost) in zip(notes, got) if lost][:12])
        print("  보기", [(n[:36], g) for n, (g, _) in zip(notes, got) if g][:5])
