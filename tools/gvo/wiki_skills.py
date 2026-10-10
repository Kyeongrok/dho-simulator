"""일본 위키(gvo.gamedb.info 의 Skill/List)의 스킬 일람 표에서 모험 스킬의 최고 랭크 · 선행 스킬을 뽑는다.

    python tools\\gvo\\wiki_skills.py            # 무엇이 뽑히는지 본다
    python tools\\gvo\\wiki_skills.py --write    # data\\extracted\\skill-facts-wiki.json 을 쓴다

받지는 않는다 — 사본 data\\extracted\\wiki\\gamedb_Skill_List.html 을 푼다(2026-10-11 에 한 번 받은 것).
표의 칸: スキル名 · 最高ランク · 習得条件(冒 · 商 · 海 · 他) · 消費行動力 · 効果 · 追加効果(スキル練成) · 優遇職 · 習得場所.
지금은 모험 스킬만(사용자, 2026-10-11: 「모험부터」). 습득 조건 자료가 게임에 없는 다섯(인양 · 예항 · 함정 · 언어학 · 바이올린 연주)은
선행 스킬을 넣지 않는다(사용자: 「현행대로 놔두면 되고」) — 최고 랭크만 넣는다.
소비 행동력과 습득 레벨은 skill-learn-gvdb.json 에 이미 있어서 여기서는 안 뽑는다.
"""
import html
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EXTRACTED = os.path.join(ROOT, 'data', 'extracted')
NAMES = {
    '補給': '보급', '操帆': '돛 조종', '測量': '측량', '釣り': '낚시', '酒宴': '주연', '駆除': '구제', '救助': '구조', '探索': '탐색', '視認': '인식',
    '観察': '관찰', '開錠': '자물쇠 따기', '採集': '채집', '調達': '조달', '行軍': '행군', '生存': '생존', '考古学': '고고학', '宗教学': '종교학',
    '財宝鑑定': '보물 감정', '美術': '미술', '地理学': '지리학', '生態調査': '생태 조사', '生物学': '생물학', '口説き': '대화술', '機雷発見': '기뢰발견',
    'バイオリン 演奏': '바이올린 연주', '投てき術': '던지기 기술', '言語学': '언어학', 'サルベージ': '인양', '曳航': '예항', '罠': '함정',
    '航行技術': '항해기술', '天文学': '천문학', '調教': '조교',
}
KEEP_LEARNING = {'인양', '예항', '함정', '언어학', '바이올린 연주'}


def main():
    page = open(os.path.join(EXTRACTED, 'wiki', 'gamedb_Skill_List.html'), 'rb').read().decode('euc-jp', errors='replace')
    start = page.rfind('冒険系スキル')       # 차림표에도 같은 말이 있다 — 맨 뒤의 것이 표의 제목이다
    part = page[start:page.rfind('交易系スキル')]
    ids = {s['Name']: s['Id'] for s in json.load(open(os.path.join(EXTRACTED, 'skills.json'), encoding='utf-8'))}
    facts = {}
    for row in re.findall(r'<tr[^>]*>(.*?)</tr>', part, re.S):
        cells = [re.sub(r'\s+', ' ', html.unescape(re.sub('<[^>]+>', '', re.sub(r'<br[^>]*>', ' ', c)))).strip() for c in re.findall(r'<t[hd][^>]*>(.*?)</t[hd]>', row, re.S)]
        if len(cells) < 8 or cells[0] not in NAMES or not cells[1].isdigit():
            continue
        name = NAMES[cells[0]]
        needs = []
        if name not in KEEP_LEARNING:
            for m in re.finditer(r'([^\d\s/]+?)\s*(\d+)', cells[5]):
                if m.group(1) in NAMES:
                    needs.append([ids[NAMES[m.group(1)]], int(m.group(2))])
        facts[ids[name]] = {"Name": name, "Max": int(cells[1]), "Needs": needs}
    for skill, fact in sorted(facts.items()):
        print(skill, fact["Name"], '최고', fact["Max"], '선행', fact["Needs"])
    print(len(facts), '가지')
    if '--write' in sys.argv:
        out = os.path.join(EXTRACTED, 'skill-facts-wiki.json')
        with open(out, 'w', encoding='utf-8', newline='\n') as f:
            json.dump({str(k): v for k, v in sorted(facts.items())}, f, ensure_ascii=False, indent=1)
        print(out)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
