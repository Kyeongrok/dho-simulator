"""dhoguide.kr 의 목록 쪽을 천천히 받는다 — 한 쪽씩, 사이를 띄우고, 200 이 아니면 그 자리에서 그만둔다.

    python tools\\gvo\\dhoguide_list.py equipment 172            # 장비품 목록 1 ~ 172쪽, 3분에 한 쪽
    python tools\\gvo\\dhoguide_list.py equipment 172 --gap 180

사본은 data\\extracted\\wiki\\dhoguide-list-<갈래>-<쪽>.html. 이미 있는 쪽은 다시 받지 않는다(같은 쪽은 한 번만).
진행은 data\\extracted\\wiki\\dhoguide-list-<갈래>.log 에 적는다. 사용자, 2026-10-11: 「일단 3분으로 해봐」.
"""
import os
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WIKI = os.path.join(ROOT, 'data', 'extracted', 'wiki')
AGENT = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126 Safari/537.36'


def main():
    kind, last = sys.argv[1], int(sys.argv[2])
    gap = int(sys.argv[sys.argv.index('--gap') + 1]) if '--gap' in sys.argv else 180
    log = os.path.join(WIKI, f'dhoguide-list-{kind}.log')

    def say(text):
        with open(log, 'a', encoding='utf-8') as f:
            f.write(time.strftime('%Y-%m-%d %H:%M:%S ') + text + '\n')

    fetched = False
    for page in range(1, last + 1):
        path = os.path.join(WIKI, f'dhoguide-list-{kind}-{page}.html')
        if os.path.exists(path) and os.path.getsize(path) > 20000:
            continue
        if fetched:
            time.sleep(gap)
        url = f'https://www.dhoguide.kr/dho/list.php?type={kind}&page={page}'
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': AGENT}), timeout=60) as reply:
                status, body = reply.status, reply.read()
        except urllib.error.HTTPError as error:
            say(f'{page}쪽 {error.code} — 그만둔다')
            return 1
        except Exception as error:
            say(f'{page}쪽 {error!r} — 그만둔다')
            return 1
        if status != 200 or len(body) < 20000 or f'/dho/{kind}/'.encode() not in body:
            say(f'{page}쪽 status {status} · {len(body)}바이트 · 목록이 아닌 것 같다 — 그만둔다')
            return 1
        with open(path, 'wb') as f:
            f.write(body)
        say(f'{page}쪽 받음 {len(body)}바이트')
        fetched = True
    say('끝')
    return 0


if __name__ == '__main__':
    sys.exit(main())
