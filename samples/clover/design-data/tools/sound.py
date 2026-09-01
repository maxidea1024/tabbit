# -*- coding: utf-8 -*-
"""소리를 가져와 이름을 붙입니다.

**합성만으로는 카드와 칩이 되지 않습니다.** 오실레이터 하나와 노이즈로는 종이가 스치는
소리와 칩이 부딪히는 소리가 나지 않습니다 — 그 둘은 물리적으로 복잡해서, 녹음된 것을
가져오는 편이 손으로 다듬는 것보다 낫습니다.

    python samples/clover/design-data/tools/sound.py --fetch   # 받고 풉니다
    python samples/clover/design-data/tools/sound.py           # 목록만 확인합니다

받은 것은 `web/public/sound/<cue_id>.ogg` 로 놓입니다. **파일 이름이 곧 신호의 이름입니다** —
어느 파일이 어느 소리인지 다른 곳을 보지 않아도 됩니다.

가져온 것은 `web/public/sound/readme.md` 에 어디서 왔고 어느 라이선스인지 적습니다. 트럼프
52장의 얼굴과 같은 규칙입니다.
"""

import io
import os
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)
OUT = os.path.join(SAMPLE, 'web', 'public', 'sound')
CACHE = os.path.join(DESIGN, 'out', 'sound-packs')

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# 받아 오는 꾸러미. **둘 다 CC0 입니다** — 표기 의무가 없으므로 저장소에 그대로 담습니다.
PACKS = {
    'casino': 'https://kenney.nl/media/pages/assets/casino-audio/'
              '2472606a04-1721639069/kenney_casino-audio.zip',
    'interface': 'https://kenney.nl/media/pages/assets/interface-sounds/'
                 'fa43c1dd4d-1677589452/kenney_interface-sounds.zip',
}

# 신호 하나에 파일 하나.
#
# **물리적인 것은 카지노 꾸러미, 화면의 것은 인터페이스 꾸러미입니다.** 그리고 값에 따라
# 음이 오르는 신호는 칩과 카드를 두드리는 소리를 씁니다 — 그것을 재생 속도로 올리는 것이
# 원작의 그 소리입니다.
MAP = {
    # 득점. 음이 하나씩 오릅니다.
    'card_chip': ('casino', 'chip-lay-1'),
    'card_mult': ('casino', 'chips-collide-1'),
    'joker_add': ('casino', 'chip-lay-2'),
    'joker_mul': ('casino', 'chips-stack-3'),
    'joker_money': ('casino', 'chips-handle-2'),
    'joker_fizzle': ('interface', 'error_006'),
    'retrigger': ('casino', 'chip-lay-3'),
    'score_count': ('casino', 'chips-collide-2'),
    'score_settle': ('casino', 'chips-stack-5'),

    # 라운드의 끝.
    'blind_clear': ('interface', 'confirmation_002'),
    'blind_fail': ('interface', 'error_003'),
    'run_win': ('interface', 'confirmation_004'),
    'run_lose': ('interface', 'error_008'),
    'boss_reveal': ('interface', 'bong_001'),

    # 카드.
    # **뽑는 것과 고르는 것은 갈려야 합니다.** 둘 다 같은 계열의 미끄러지는 소리이면,
    # 패가 깔릴 때 여덟 번 나는 그 소리가 「누가 카드를 고르고 있다」로 들립니다 — 뽑는
    # 것은 판에 놓이는 소리이고, 고르는 것은 집어 드는 소리입니다.
    'card_draw': ('casino', 'card-place-4'),
    'card_select': ('casino', 'card-slide-5'),
    'card_place': ('casino', 'card-place-1'),
    'card_slam': ('casino', 'card-shove-1'),
    'card_flip': ('casino', 'card-slide-3'),
    'card_destroy': ('casino', 'card-shove-3'),

    # 돈과 칩.
    'coin_land': ('casino', 'chip-lay-2'),
    'coin_lose': ('casino', 'chips-collide-4'),

    # 상점.
    'shop_enter': ('interface', 'maximize_003'),
    'shop_buy': ('casino', 'chips-handle-3'),
    # 섞는 소리는 3초입니다 — 리롤 한 번에 3초는 다음 행동을 덮습니다. 부챗살로.
    'shop_reroll': ('casino', 'card-fan-1'),
    'joker_buy': ('casino', 'chips-handle-1'),
    'joker_sell': ('casino', 'chips-handle-5'),
    'joker_burn': ('interface', 'scratch_004'),
    'joker_move': ('casino', 'card-slide-7'),
    'consumable_use': ('interface', 'glass_002'),
    'pack_open': ('casino', 'cards-pack-open-1'),
    'pack_pick': ('casino', 'cards-pack-take-out-1'),
    'voucher_buy': ('interface', 'confirmation_001'),

    # 블라인드 고르기.
    'blind_select': ('interface', 'switch_003'),
    'blind_skip': ('interface', 'back_002'),

    # 화면.
    # `click_002` 는 0.01초입니다 — 그 길이로는 들리지 않습니다.
    'button': ('interface', 'click_001'),
    'panel_open': ('interface', 'open_002'),
    'panel_close': ('interface', 'close_002'),
}


