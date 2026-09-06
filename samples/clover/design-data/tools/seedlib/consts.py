# -*- coding: utf-8 -*-
"""Const.xlsx · Feel.xlsx · Text.xlsx — 상수셋 · 연출 수치 · 번역 대조본.

**연출 수치도 데이터입니다.** 웹과 유니티가 같은 문턱을 읽어야 같은 세기로 보입니다.
"""

from . import cards, consumables, jokers, phrases, progression, setup_, shop
from .grid import constants, table, write

RUN = [
    ('StartingMoney', 'int', 4, '런을 시작할 때의 금액'),
    ('HandsPerRound', 'int', 4, '라운드마다 낼 수 있는 핸드'),
    ('DiscardsPerRound', 'int', 3, '라운드마다 버릴 수 있는 횟수'),
    ('HandSize', 'int', 8, '패에 드는 카드'),
    ('JokerSlots', 'int', 5, '조커 슬롯'),
    ('ConsumableSlots', 'int', 2, '소모품 슬롯'),
    ('MaxPlayedCards', 'int', 5, '한 번에 낼 수 있는 카드'),
    ('WinAnte', 'int', 8, '이 안테를 넘기면 승리'),
    ('ShowdownEvery', 'int', 8, '최종 보스가 나오는 안테의 주기'),
    ('EndlessGrowthBp', 'int', 26000, '안테 9 이상의 요구 점수 증가율. 만분율. '
                                      '**원작의 값을 수집하지 못했으므로 우리 값입니다**'),
]

# 씬이 갈리는 자리마다 어떻게 지우는가.
#
# **방향이 뜻입니다.** 판으로 들어가는 것과 나오는 것은 같은 방법에 방향만 반대이고, 계정
# 화면은 옆으로 갑니다 — 들어가고 나오는 것과 옆으로 가는 것이 다른 일이기 때문입니다.
#
# 규격은 `doc/ui/transition.md` 입니다.
INK = '#05070d'
LIGHT = '#f4ecd8'
TRANSITION = [
    ('title_run', 'Push', 340, 70, 420, INK, True, '-', '판으로 들어갑니다'),
    ('run_title', 'Push', 300, 60, 360, INK, False, '-', '판에서 나옵니다'),
    ('run_lost', 'Burn', 520, 80, 340, INK, True, 'joker_burn',
     '진 판이 그 자리에서 타 없어집니다'),
    ('run_won', 'Fade', 300, 60, 380, LIGHT, True, 'voucher_buy',
     '이긴 판만 밝은 쪽으로 나갑니다'),
    ('run_restart', 'Blocks', 260, 60, 300, INK, True, '-',
     '접고 곧바로 펴는 것이므로 짧고 건조합니다'),
    ('login_title', 'Slide', 260, 50, 300, INK, True, '-', '로그인 화면에서 타이틀로'),
    ('title_login', 'Slide', 260, 50, 300, INK, False, '-', '타이틀에서 로그인 화면으로'),
    ('boot_first', 'Fade', 0, 0, 520, INK, True, '-',
     '로딩에서 첫 화면으로. **지울 앞 화면이 없으므로 되돌리기만 합니다**'),
    ('quiet', 'Fade', 120, 0, 120, INK, True, '-',
     '전환을 줄였을 때. **0이 아닙니다** — 갈아 끼우는 프레임은 어느 설정에서도 '
     '보이면 안 됩니다'),
]

SCORE = [
    ('MultScale', 'int', 10000, '배수의 단위. 10000이 ×1'),
    ('MultDefault', 'int', 10000, '곱 누적값의 시작값'),
    ('RoundDownToNegativeInfinity', 'bool', 'TRUE',
     '나눗셈의 내림 방향. **규격이므로 바꾸지 않습니다**'),
    ('MaxRetriggerDepth', 'int', 8, '재발동이 재발동을 부르는 깊이의 상한'),
]

ECONOMY = [
    ('InterestPer5', 'int', 1, '보유 $5마다 받는 이자'),
    ('InterestCap', 'int', 5, '이자의 상한'),
    ('SellDivisor', 'int', 2, '판매가는 구입가를 이것으로 나눈 뒤 내림'),
    ('SellMin', 'int', 1, '판매가의 하한'),
    ('LegendaryBaseCost', 'int', 10, '전설 조커는 가격이 없으므로 판매가의 기준이 이것입니다'),
    ('TarotCost', 'int', 3, '상점의 타로 가격'),
    ('PlanetCost', 'int', 3, '상점의 행성 가격'),
    ('SpectralCost', 'int', 4, '상점의 유령 가격'),
    ('PlayingCardCost', 'int', 1, '상점의 플레잉 카드 가격'),
    ('VoucherCost', 'int', 10, '바우처 값'),
    ('ShopCardSlots', 'int', 2, '상점의 카드 칸'),
    ('ShopPackSlots', 'int', 2, '상점의 팩 칸'),
    ('SoulChanceNum', 'int', 3, '`The Soul` 이 나올 확률의 분자'),
    ('SoulChanceDen', 'int', 1000, '`The Soul` 이 나올 확률의 분모'),
]

