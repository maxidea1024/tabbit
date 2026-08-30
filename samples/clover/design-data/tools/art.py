# -*- coding: utf-8 -*-
"""그림 프롬프트를 뽑습니다.

조커 150종과 소모품 52종의 그림을 생성하는데, **프롬프트를 손으로 202번 적지 않습니다** —
식별자가 이미 영어의 구체적인 낱말이므로 그것을 문장으로 바꾸고, 어색한 것만 아래의 표에
적어 둡니다.

    python samples/clover/design-data/tools/art.py            # 목록을 뽑습니다
    python samples/clover/design-data/tools/art.py --missing  # 아직 없는 것만

`out/art-prompts.tsv` 가 나오고, 그것을 보고 생성기를 돌립니다. 프롬프트가 파일에 남는 것이
요점입니다 — 그림이 이상하면 무엇을 넣어서 그렇게 되었는지 볼 수 있어야 합니다.
"""

import csv
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# **화풍을 한 줄에 고정합니다.** 202장이 같은 화풍이어야 한 게임의 그림으로 보입니다.
STYLE = ('drawn as flat vector art with bold outlines and cel shading, filling the frame, '
         'on a plain solid dark navy background that bleeds to all four edges. '
         'Palette: dark navy, slate blue, warm gold, crimson, sage green. '
         'No frame, no border, no lettering.')

# 식별자를 그대로 읽으면 어색한 것들. **여기 없는 것은 식별자를 그대로 문장으로 만듭니다.**
OVERRIDE = {
    'half_note': 'a single half note carved from pale stone',
    'low_note': 'a low bass note drawn as a heavy dark stone disc',
    'even_note': 'two identical stone discs balanced on a beam',
    'odd_note': 'three uneven stone discs stacked off-centre',
    'blank_card': 'a blank playing card with a faint embossed edge',
    'empty_frame': 'an empty gilded picture frame hanging in the dark',
    'four_pane': 'a four-pane window with cold light behind it',
    'eight_bell': 'a small brass bell with eight notches on its rim',
    'the_fool': 'a jester in a patchwork hood, seen from the shoulders up',
    'the_reader': 'a hooded figure bent over an open book, seen from behind',
    'the_wanderer': 'a lone traveller with a walking staff on a long road',
    'the_sharper': 'a gloved hand palming a card under a table edge',
    'the_barker': 'a carnival barker shouting through a brass megaphone',
    'the_steward': 'a keyring with many old keys held in a gloved hand',
    'two_masks': 'two theatre masks, one laughing and one weeping, side by side',
    'loaded_dice': 'a pair of dice with weighted, off-centre pips',
    'standing_stone': 'a tall standing stone with carved grooves',
    'stepping_stone': 'flat stepping stones crossing dark water',
    'gilt_frame': 'an ornate gilded frame with nothing inside',
    'gilt_pot': 'a small gilded pot overflowing with coins',
    'gilt_coin': 'a single thick gold coin on edge',
    'old_ledger': 'a thick old ledger book with a ribbon marker',
    'old_almanac': 'a worn almanac open to a page of moon phases',
    'old_bench': 'a weathered wooden bench in the dark',
    'road_sign': 'a leaning wooden road sign with two blank arms',
    'long_road': 'an empty road running to a low horizon',
    'raw_gem': 'an uncut gemstone with rough facets',
    'card_sharper': 'a fanned hand of cards held close to the chest',
}

# 소모품 세 갈래는 갈래마다 화풍의 한마디를 더합니다.
FLAVOUR = {
    'tarot': 'an arcana emblem of ',
    'planet': 'the planet ',
    'spectral': 'a ghostly sigil of ',
}


def phrase(identifier):
    """식별자 하나를 영어 구절로. `quartz_bloom` 이 `a quartz bloom` 이 됩니다."""
    if identifier in OVERRIDE:
        return OVERRIDE[identifier]
    words = identifier.replace('_', ' ')
    if words.startswith('the '):
        return words
    first = words[0]
    article = 'an' if first in 'aeiou' else 'a'
    return '%s %s' % (article, words)