def cues():
    """`SoundCue` 표의 신호들. **표가 기준입니다** — 여기 없는 것을 가져오지 않습니다."""
    path = os.path.join(DESIGN, 'data', 'SoundCue.tsv')
    rows = io.open(path, encoding='utf-8').read().split('\n')
    out = []
    for line in rows[4:]:
        cells = line.split('\t')
        if len(cells) > 1 and cells[1]:
            out.append(cells[1])
    return out


def fetch():
    """꾸러미를 받습니다. 이미 있으면 그대로 씁니다."""
    import urllib.request

    if not os.path.isdir(CACHE):
        os.makedirs(CACHE)
    for name, url in PACKS.items():
        target = os.path.join(CACHE, name + '.zip')
        if os.path.exists(target):
            print('있음 %s' % target)
            continue
        print('받는 중 %s' % name)
        urllib.request.urlretrieve(url, target)


def extract():
    """지도대로 뽑아 신호의 이름으로 놓습니다."""
    if not os.path.isdir(OUT):
        os.makedirs(OUT)

    opened = {}
    for name in PACKS:
        path = os.path.join(CACHE, name + '.zip')
        if not os.path.exists(path):
            print('꾸러미가 없습니다: %s — --fetch 를 먼저' % path)
            return 1
        opened[name] = zipfile.ZipFile(path)

    missing = []
    for cue, (pack, base) in sorted(MAP.items()):
        inside = 'Audio/%s.ogg' % base
        if inside not in opened[pack].namelist():
            missing.append('%s ← %s/%s' % (cue, pack, base))
            continue
        with io.open(os.path.join(OUT, cue + '.ogg'), 'wb') as handle:
            handle.write(opened[pack].read(inside))

    if missing:
        print('꾸러미에 없는 것 %d개:' % len(missing))
        for one in missing:
            print('  ' + one)
        return 1
    return 0


def main():
    known = cues()
    unknown = [cue for cue in MAP if cue not in known]
    empty = [cue for cue in known if cue not in MAP]

    print('신호 %d개 · 지도 %d개' % (len(known), len(MAP)))
    if unknown:
        print('표에 없는 신호를 가리킵니다: %s' % ' '.join(unknown))
    if empty:
        print('아직 소리가 없는 신호: %s' % ' '.join(empty))

    if '--fetch' in sys.argv:
        fetch()
        code = extract()
        if code:
            return code
        total = sum(os.path.getsize(os.path.join(OUT, f))
                    for f in os.listdir(OUT) if f.endswith('.ogg'))
        print('%s 에 %d개 · %.0fKB' % (OUT, len(MAP), total / 1024))
    return 0


if __name__ == '__main__':
    sys.exit(main())
