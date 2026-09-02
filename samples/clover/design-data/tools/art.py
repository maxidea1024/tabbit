# -*- coding: utf-8 -*-
"""그림 프롬프트를 뽑습니다.

조커와 소모품과 태그와 보스의 그림을 생성하는데, **프롬프트를 하나하나 손으로 적지 않습니다** —
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

# **화풍을 한 자리에 고정합니다.** 202장이 같은 화풍이어야 한 게임의 그림으로 보입니다.
#
# 그림이 아니라 **문장(紋章)** 입니다. 2D 게임의 카드에 앉는 것이므로 사진처럼 그리면 화면의
# 나머지와 겉돌고, 작게 줄이면 무엇인지 읽히지도 않습니다 — 두꺼운 선과 납작한 색으로 크게
# 하나를 놓는 편이 어느 크기에서도 읽힙니다.
#
# 세로 2:3 입니다. **카드의 비율입니다** — 정사각형으로 뽑으면 카드에 넣을 때 위아래가
# 잘리거나 좌우에 빈 자리가 남고, 그러면 그림이 아니라 아이콘이 됩니다.
#
# 바탕색을 못박는 것이 요점입니다. 적지 않으면 어떤 것은 크림색으로 떠서 한 벌로 보이지
# 않습니다.
PROMPT = (
    'Bold flat 2D emblem of %s. '
    'Heraldic symbol, thick clean outlines, screen-print poster art, limited flat colour '
    'palette, no realism, no photography, no soft shading, no depth of field. '
    'Decorative geometric background pattern behind the symbol: art-deco rays and a fine '
    'dot grid with subtle paper grain, on a DEEP NAVY background. '
    'The symbol is HUGE and fills the whole vertical frame. '
    'Enclosed by a thin art-deco keyline border with small square corner ornaments on a '
    'narrow cream margin. '
    'Palette: deep navy, teal, warm gold, crimson. Vertical composition. '
    'No text, no letters, no numbers, no signature.')

# **태그는 카드가 아니라 칩입니다.** 조커와 소모품은 손에 드는 카드이고, 태그는 판에 놓는
# 포커 칩입니다 — 원작에서도 그렇습니다. 그래서 세로 카드 틀이 아니라 정사각 칩으로 뽑습니다.
#
# 문장에 「POKER CHIP」 이라고 적는 것이 요점입니다. 「뱃지」 나 「토큰」 으로 적으면 테두리
# 없는 납작한 문양에 그림자만 늘어뜨린 것이 나오고, 34픽셀로 줄이면 무엇인지 읽히지 않습니다.
TAG_PROMPT = (
    'Bold flat 2D icon of a round POKER CHIP with %s stamped in its centre. '
    'The chip is a circle with an inner ring and evenly spaced rectangular edge dashes '
    'around its rim, seen straight on, centred, filling most of the square frame. '
    'Thick clean outlines, screen-print poster art, limited flat colour palette, no realism, '
    'no photography, no gradients, no soft shading, no drop shadow. '
    'Flat DEEP NAVY background with a fine dot grid. '
    'Palette: deep navy, teal, warm gold, crimson. Square composition. '
    'No text, no letters, no numbers, no signature.')

# **보스는 인장입니다.** 태그가 판에 놓는 칩이라면 보스는 그 안테를 막고 선 것이고, 붉은
# 돌에 표시 하나를 새긴 것이 그것입니다 — 태그의 칩과 한눈에 갈려야 하므로 색과 테두리가
# 다릅니다.
BOSS_PROMPT = (
    'Bold flat 2D icon of a round dark stone SEAL with %s carved into its centre. '
    'The seal is a circle with a heavy notched rim and one large carved symbol at its '
    'centre, seen straight on, centred, filling most of the square frame. '
    'Thick clean outlines, screen-print poster art, limited flat colour palette, no realism, '
    'no photography, no gradients, no soft shading, no drop shadow. '
    'Flat DARK CRIMSON background with a fine dot grid. '
    'Palette: dark crimson, black, cold grey, warm gold. Square composition. '
    'No text, no letters, no numbers, no signature.')

# 그림의 크기. **카드는 카드의 비율이고 뱃지는 정사각입니다** — 그리고 이 비율을 내는
# 모델로 뽑아야 합니다. `flux-1-schnell` 은 가로세로를 무시하고 정사각형만 냅니다.
SIZE = (640, 960)
TAG_SIZE = (640, 640)
MODEL = 'lucid-origin'


# 갈래마다의 화풍. **여기 없는 갈래는 카드의 화풍으로 갑니다.**
STYLE = {'tag': TAG_PROMPT, 'boss': BOSS_PROMPT}
# 정사각으로 뽑는 갈래들. 칩과 인장이 그렇습니다.
SQUARE = ('tag', 'boss')


def prompt_for(kind, subject):
    """그 대상 하나의 프롬프트. **화풍은 갈래마다 한 벌씩 위에 있습니다.**"""
    lowered = subject[0].lower() + subject[1:]
    return STYLE.get(kind, PROMPT) % lowered


def size_for(kind):
    return TAG_SIZE if kind in SQUARE else SIZE

# 식별자를 그대로 읽으면 어색한 것들. **여기 없는 것은 식별자를 그대로 문장으로 만듭니다.**
# 그림 생성기가 낱말만 보고 거절하는 것들이 있습니다. **뜻은 그대로 두고 낱말만 바꿉니다** —
# 「a seed pod」·「a tiny tot」·「strength」 같은 것이 오탐으로 걸렀습니다.
OVERRIDE = {
    # **얻어걸린 실입니다.** 적지 않으면 모델이 네모난 소용돌이를 그리고, 그것은
    # 회전에 따라 다른 것으로 읽힙니다 — 게임에 넣을 수 없는 모양입니다.
    'tangle': 'a loose tangle of knotted string, irregular and organic',
    'smudge': 'a smudged ink thumbprint on paper',
    'almanac': 'an open almanac book showing moon phases',
    'seed_pod': 'a dried poppy capsule with a ring of vents',
    'tintype': 'an antique metal-plate portrait in an oval frame',
    'puffball': 'a round white mushroom releasing a cloud of spores',
    'night_thief': 'a masked burglar in a dark cloak carrying a lantern',
    'broad_bean': 'three round green legumes in a long green shell',
    'tiny_tot': 'a very small jester doll in a patchwork hood',
    'strength': 'a lion with a wreath, an arcana emblem of fortitude',
    'the_devil': 'a horned goat-headed idol, an arcana emblem',
    'cryptid': 'a shadowy horned beast glimpsed between trees',
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
    # **태그의 갈래말은 비어 있습니다.** 화풍의 문장이 이미 「둥근 뱃지」 라고 적고 있어서,
    # 여기에 또 적으면 「뱃지에 찍힌 뱃지」 가 됩니다.
    'tag': '',
}

# 태그 24종. **식별자를 그대로 읽으면 그릴 것이 없습니다** — `uncommon` 이나 `topup` 은
# 낱말이지 그림이 아니므로, 그 태그가 하는 일을 그림 하나로 옮겨 적습니다.
#
# 24개가 같은 둥근 뱃지이고 찍힌 문양만 다릅니다 — 34픽셀로 줄여도 「태그」 라는 것이 먼저
# 읽혀야 하고, 그러려면 테두리가 같아야 합니다.
TAG_SUBJECT = {
    'uncommon': 'a single bold star',
    'rare': 'three stars in a row',
    'negative': 'an inverted black diamond',
    'foil': 'a metallic chevron',
    'holographic': 'a prismatic rainbow band',
    'polychrome': 'three overlapping colour circles',
    'investment': 'a stack of coins',
    'voucher': 'a folded coupon ticket',
    'boss': 'a horned crown',
    'standard': 'a cluster of playing-card pips',
    'charm': 'a lucky horseshoe',
    'meteor': 'a falling meteor with a tail',
    'buffoon': 'a jester hat with bells',
    'ethereal': 'a ghostly wisp',
    'handy': 'an open palm',
    'garbage': 'a crumpled ball of paper',
    'coupon': 'a ticket cut by scissors',
    'd6': 'a six-sided die',
    'double': 'two mirrored arrows',
    'juggle': 'three juggling balls in an arc',
    'economy': 'a rising coin graph',
    'speed': 'a winged boot',
    'orbital': 'a ring orbiting a dot',
    'topup': 'an overflowing cup',
}


# 확장 350종의 구절은 파이일 하나에 따로 담았습니다 — 수가 많아 이 표에 섞으면
# 어느 것이 원작 대조본의 것인지 보이지 않습니다.
from art_expansion import EXPANSION  # noqa: E402

OVERRIDE.update(EXPANSION)


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
    for identifier, display in read_table('Tag.tsv', 'tag_id'):
        out.append(('tag', identifier, display,
                    FLAVOUR['tag'] + TAG_SUBJECT.get(identifier, phrase(identifier))))
    for identifier, display in read_table('BossBlind.tsv', 'boss_id'):
        out.append(('boss', identifier, display,
                    BOSS_SUBJECT.get(identifier, phrase(identifier))))
    return out


# 보스 28종의 대상. **표시 하나로 새길 수 있어야 합니다** — 「the psychic」 처럼 사람을
# 가리키는 낱말은 그대로 두면 초상화가 나오고, 인장에는 초상화가 들어가지 않습니다.
BOSS_SUBJECT = {
    'the_hook': 'a heavy curved iron hook',
    'the_club': 'a club suit symbol',
    'the_psychic': 'an open eye inside an open palm',
    'the_goad': 'a long pointed cattle prod',
    'the_window': 'a four-pane window frame',
    'the_manacle': 'an open iron shackle with a broken chain link',
    'the_pillar': 'a fluted stone column',
    'the_head': 'a featureless profile of a head',
    'the_house': 'a small steep-roofed house',
    'the_wall': 'a section of stacked brick wall',
    'the_wheel': 'a spoked cart wheel',
    'the_arm': 'a bent arm flexing',
    'the_fish': 'a fish seen from the side',
    'the_water': 'three stacked wave lines',
    'the_mouth': 'a closed mouth with sealed lips',
    'the_needle': 'a sewing needle with thread through its eye',
    'the_flint': 'a flint stone striking sparks',
    'the_mark': 'a target with crossed lines',
    'the_eye': 'a single wide-open eye',
    'the_tooth': 'a single pointed tooth',
    'the_plant': 'a sprouting seedling with two leaves',
    'the_serpent': 'a coiled snake',
    'the_ox': 'an ox skull with heavy horns',
    'amber_acorn': 'an oak nut sitting in its scaled cup',
    'verdant_leaf': 'a single broad leaf with veins',
    'violet_vessel': 'a wide-bellied urn with two handles',
    'crimson_heart': 'a heart suit symbol split by a jagged fracture line',
    'cerulean_bell': 'a hanging bell with its clapper',
}


def target(kind, identifier):
    # **트럼프만 png 입니다.** 나머지는 결이 있는 사각형 그림이라 webp 로 굽습니다 —
    # png 로 두면 장당 400KB 이고 202장이면 77MB 입니다.
    return os.path.join(SAMPLE, 'web', 'public', 'art', kind, identifier + '.webp')


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
                            prompt_for(kind, subject), have))

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
            print('%s/%s\t%s' % (kind, identifier, prompt_for(kind, subject)))


if __name__ == '__main__':
    main()
