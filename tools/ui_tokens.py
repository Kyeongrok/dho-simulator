"""화면 코드(src\\Dho\\Ui)에 쓰인 디자인 토큰을 뽑는다 — 색 · 글자 크기 · 단추 높이.

    python tools\\ui_tokens.py            # data\\ui-tokens.json 을 다시 쓴다

- 색: `new Color4(r, g, b, a)` 로 바로 적은 것과 `Canvas.이름`(이름 붙은 색)을 쓴 횟수.
- 글자 크기: `canvas.Text(글, x, y, w, h, 크기, …)` 의 여섯째 값이 수로 적힌 것.
- 단추: `canvas.Button(글, x, y, w, h, 켜짐, 크기)` 의 높이와 글자 크기가 수로 적힌 것.
셈으로 적힌 값(조건식 · 변수)은 세지 않는다. 게임의 모드 창 「토큰」 탭이 이 파일을 읽어 보인다.
"""
import collections
import glob
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NUMBER = re.compile(r'^\s*(\d+(?:\.\d+)?)f?\s*$')


def calls(source, name):
    """name( … ) 호출마다 맨 바깥 쉼표로 가른 인자들."""
    at = 0
    while True:
        at = source.find(name + '(', at)
        if at < 0:
            return
        i = at + len(name) + 1
        depth, start, args, quote = 1, i, [], None
        while i < len(source) and depth > 0:
            c = source[i]
            if quote:
                if c == '\\':
                    i += 1
                elif c == quote:
                    quote = None
            elif c in '"\'':
                quote = c
            elif c in '([{':
                depth += 1
            elif c in ')]}':
                depth -= 1
                if depth == 0:
                    args.append(source[start:i])
            elif c == ',' and depth == 1:
                args.append(source[start:i])
                start = i + 1
            i += 1
        yield args
        at = i


def number(text):
    m = NUMBER.match(text)
    return float(m.group(1)) if m else None


def main():
    colours, named, sizes, heights, button_sizes = (collections.Counter() for _ in range(5))
    names = {}
    files = sorted(glob.glob(os.path.join(ROOT, 'src', 'Dho', 'Ui', '*.cs')))
    for path in files:
        source = open(path, encoding='utf-8').read()
        if path.endswith('Canvas.cs'):
            for m in re.finditer(r'static readonly Color4 (\w+) = new\(([^)]*)\)', source):
                names[m.group(1)] = [float(v.strip().rstrip('f')) for v in m.group(2).split(',')]
        for args in calls(source, 'new Color4'):
            values = [number(a) for a in args]
            if len(values) == 4 and all(v is not None for v in values):
                colours[tuple(round(v, 3) for v in values)] += 1
        for m in re.finditer(r'\bCanvas\.(\w+)\b', source):
            if m.group(1) in ('PanelFill', 'PanelEdge', 'White', 'Dim', 'Gold'):
                named[m.group(1)] += 1
        for args in calls(source, 'canvas.Text'):
            if len(args) >= 6 and number(args[5]) is not None:
                sizes[number(args[5])] += 1
        for name in ('canvas.Button', 'canvas.CloseButton'):
            for args in calls(source, name):
                if len(args) >= 5 and number(args[4]) is not None:
                    heights[number(args[4])] += 1
                if len(args) >= 7 and number(args[6]) is not None:
                    button_sizes[number(args[6])] += 1

    def rows(counter):
        return [{"Value": value, "Count": count} for value, count in sorted(counter.items(), key=lambda kv: (-kv[1], kv[0]))]

    book = {
        "Note": "화면 코드에서 뽑은 디자인 토큰 — tools\\ui_tokens.py 가 다시 쓴다. 손으로 고치지 않는다.",
        "Files": [os.path.basename(f) for f in files],
        "Named": [{"Name": name, "Rgba": names.get(name, []), "Count": named.get(name, 0)} for name in sorted(names, key=lambda n: -named.get(n, 0))],
        "Colours": [{"Rgba": list(rgba), "Count": count} for rgba, count in sorted(colours.items(), key=lambda kv: (-kv[1], kv[0]))],
        "TextSizes": rows(sizes),
        "ButtonHeights": rows(heights),
        "ButtonSizes": rows(button_sizes),
    }
    out = os.path.join(ROOT, 'data', 'ui-tokens.json')
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(book, f, ensure_ascii=False, indent=1)
    print(f"{out}: 이름 붙은 색 {len(book['Named'])} · 색 {len(book['Colours'])} · 글자 크기 {len(book['TextSizes'])} · 단추 높이 {len(book['ButtonHeights'])} · 단추 글자 {len(book['ButtonSizes'])}")


if __name__ == '__main__':
    main()