FEEL = [
    ('ScoreStepMs', 'int', 120, '득점 카드 하나의 연출 길이'),
    ('JokerStepMs', 'int', 140, '조커 하나의 연출 길이'),
    ('RetriggerStepMs', 'int', 90, '재발동의 연출 길이'),
    ('HandLabelMs', 'int', 180, '족보 표시의 길이'),
    ('MultiplyMs', 'int', 400, '칩과 배수가 점수로 합쳐지는 길이'),
    ('SettleMs', 'int', 300, '요구 점수 게이지가 채워지는 길이'),
    ('FastForwardScale', 'int', 4, '빠르게 넘기기의 배속'),
    ('ShakeMaxPx', 'int', 12, '화면 흔들림의 최대 진폭'),
    ('ShakeThresholdMult', 'int', 200000, '흔들림이 시작되는 배수. 만분율'),
    ('ShakeMaxMult', 'int', 3000000, '흔들림이 최대가 되는 배수. 만분율'),
    ('NumberScaleMaxBp', 'int', 16000, '숫자가 커지는 최대 배율. 만분율'),
    ('PitchMaxSemitones', 'int', 12, '소리 음높이가 올라가는 최대 반음'),
    ('ParticleMax', 'int', 30, '카드 뒤 파티클의 최대 개수'),
    ('ChromaticMaxPx', 'int', 2, '색수차의 최대 폭'),
    ('CardHoverLiftPx', 'int', 12, '카드에 마우스를 올렸을 때 떠오르는 높이'),
    ('CardHoverTiltDeg', 'int', 6, '그때의 기울기'),
    ('DrawStaggerMs', 'int', 35, '카드를 뽑을 때 장마다의 간격. 뒷면으로 자리에 붙는 간격입니다'),
    ('DrawLandMs', 'int', 200, '마지막 장이 뒷면으로 자리에 붙기까지'),
    ('FlipStaggerMs', 'int', 25, '다 붙은 뒤 왼쪽부터 뒤집는 장마다의 간격'),
    ('PlayStaggerMs', 'int', 90, '낸 카드가 판으로 올라갈 때 장마다의 간격'),
    ('PlayLandMs', 'int', 260, '마지막 장이 자리에 붙고 득점이 시작되기까지'),
    ('TagGainMs', 'int', 760, '건너뛰어 받은 태그 칩이 커져서 머리띠로 날아가 앉기까지'),
    ('TagUseMs', 'int', 300, '받자마자 쓰이는 태그가 발동하는 시간'),
]

RNG_STREAM = [
    ('Shuffle', '덱 섞기'),
    ('ShopSlot', '상점 카드 칸에 무엇이 오는가'),
    ('ShopRarity', '조커가 왔을 때의 희귀도'),
    ('ShopVoucher', '바우처 칸'),
    ('Pack', '팩의 내용'),
    ('JokerProc', '조커의 확률 발동'),
    ('CardProc', '`Glass` 파괴와 `Lucky` 발동'),
    ('Boss', '보스 추첨'),
    ('Tag', '태그 추첨'),
    ('Misprint', '`smudge` 의 난수 배수'),
]

# 에디션, 셰이더 이름, 세기(만분율), 흐르는 속도(만분율), 노이즈(만분율)
EDITION_VISUAL = [
    ('Base', 'none', 0, 0, 0),
    ('Foil', 'foil', 8000, 3000, 0),
    ('Holographic', 'holo', 10000, 2000, 3500),
    ('Polychrome', 'poly', 12000, 1500, 2000),
    ('Negative', 'negative', 10000, 800, 0),
]