def read_table(name, key):
    path = os.path.join(DESIGN, 'data', name)
    rows = list(csv.reader(io.open(path, encoding='utf-8'), delimiter='\t'))
    head = rows[1]
    at = head.index(key)
    name_at = head.index('name') if 'name' in head else None
    return [(row[at], row[name_at] if name_at is not None else '') for row in rows[4:] if row]


def entries():
    out = []
    for identifier, display in read_table('Joker.tsv', 'joker_id'):
        out.append(('joker', identifier, display, phrase(identifier)))
    for identifier, display in read_table('Tarot.tsv', 'tarot_id'):
        out.append(('tarot', identifier, display, FLAVOUR['tarot'] + phrase(identifier)))
    for identifier, display in read_table('Planet.tsv', 'planet_id'):
        # 행성은 관사를 붙이지 않습니다 — 「a pluto」 가 아니라 「Pluto」 입니다.
        body = identifier.replace('_', ' ').title()
        out.append(('planet', identifier, display,
                    FLAVOUR['planet'] + body + ' with rings and moons'))
    for identifier, display in read_table('Spectral.tsv', 'spectral_id'):
        out.append(('spectral', identifier, display, FLAVOUR['spectral'] + phrase(identifier)))
    return out


def target(kind, identifier):
    return os.path.join(SAMPLE, 'web', 'public', 'art', kind, identifier + '.png')


def main():
    only_missing = '--missing' in sys.argv
    rows = entries()

    out_dir = os.path.join(DESIGN, 'out')
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    path = os.path.join(out_dir, 'art-prompts.tsv')
    with io.open(path, 'w', encoding='utf-8', newline='') as handle:
        handle.write('kind\tid\tname\tsubject\tprompt\thave\n')
        for kind, identifier, display, subject in rows:
            have = '1' if os.path.exists(target(kind, identifier)) else ''
            handle.write('%s\t%s\t%s\t%s\t%s\t%s\n'
                         % (kind, identifier, display, subject,
                            '%s, %s' % (subject[0].upper() + subject[1:], STYLE), have))

    # **화면이 읽는 목록.** 이것이 없으면 그림마다 없는 파일을 찾아 404를 냅니다.
    art_dir = os.path.join(SAMPLE, 'web', 'public', 'art')
    if not os.path.isdir(art_dir):
        os.makedirs(art_dir)
    have = ['%s/%s' % (kind, identifier) for kind, identifier, _d, _s in rows
            if os.path.exists(target(kind, identifier))]
    # 트럼프 52장은 **밖에서 온 것**이라 여기 목록에 없습니다. 폴더를 그대로 훑습니다 —
    # 어디서 왔고 어느 라이선스인지는 `web/public/art/card/readme.md` 에 있습니다.
    card_dir = os.path.join(art_dir, 'card')
    if os.path.isdir(card_dir):
        for entry in sorted(os.listdir(card_dir)):
            if entry.endswith('.png'):
                have.append('card/%s' % entry[:-4])
    index = os.path.join(art_dir, 'index.json')
    with io.open(index, 'w', encoding='utf-8', newline='') as handle:
        handle.write('[\n')
        handle.write(',\n'.join('  "%s"' % entry for entry in have))
        handle.write('\n]\n')

    missing = [r for r in rows if not os.path.exists(target(r[0], r[1]))]
    print('%s' % path)
    print('전부 %d개 · 그림이 있는 것 %d개 · 없는 것 %d개'
          % (len(rows), len(rows) - len(missing), len(missing)))

    if only_missing:
        for kind, identifier, _display, subject in missing:
            print('%s/%s\t%s, %s' % (kind, identifier,
                                     subject[0].upper() + subject[1:], STYLE))


if __name__ == '__main__':
    main()