# 식별자, 언제, 음높이가 값을 따라가는가
SOUND_CUE = [
    ('card_chip', '카드의 칩이 더해질 때', True),
    ('card_mult', '카드의 배수가 더해질 때', True),
    ('joker_add', '조커가 가산할 때', False),
    ('joker_mul', '조커가 곱할 때', True),
    ('joker_money', '조커가 돈을 줄 때', False),
    ('joker_fizzle', '조커의 확률이 빗나갔을 때', False),
    ('retrigger', '재발동할 때', False),
    ('score_count', '점수를 세는 동안', True),
    ('score_settle', '점수가 확정될 때', False),
    ('blind_clear', '요구 점수를 넘겼을 때', False),
    ('blind_fail', '넘기지 못했을 때', False),
    ('card_draw', '카드를 뽑을 때', False),
    ('card_select', '카드를 고를 때', False),
    ('card_destroy', '카드가 파괴될 때', False),
    ('shop_enter', '상점에 들어갈 때', False),
    ('shop_buy', '무언가를 살 때', False),
    ('shop_reroll', '리롤할 때', False),
    ('boss_reveal', '보스가 나타날 때', False),
    ('run_win', '승리할 때', False),
    ('run_lose', '패배할 때', False),
]

# 족보의 표시 이름. **포커의 이름이므로 영어는 그대로이고 한국어는 관용 표기입니다.**
HAND_NAMES = [
    ('HighCard', '하이 카드', 'High Card'),
    ('Pair', '페어', 'Pair'),
    ('TwoPair', '투 페어', 'Two Pair'),
    ('ThreeOfAKind', '트리플', 'Three of a Kind'),
    ('Straight', '스트레이트', 'Straight'),
    ('Flush', '플러시', 'Flush'),
    ('FullHouse', '풀 하우스', 'Full House'),
    ('FourOfAKind', '포 카드', 'Four of a Kind'),
    ('StraightFlush', '스트레이트 플러시', 'Straight Flush'),
    ('FiveOfAKind', '파이브 오브 어 카인드', 'Five of a Kind'),
    ('FlushHouse', '플러시 하우스', 'Flush House'),
    ('FlushFive', '플러시 파이브', 'Flush Five'),
]

ACHIEVEMENT = [
    # 식별자, 표시 이름, 조건
    ('first_blood', '첫 관문', '스몰 블라인드를 처음 격파합니다'),
    ('ante_four', '중반', '안테 4에 도달합니다'),
    ('ante_eight', '완주', '안테 8을 넘깁니다'),
    ('endless', '그 너머', '안테 12에 도달합니다'),
    ('ten_thousand', '만 점', '한 핸드로 10,000점을 냅니다'),
    ('million', '백만 점', '한 핸드로 1,000,000점을 냅니다'),
    ('royal', '로열', '로열 플러시를 냅니다'),
    ('five_of_a_kind', '다섯 장', '파이브 오브 어 카인드를 냅니다'),
    ('flush_five', '한 장으로 다섯', '플러시 파이브를 냅니다'),
    ('full_tray', '가득 찬 줄', '조커 슬롯을 전부 채웁니다'),
    ('legendary', '전설', '전설 조커를 얻습니다'),
    ('all_tarot', '대아르카나', '타로 22종을 모두 써 봅니다'),
    ('all_planet', '태양계', '행성 12종을 모두 써 봅니다'),
    ('no_discard', '군더더기 없이', '버리기를 한 번도 쓰지 않고 안테 하나를 넘깁니다'),
    ('broke', '빈손', '금액 $0으로 보스를 격파합니다'),
    ('rich', '부자', '$100을 모읍니다'),
    ('skipper', '건너뛰기', '한 런에서 블라인드 8개를 스킵합니다'),
    ('every_deck', '모든 덱', '덱 15종으로 각각 승리합니다'),
    ('gold_stake', '황금', '황금 스테이크로 승리합니다'),
    ('collector', '수집가', '조커 150종을 모두 발견합니다'),
]


def strings():
    """번역 대조본. 시트의 표시 이름이 한국어이고 여기 영어가 있습니다."""
    rows = []

    def add(prefix, ident, ko, en):
        rows.append(['%s.%s.name' % (prefix, ident), ko, en])

    for e in jokers.JOKERS:
        add('joker', e[0], e[1], e[2])
    for t in consumables.TAROT:
        add('tarot', t[0], t[1], t[2])
    for p in consumables.PLANET:
        add('planet', p[0], p[1], p[2])
    for s in consumables.SPECTRAL:
        add('spectral', s[0], s[1], s[2])
    for v in shop.VOUCHER:
        add('voucher', v[0], v[1], v[2])
    for t in setup_.TAG:
        add('tag', t[0], t[1], t[2])
    for d in setup_.DECK:
        add('deck', d[0], d[1], d[2])
    for b in progression.BOSS:
        add('boss', b[0], b[1], b[2])
    for b in progression.BLIND:
        add('blind', b[0].lower(), b[1], b[0] + ' Blind')
    for s in setup_.STAKE:
        add('stake', s[0].lower(), s[1], s[0] + ' Stake')
    for e in cards.ENHANCEMENTS:
        add('enhancement', e[0].lower(), e[1], e[0])
    for s in cards.SEALS:
        add('seal', s[0].lower(), s[1], s[0])
    for e in cards.EDITIONS:
        add('edition', e[0].lower(), e[1], e[0])
    for a in ACHIEVEMENT:
        add('achievement', a[0], a[1], a[0].replace('_', ' ').title())
    for kind, ko, en in HAND_NAMES:
        add('hand', kind, ko, en)

    # 효과를 문장으로 만드는 문구들. **조커 150종의 설명문을 손으로 적지 않습니다.**
    rows.extend(phrases.rows())
    return rows


def seed():
    write('Const_Run',
          constants('RunConst', '런 하나의 시작값과 상한입니다.', RUN))
    write('Const_Score',
          constants('ScoreConst',
                    '득점의 단위와 규격입니다. **두 구현이 같아야 하는 값들입니다.**', SCORE))
    write('Const_Economy',
          constants('EconomyConst', '금액에 관한 값입니다.', ECONOMY))

    write('RngStream', table(
        'RngStream(key=stream)',
        '난수 스트림입니다. 시드 하나에서 스트림마다 다른 상태를 파생합니다.',
        ['stream', 'note'], ['RngStreamKind', 'string'],
        ['스트림', '어디에 쓰는가'],
        [list(r) for r in RNG_STREAM]))

    write('Const_Feel',
          constants('FeelConst',
                    '연출의 길이와 문턱입니다. **두 런타임이 같은 값을 읽습니다.**', FEEL))

    write('Transition', table(
        'Transition(key=transition_id)',
        '씬이 갈릴 때 화면을 어떻게 지우는가입니다. **덮개를 그리는 것이 아니라 화면 자체를 '
        '처리합니다.**',
        ['transition_id', 'kind', 'out_ms', 'hold_ms', 'in_ms', 'ink', 'toward', 'cue', 'note'],
        ['string (regex="^[a-z][a-z0-9_]*$")', 'TransitionKind', 'int (min=0, max=2000)',
         'int (min=0, max=2000)', 'int (min=0, max=2000)',
         'string (regex="^#[0-9a-f]{6}$")', 'bool', 'string?', 'string'],
        ['갈리는 자리', '지우는 방법', '지우는 시간. 밀리초', '아무것도 보이지 않는 채로 머무는 시간',
         '되돌리는 시간', '다 지워진 자리에 남는 색', '다가오는가. 밀림과 옆으로가 이 값을 봅니다',
         '시작할 때 나는 소리. `SoundCue` 의 이름', '어느 자리인가'],
        [list(t) for t in TRANSITION]))

    write('EditionVisual', table(
        'EditionVisual(key=edition)',
        '에디션 셰이더의 파라미터입니다. 웹 GLSL과 유니티 HLSL이 같은 수식에 이 값을 넣습니다.',
        ['edition', 'shader', 'strength', 'flow_speed', 'noise'],
        ['EditionKind', 'string', 'int (min=0)', 'int (min=0)', 'int (min=0)'],
        ['에디션', '셰이더 이름', '세기. 만분율', '흐르는 속도. 만분율', '노이즈. 만분율'],
        [list(v) for v in EDITION_VISUAL]))

    write('SoundCue', table(
        'SoundCue(key=cue_id)',
        '소리 하나입니다. 음높이가 값을 따라가는 것과 그렇지 않은 것이 갈립니다.',
        ['cue_id', 'note', 'pitch_follows_value'],
        ['string (regex="^[a-z][a-z0-9_]*$")', 'string', 'bool'],
        ['식별자', '언제 나는가', '음높이가 값을 따라가는가'],
        [list(s) for s in SOUND_CUE]))

    write('StringTable', table(
        'StringTable(key=string_id)',
        '번역 대조본입니다. 시트의 표시 이름이 한국어이고 영어가 여기 있습니다.',
        ['string_id', 'ko', 'en'], ['string', 'string', 'string'],
        ['식별자', '한국어', '영어'],
        strings()))

    write('Achievement', table(
        'Achievement(key=achievement_id)',
        '도전과제입니다. **원작의 57종을 옮기지 않고 우리 목록을 만들었습니다.**',
        ['achievement_id', 'name', 'condition', 'sort_order'],
        ['string (regex="^[a-z][a-z0-9_]*$")', 'string (text=Achievement)', 'string',
         'int (min=1, max=20)'],
        ['식별자', '표시 이름', '조건', '표시 순서'],
        [[a[0], a[1], a[2], i + 1] for i, a in enumerate(ACHIEVEMENT)]))

    return 10
