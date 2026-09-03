// 화면.
//
// **코어를 부르고 이벤트를 받아 그립니다.** 규칙은 여기 없습니다 — 어디에 놓을지와 얼마나
// 세게 보일지뿐이고, 뒤쪽의 수치는 `Const_Feel` 이므로 데이터입니다.
//
// 배치는 왼쪽에 판돈과 점수, 위에 조커와 소모품, 가운데에 낸 카드, 아래에 패입니다.
// 시선이 왼쪽에서 오른쪽으로 한 번 흐르게 두었습니다.

import {
  BlurFilter, Container, Graphics, Rectangle, Sprite, Text, Texture,
  type FederatedPointerEvent,
  type Application,
} from 'pixi.js'

import { JokerPool } from '../generated/enums/joker-pool'
import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { PackKind } from '../generated/enums/pack-kind'
import { PackSize } from '../generated/enums/pack-size'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { RankKind } from '../generated/enums/rank-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import type { Data } from '../core/data'
import { describe } from '../core/describe'
import { evaluate } from '../core/hand'
import {
  apply, defaultRules, newRun, rewardOf, tagFor, targetOf, type Action,
} from '../core/run'
import { language, nameOf, setLanguage, t, text, tf } from '../core/strings'
import { stakeRow, stakeSlug } from '../core/stake'
import { SetupPanel, setupLabel, validSetup, type RunSetup } from '../ui/setup'
import { useFont } from '../ui/font'
import { rerollCost, sellValueOf, type ShopItem } from '../core/shop'
import { bestHand, valueOf } from '../core/suggest'
import { newCounters, type CardInstance, type GameEvent, type RunState } from '../core/state'
import { BackgroundFilter } from '../shader/background'
import { PunchFilter } from '../shader/punch'
import { FlameFilter } from '../shader/flame'
import { ArriveFilter } from '../shader/arrive'
import { DissolveFilter } from '../shader/dissolve'
import { Audio } from './audio'
import { CardView, type EditionLook } from './card-view'
import { BlindBadge, Slot } from './hud'
import { JokerView } from './joker-view'
import {
  buildTimeline, particlesOf, readFeel, scaleOf, semitonesOf, shakeOf, TimelinePlayer,
  type Beat, type Feel,
} from './juice'
import { Coins } from './coins'
import { Motion, Spring } from './motion'
import { Particles } from './particles'
import { artFor, onArtReady, type ArtKind } from './art'
import { backLookOf, cardBack, drawCardBack, setCardBack } from './card-back'
import { cardArtDir, cardBackMotif, cardPaper, drawsIndex, setCardSet, setLookOf, suitInk } from './card-set'
import { drawGlyph, glyphFor, hashOf, hsl, shade } from './glyph'
import { cardArtId, drawFace } from './pips'
import { mix } from './skin'
import { COLOR, SIZE } from './theme'
import { box, type Box, CENTER, pointOf, putText, splitX } from '../ui/layout'
import { Button, Panel } from '../ui/widgets'
import { poolsOf, type PoolChoice } from '../core/pool'
import { Guide } from '../ui/guide'
import { JokerPoolPanel } from '../ui/joker-pool'
import {
  ChallengePanel, loadProgress, saveProgress, type ChallengeProgress,
} from '../ui/challenge'
import { randomSeed, Title } from '../ui/title'
import { LeaderboardHub, type EndLine } from '../ui/hub'
import { NetStatus } from '../ui/net-status'
import { LoginScene } from '../ui/login-scene'
import { ConfirmPanel } from '../ui/confirm'
import * as account from '../net/session'
import { busy as netBusy } from '../net/session'
import { newMetrics, observe, type MetricsAcc } from '../core/metrics'

import type { Scene } from './scene'
import { FOOTER_BAR, panelFrame, TITLE_BAR } from '../ui/modal'
import { Modals, type ModalPanel } from '../ui/modal'
import { richBlock, richLine } from '../ui/rich'
import {
  chosen, loadOptions, OptionsPanel, saveOptions, type Options,
} from '../ui/options'
import { Toasts } from '../ui/toast'
import { Tooltip } from '../ui/tooltip'

/**
 * 순위 글 안에서 숫자가 있는 자리.
 *
 * **글이 6개 언어이므로 자리를 수로 찾습니다.** 「등정 #412 ↑12」 에서 첫 `#숫자` 하나가
 * 굴러 내려가는 값이고, 그 앞뒤의 말은 언어마다 다릅니다.
 */
const RANK_MARK = /#\d+/

// 화면의 자리. **원작의 배치를 따릅니다** — 왼쪽에 판돈과 점수가 세로로 쌓이고, 위에 조커와
// 소모품이 나란히 서고, 가운데에서 카드를 내고, 아래에 패가 부챗살로 펴집니다.
const LEFT = 16
const PANEL_W = 264
/**
 * 왼쪽 판의 오른쪽 칸이 시작하는 자리.
 *
 * **두 칸이 판을 꽉 채웁니다.** 칸 하나가 124이고 사이가 16이면 둘이 264 — 판의 너비
 * 그대로입니다. 예전에는 오른쪽 칸을 `LEFT + 134` 에 두었고, 그러면 줄이 6픽셀 일찍
 * 끝나서 **오른쪽에만 여백이 남았습니다.**
 */
const RIGHT_COL = LEFT + 140
/** 판이 놓이는 자리의 가운데. 왼쪽 패널을 뺀 나머지의 한가운데입니다. */
const BOARD_X = (LEFT + PANEL_W + 20 + SIZE.width) / 2
/**
 * 판 위에 뜨는 것들의 가운데. **화면의 한가운데입니다.**
 *
 * 판의 한가운데에 두면 왼쪽 패널만큼 오른쪽으로 밀린 자리에 서고, 같은 화면에 뜨는
 * 정산·옵션·게임 방법은 화면 한가운데에 서므로 뜰 때마다 자리가 달라집니다.
 */
const POPUP_X = SIZE.width / 2

/** 상점의 것들이 하나씩 서는 간격. */
const REVEAL_STEP = 0.13
/** 하나가 다 서는 데 걸리는 시간. */
const REVEAL_SPAN = 0.28
/** 설 때 아래에서 올라오는 거리. */
const REVEAL_RISE = 14
/** 정산의 줄이 하나씩 서는 간격. */
const PAYOUT_STEP = 0.16
/**
 * 판이 열리고 첫 줄이 서기까지.
 *
 * **그 사이에 「정산 중」 이 뜹니다.** 0.12초는 그 글을 읽을 수 없고, 그보다 길면 기다리는
 * 것이 됩니다.
 */
const PAYOUT_WAIT = 0.52
/**
 * 상점이 서는 가장 높은 자리.
 *
 * **조커와 소모품 줄의 아래입니다** — 그 줄과 칸 수를 적은 글이 `y` 193 에서 끝납니다.
 */
const SHOP_TOP = 200

/** 뜯은 팩의 이름이 서는 자리. */
const PACK_TITLE_Y = 236
/** 펼친 카드의 가운데 높이. */
const PACK_CARDS_Y = 420
/**
 * 펼친 카드의 크기.
 *
 * **줄에 선 것보다 큽니다.** 지금 하는 일이 그 카드 하나를 고르는 것이므로, 다른 것과
 * 같은 크기로 두면 어느 것을 보아야 하는지가 정해지지 않습니다.
 */
/**
 * 머리띠와 첫 줄 사이.
 *
 * **6픽셀은 붙어 있는 것입니다.** 머리띠가 46픽셀이고 그 밑에 곧바로 단추가 서면 제목과
 * 단추가 한 덩어리로 보여서, 머리띠가 판의 머리가 아니라 첫 줄의 배경이 됩니다.
 */
const MENU_PAD = 18

const PACK_SCALE = 1.55
const PACK_CARD_W = SIZE.jokerWidth * PACK_SCALE
const PACK_CARD_H = SIZE.jokerHeight * PACK_SCALE

/** 산 것이 제자리에 닿기까지. 용수철이 그만큼에 잦아듭니다. */
/**
 * 산 것이 자리에 닿는 데까지.
 *
 * **오는 길이 보여야 합니다.** 0.3초는 사는 순간과 닿는 순간이 겹쳐 보이고, 그러면
 * 「울렁이다 · 오다 · 닿다」 셋 중 가운데가 없어집니다. 용수철의 빠르기(`Motion.drift`)와
 * 같이 정해지는 값입니다.
 */
const LAND_AT = 0.52
const JOKER_Y = 108
/** 조커 5칸이 시작하는 자리. */
const JOKER_X = 372
const CONSUMABLE_X = 962
const PLAY_Y = 366
/**
 * 쓴 소모품의 네 마디.
 *
 * **눈이 따라갈 수 있는 길이여야 합니다.** 넷을 합쳐 1.5초 남짓이고, 그 사이에 판이 멈추지는
 * 않습니다 — 코어는 이미 처리했고 이것은 그 결과를 보이는 몫입니다.
 */
const ITEM_WARP = 0.30
const ITEM_ARRIVE = 0.62
const ITEM_FLASH = 0.34
const ITEM_HOLD = 1.05
/** 상점의 팝 딱지 하나의 너버. **상점 카드의 158 과 다릅니다.** */
const PACK_TILE_W = 104

/**
 * 소모품 슬롯으로 가는 갈래인가.
 *
 * **플레잉 카드는 아닙니다.** 「조커가 아니면 소모품」으로 세고 있어서, 표준 팩에서 카드를
 * 집으면 아무 상관 없는 소모품 하나가 팩에서 날아오는 연출이 붙었습니다 — 카드는 덱으로
 * 들어가고 소모품 칸은 그대로인데 화면만 그렇게 보였습니다.
 */
/**
 * 그 자리에서 잔액이 바뀌어야 하는 돈인가.
 *
 * **플레이어가 직접 낸 돈과 받은 돈입니다** — 사는 것과 파는 것. 그것은 누른 그 순간의
 * 일이므로 잔액이 바로 따라야 하고, 동전은 그 뒤로 날아가면 됩니다.
 *
 * 블라인드 보상과 이자와 조커가 주는 돈은 아닙니다 — 그것들은 하나씩 세어 올리는 것이
 * 정산의 몫이고, 미리 합계를 보여 주면 무엇으로 번 돈인지가 사라집니다.
 */
function paidNow(reason: string): boolean {
  return reason === 'shop' || reason === 'sell'
}

function isConsumable(kind: ShopItemKind): boolean {
  return kind === ShopItemKind.Tarot || kind === ShopItemKind.Planet
    || kind === ShopItemKind.Spectral
}

/**
 * 꾸욱 누르고 있으면 설명이 뜨는 데까지.
 *
 * **마우스에는 「올린다」가 있고 손가락에는 없습니다.** 손가락은 누르거나 안 누르거나
 * 둘뿐이라, 올리는 것으로 뜨는 설명은 손가락으로는 뜨게 할 방법이 없습니다 — 누르고
 * 기다리는 것이 그 자리를 대신합니다.
 *
 * 0.34초는 짧아서 고르려고 누른 것에도 뜨고, 0.7초는 길어서 뜨기 전에 손을 뗍니다.
 */
/**
 * 들린 카드가 제자리로 내려오는 데까지.
 *
 * **용수철의 시간입니다.** 내려오는 용수철이 8픽셀을 내려오는 데 그만큼 걸립니다 — 이보다
 * 짧으면 내려오는 도중에 나가고, 길면 다 내려온 카드가 가만히 기다립니다. 내려오는 것을
 * 두 배 빠르게 했으므로 이것도 절반입니다.
 */
const ITEM_SETTLE = 0.11

/**
 * 다 내려온 카드가 나가기 전에 머무는 동안.
 *
 * **내려오자마자 나가면 내려온 것이 보이지 않습니다.** `ITEM_SETTLE` 이 내려오는 용수철의
 * 시간 그대로라 카드가 8픽셀을 내려온 그 프레임에 오른쪽으로 빠졌고, 그러면 올라갔다
 * 내려온 한 몸짓이 「올라갔다가 사라졌다」로 뭉개집니다 — 내려온 자리에 한 박자 서 있어야
 * 그 카드가 제 할 일을 마치고 물러나는 것으로 보입니다.
 *
 * 길게 잡을 자리가 아닙니다. 이 뒤에 정산이 서므로, 여기서 끄는 만큼 그것이 늦습니다.
 */
const ITEM_LINGER = 0.22

const HOLD_TIP = 0.45
/** 그 사이에 손가락이 이만큼 움직이면 누른 것이 아니라 끈 것입니다. */
const HOLD_SLACK = 16
/**
 * 고른 카드가 무슨 족보인지 뜨는 자리.
 *
 * **고른 카드는 줄에서 위로 올라옵니다.** 그만큼 띄워 두지 않으면 이 쪽지가 올라온 카드에
 * 걸터앉습니다.
 */
const PREVIEW_Y = 430
const HAND_Y = 608
/** 버튼 줄. **패 아래입니다** — 패와 겹치면 카드를 고를 수가 없습니다. */
/**
 * 판 아래 버튼 줄의 윗변.
 *
 * **손가락에 맞게 키웠습니다.** 46픽셀 높이는 마우스에는 넉넉하지만 손가락에는 빠듯해서,
 * 낸다와 버린다 사이의 취소를 잘못 누릅니다 — 키운 만큼 줄이 위로 올라오고, 손패와 지시문도
 * 그만큼 비켜섭니다.
 */
const BUTTON_Y = 728
/** 낸다·버린다의 크기. */
const PLAY_W = 148
const PLAY_H = 56
/** 취소. 가운데에 서고 그 둘보다 좁습니다. */
const CLEAR_W = 76
/** 버튼 사이. **손가락 하나가 들어가야 합니다.** */
const BUTTON_GAP = 8
/** 상점의 줄들. **팩 줄이 카드 줄과 바우처 사이에 들어갑니다.** */
const SHOP_CARD_Y = 252

/** 고른 카드에 도는 빛의 색. 셰이더가 0..1 로 받습니다. */
const PICK_TINT: [number, number, number] = [0.45, 1.0, 0.68]

/** 줄바꿈. 문자열 안에 그대로 적으면 이 파일을 고치는 도구들이 자꾸 끊어 놓습니다. */
const NEWLINE = String.fromCharCode(10)

/** 칩과 배수 칸의 윗변. 불이 이 자리를 따라다닙니다. */
// 칩과 배수의 줄. **점수 칸과 사이를 벌립니다** — 그 사이에 고른 족보의 이름이 섭니다.
/**
 * 칩 × 배수 상자의 윗변.
 *
 * **위로 불의 자리를 비워 둡니다.** 불은 이 윗변에 뿌리를 두고 위로 솟으므로, 그만큼
 * 위에는 아무것도 두지 않습니다 — 족보 이름은 그 불 위입니다.
 */
/**
 * 떠오르는 차이 글의 개수와 목숨.
 *
 * **여덟이면 넉넉합니다.** 한 프레임에 바뀌는 칸은 많아야 셋(핸드·버리기·안테)이고,
 * 0.62초는 그 셋이 두 번 겹칠 만큼입니다.
 */
/** 새로 선 태그가 번쩍이는 동안. 부우 하고 나왔다가 잦아드는 데까지입니다. */
const TAG_FLASH = 0.55
/** 태그가 발동할 때 켜졌다 잦아드는 데까지. */
const TAG_FIRE = 0.6
/** 나오는 번쩍임이 잦아들기를 기다리는 동안. 그 자리에서 쓰이는 태그에만 걸립니다. */
const TAG_FIRE_WAIT = 0.5


const DELTA_POOL = 8
const DELTA_LIFE = 0.62

const CHIPS_Y = 336
/**
 * 칩 × 배수 덩어리.
 *
 * **하나입니다.** 칸 둘이 각자 테두리를 두르고 있었고, 곱셈표가 그 사이의 빈 자리에 글자
 * 하나로 떠 있어서 어디에도 속하지 않은 채 걸쳐 보였습니다 — 바탕을 색으로 갈라 두면
 * 테두리가 할 일이 없어지고, 곱셈표는 두 색이 만나는 자리에 앉습니다.
 */
const CHIPS_H = 58
const CHIPS_R = 10
/** 두 상자 사이. **곱셈표가 그 사이에 섭니다.** */
const CHIPS_GAP = 34
/**
 * 불이 상자 위로 솟는 높이와, 뿌리가 상자 안으로 물리는 깊이.
 *
 * **상자 위에 붙습니다.** 상자 안에서 태우면 불이 숫자를 덮어 아무것도 읽히지 않습니다 —
 * 사람 머리가 타는 것처럼 윗변에 뿌리를 두고 위로 솟습니다.
 */
const FIRE_RISE = 40
const FIRE_BITE = 5
/**
 * 불은 **자기 반 안에서** 탑니다.
 *
 * 예전에는 칸 뒤에 두고 칸을 반투명하게 만들어 비치게 했는데, 그러면 테두리 밖으로 자락이
 * 새어 나오고 칸 위에 얼룩이 얹힌 것으로 보였습니다 — 바탕이 색으로 갈린 지금은 그 색 위에
 * 그대로 얹으면 되고, 반 밖으로 나가지 않게 가려 둡니다.
 */

/** 덱 판의 카드에 쓰는 것들. 손패의 카드와 같은 값이라 여기 한 벌만 둡니다. */
const MINI_RANK: Record<number, string> = {
  2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9', 10: '10',
  11: 'J', 12: 'Q', 13: 'K', 14: 'A',
}

const MINI_TINT: Partial<Record<number, number>> = {
  [EnhancementKind.Bonus]: 0xcfe0f5,
  [EnhancementKind.Mult]: 0xf5ccd2,
  [EnhancementKind.Wild]: 0xe6d6f5,
  [EnhancementKind.Glass]: 0xd8f0f5,
  [EnhancementKind.Steel]: 0xd6d6d6,
  [EnhancementKind.Stone]: 0xa9a396,
  [EnhancementKind.Gold]: 0xf3dc99,
  [EnhancementKind.Lucky]: 0xd2f0c6,
}

/**
 * 족보 하나가 어떤 모양인가.
 *
 * **규칙이 아니라 보기입니다.** 어느 카드로 예를 들지는 판정에 아무 영향이 없고, 그래서
 * 표가 아니라 여기 있습니다. `counts` 는 그 카드가 족보에 드는가입니다 — 들지 않는 카드가
 * 물러나 있어야 「다섯 장을 냈는데 둘만 센다」가 그림에 남습니다.
 */
const HAND_SHAPE: Partial<Record<PokerHandKind, { rank: number; suit: SuitKind;
                                                  counts: boolean }[]>> = {
  [PokerHandKind.HighCard]: [
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 10, suit: SuitKind.Heart, counts: false },
    { rank: 7, suit: SuitKind.Club, counts: false },
    { rank: 5, suit: SuitKind.Diamond, counts: false },
    { rank: 3, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.Pair]: [
    { rank: 9, suit: SuitKind.Spade, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: false },
    { rank: 6, suit: SuitKind.Diamond, counts: false },
    { rank: 2, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.TwoPair]: [
    { rank: 9, suit: SuitKind.Spade, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 4, suit: SuitKind.Club, counts: true },
    { rank: 4, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.ThreeOfAKind]: [
    { rank: 7, suit: SuitKind.Spade, counts: true },
    { rank: 7, suit: SuitKind.Heart, counts: true },
    { rank: 7, suit: SuitKind.Club, counts: true },
    { rank: 11, suit: SuitKind.Diamond, counts: false },
    { rank: 3, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.Straight]: [
    { rank: 5, suit: SuitKind.Spade, counts: true },
    { rank: 6, suit: SuitKind.Heart, counts: true },
    { rank: 7, suit: SuitKind.Club, counts: true },
    { rank: 8, suit: SuitKind.Diamond, counts: true },
    { rank: 9, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.Flush]: [
    { rank: 2, suit: SuitKind.Heart, counts: true },
    { rank: 6, suit: SuitKind.Heart, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 11, suit: SuitKind.Heart, counts: true },
    { rank: 13, suit: SuitKind.Heart, counts: true },
  ],
  [PokerHandKind.FullHouse]: [
    { rank: 8, suit: SuitKind.Spade, counts: true },
    { rank: 8, suit: SuitKind.Heart, counts: true },
    { rank: 8, suit: SuitKind.Club, counts: true },
    { rank: 3, suit: SuitKind.Diamond, counts: true },
    { rank: 3, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.FourOfAKind]: [
    { rank: 12, suit: SuitKind.Spade, counts: true },
    { rank: 12, suit: SuitKind.Heart, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: true },
    { rank: 12, suit: SuitKind.Diamond, counts: true },
    { rank: 5, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.StraightFlush]: [
    { rank: 9, suit: SuitKind.Club, counts: true },
    { rank: 10, suit: SuitKind.Club, counts: true },
    { rank: 11, suit: SuitKind.Club, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: true },
    { rank: 13, suit: SuitKind.Club, counts: true },
  ],
  [PokerHandKind.FiveOfAKind]: [
    { rank: 10, suit: SuitKind.Spade, counts: true },
    { rank: 10, suit: SuitKind.Heart, counts: true },
    { rank: 10, suit: SuitKind.Club, counts: true },
    { rank: 10, suit: SuitKind.Diamond, counts: true },
    { rank: 10, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.FlushHouse]: [
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Diamond, counts: true },
  ],
  [PokerHandKind.FlushFive]: [
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
  ],
}

const MINI_SEAL: Partial<Record<number, number>> = {
  [SealKind.Red]: 0xd23b3b,
  [SealKind.Blue]: 0x3b7fd2,
  [SealKind.Gold]: 0xe0b53b,
  [SealKind.Purple]: 0x9a5bd2,
}

/**
 * 불의 층 둘.
 *
 * **바깥은 상자의 색입니다.** 그래야 불의 뿌리가 상자와 같은 색이라 그 상자가 타는 것으로
 * 보입니다 — 손으로 적어 두었더니 상자는 칩의 파랑인데 불은 다른 파랑이었습니다.
 *
 * **안쪽 심지는 흰쪽이지만 그냥 흰색이 아닙니다.** 순전한 흰색으로 끌어오면 붉은 불이
 * 분홍이 됩니다 — 불의 심지는 노랗고, 찬 불의 심지는 희읍스름한 하늘색입니다. 그 둘만
 * 색으로 적어 둡니다.
 */
function flameOf(tint: number, core: number): {
  cool: [number, number, number]
  hot: [number, number, number]
} {
  return { cool: rgbOf(tint), hot: rgbOf(mix(tint, core, 0.62)) }
}

/** 셰이더가 받는 차례 그대로. */
function flamePair(tint: number, core: number):
[[number, number, number], [number, number, number]] {
  const pair = flameOf(tint, core)
  return [pair.cool, pair.hot]
}

/** 뜨거운 불의 심지. 노랗습니다. */
const CORE_WARM = 0xffd76a
/** 찬 불의 심지. 희읍스름한 하늘색입니다. */
const CORE_COLD = 0xd8f4ff

const DECK_X = SIZE.width - 62
const DECK_Y = 608

/**
 * 펼친 팩의 카드 하나.
 *
 * **띠를 따로 들고 있습니다.** 자리가 없다는 표시는 팩이 열려 있는 동안에도 바뀌므로 —
 * 소모품을 쓰면 자리가 생깁니다 — 매 프레임 켜고 끄려면 그 조각을 찾을 수 있어야 합니다.
 */
/**
 * 「런 정보」 의 갈래.
 *
 * **한 판을 도는 동안 궁금해지는 것이 셋입니다** — 어느 족보가 몇 점인지, 이 안테의
 * 블라인드가 무엇인지, 지금 난이도가 무엇을 바꾸는지.
 */
type RunInfoTab = 'hands' | 'blinds' | 'stakes'

interface PackFace {
  node: Container
  card: Container
  mark: Container
  plate: Graphics
  label: Text
}

/** 펼친 팩의 카드 한 장. 얼굴과, 그 한 장이 자기만 아는 것들입니다. */
interface PackView {
  face: PackFace
  motion: Motion
  index: number
  item: ShopItem
  /**
   * 갸웃거리는 물결의 자리.
   *
   * **낱장이 저마다 다른 자리에서 시작합니다.** 같은 자리에서 시작하면 다섯 장이 한
   * 몸으로 기울어지고, 그것은 살아 있는 것이 아니라 판이 통째로 흔들리는 것입니다.
   */
  sway: number
  /**
   * 나오면서 한 번 반짝이는 것. 아직 나오지 않았으면 `-1`, 나온 뒤 0 에서 1 로 갑니다.
   *
   * **끝을 세는 것이지 남은 것을 세는 것이 아닙니다.** 세기는 이 값의 사인이므로 0 에서
   * 올라 1 에서 내려오고, 그래서 켜지는 것도 꺼지는 것도 부드럽습니다.
   */
  glow: number
  /** 반짝이는 동안에만 붙습니다. 다 반짝이면 떼어 냅니다 — 필터 하나가 곧 텍스처 하나입니다. */
  arrive?: ArriveFilter
}

export class Game {
  private readonly world = new Container()
  private readonly backdrop = new Container()
  /** 배경을 칠하는 흰 판. 창 크기를 그대로 받습니다. */
  private readonly sheet = new Sprite(Texture.WHITE)
  private readonly board = new Container()
  private readonly overlay = new Container()
  /**
   * 판이 떠 있을 때 뒤로 물러나는 것들.
   *
   * **판 뒤가 흐려져야 판이 앞에 있는 것으로 보입니다.** 어둡게만 덮으면 뒤의 글자가 읽히는
   * 채로 어두워질 뿐이고, 눈이 자꾸 뒤로 갑니다. 흐림은 이 통 하나에 걸립니다 — 판과 설명
   * 쪽지는 이 통 밖이라 또렷하게 남습니다.
   */
  private readonly recede = new Container()
  private readonly blur = new BlurFilter({ strength: 0, quality: 3 })
  /** 배경도 함께 흐립니다. **필터 하나를 둘에 걸지 않습니다** — 같은 프레임에 두 번 쓰입니다. */
  private readonly blurBack = new BlurFilter({ strength: 0, quality: 3 })
  /** 지금 흐린 정도. 판이 열리고 닫힐 때 잦아듭니다. */
  private blurShown = 0

  /**
   * 판의 상태.
   *
   * **코어가 제자리에서 고칩니다** — `apply` 는 이 객체를 받아 바꾸고 이벤트만 돌려줍니다.
   * 이 객체를 갈아 끼우는 곳은 **시작 전의 `useSeed` 하나뿐**입니다.
   */
  private state: RunState
  private readonly feel: Feel
  private readonly audio: Audio
  private readonly player: TimelinePlayer
  private readonly background = new BackgroundFilter()
  private readonly particles = new Particles()
  private readonly coins = new Coins()
  /**
   * 날아가는 칩.
   *
   * **칩이 숫자로만 오르면 무엇이 얼마를 낸 것인지 남지 않습니다.** 카드가 낸 칩은 그
   * 카드에서 칩 칸으로 날아가고, 액면마다 색이 다르므로 개수와 색이 곧 얼마인지입니다.
   */
  private readonly punch = new PunchFilter(SIZE.width, SIZE.height)
  private readonly tooltip = new Tooltip()
  /**
   * 무엇이 일어났는지 알리는 줄들.
   *
   * **판의 오른쪽에 섭니다.** 가운데에 세우면 낸 카드를 덮고, 그러면 읽는 것이 아니라
   * 사라지기를 기다리게 됩니다.
   */
  // **처음 서는 자리는 판 밖입니다.** 게임이 뜨는 곳이 로그인 화면이나 타이틀이므로,
  // 판 안의 자리로 시작하면 첫 알림 하나가 구석에 납니다.
  private readonly toasts = new Toasts(Toasts.OUT_RUN)

  private readonly cards = new Map<number, CardView>()
  private readonly playedViews: CardView[] = []
  /** 아직 날아가지 않은 카드들. 왼쪽부터 한 장씩 차례로 갑니다. */
  private readonly slams: { view: CardView; x: number; at: number }[] = []
  /** 마지막 카드가 자리에 닿는 시각. **그때까지 득점을 세지 않습니다.** */
  private playLanded = 0
  /** 아직 나가지 않은 버린 카드들. 이것도 왼쪽부터 한 장씩입니다. */
  private readonly fades: { view: CardView; at: number }[] = []
  /**
   * 이번 블라인드에서 화면 밖으로 나간 카드가 몇 장인가.
   *
   * **세는 것은 돌려보내기 위해서입니다.** 낸 것도 버린 것도 오른쪽으로 빠져나가는데,
   * 그것으로 끝나면 한 판을 도는 동안 덱이 계속 줄기만 하고 아무것도 돌아오지 않습니다 —
   * 카드는 없어지는 것이 아니라 다음 판에 다시 나오는 것입니다.
   */
  private retired = 0
  /**
   * 덱으로 돌아오는 중인 카드들.
   *
   * **되돌아오는 것은 낱장이 아니라 장수입니다.** 어느 카드가 어느 자리로 돌아가는지는
   * 아무도 세지 않으므로, 돌아오는 것은 뒷면 한 장씩이면 됩니다.
   */
  private readonly recalls: { node: Container; motion: Motion; at: number; sent: boolean }[] = []
  /** 아직 깔리지 않은 뽑은 카드들. **덱에서 한 장씩 옵니다.** */
  private readonly deals: { uid: number; at: number }[] = []
  /**
   * 마지막 장이 자리를 잡을 때까지.
   *
   * **놓은 것과 앉은 것은 다릅니다** — 예약이 다 빠져도 카드는 아직 용수철로 날아가는
   * 중이고, 그 동안에도 마우스가 닿으면 지나가는 카드가 들려 올라갑니다.
   */
  private dealtUntil = 0
  private readonly jokers = new Map<number, JokerView>()
  /** 타는 중인 조커들. 다 타면 치웁니다. */
  private readonly burning: JokerView[] = []
  private readonly selected = new Set<number>()
  /**
   * 고른 조커나 소모품 하나.
   *
   * **누르는 것만으로는 아무것도 팔리지 않습니다.** 조커가 판의 전부인 게임에서 한 번
   * 잘못 누른 것이 판을 끝내면 안 됩니다 — 고르면 그 밑에 무엇을 할지가 버튼으로 서고,
   * 그 버튼을 눌러야 일어납니다.
   */
  /**
   * 지금 고른 것 하나.
   *
   * **누르는 것과 하는 것을 가릅니다.** 조커와 소모품은 처음부터 그랬고 — 눌러 고르면 그
   * 밑에 `사용`·`판매` 가 섭니다 — 상점과 팩만 누르는 그 자리에서 곧바로 되었습니다.
   * 사는 것도 집는 것도 되돌릴 수 없는 일이므로, 한 번 더 눌러야 합니다.
   *
   * 상점과 팩은 `uid` 가 아니라 **칸의 번호**입니다. 그 둘은 개체가 아니라 자리이고,
   * 자리는 상점이 다시 그려질 때마다 새로 만들어지므로 개체로는 가리킬 것이 없습니다.
   */
  private held?: { kind: 'joker' | 'consumable' | 'shop' | 'pack' | 'pack_slot'; uid: number }
  /** 고른 것 밑에 서는 버튼들. */
  private readonly heldBar = new Container()
  /**
   * 블라인드 딱지 아래의 왼쪽 판.
   *
   * **통째로 내려갑니다.** 딱지가 들고 있는 태그만큼 자라므로 그 아래가 그만큼 밀립니다.
   */
  private readonly panelStack = new Container()
  /**
   * 상점 칸의 딱지들.
   *
   * **자리를 물을 곳이 있어야 합니다.** 고른 칸 밑에 단추를 세우려면 그 칸이 어디에 있는지
   * 알아야 하고, 산 것이 날아가는 자리도 그 칸입니다 — 상점은 다시 그릴 때마다 딱지를
   * 새로 만드므로 그때마다 여기도 새로 채웁니다.
   */
  private readonly shopTiles =
    new Map<number, { tile: Container; baseY: number; price: Container;
                     mid: number }>()
  /**
   * 고른 것이 들리는 높이.
   *
   * **누른 것이 올라와야 골랐다는 것이 됩니다.** 단추가 그 밑에 서는 것만으로는 어느 칸을
   * 고른 것인지가 단추의 자리로만 읽히고, 칸 자체는 아무 일도 없었던 것처럼 남습니다 —
   * 조커와 소모품이 들리는 것과 같은 몸짓입니다.
   */
  private readonly shopLift = new Spring()
  /** 상점의 팩 딱지들. 카드 딱지와 높이가 달라 그것도 함께 들고 있습니다. */
  private readonly packSlotTiles =
    new Map<number, { tile: Container; height: number; baseY: number;
                     price: Container; mid: number }>()
  /**
   * 소모품이 들린 높이.
   *
   * **조커와 같은 용수철을 탑니다.** 조커는 `place` 로 목표만 정하고 용수철이 데려가는데,
   * 소모품은 자리를 매번 새로 그리므로 그럴 것이 없습니다 — 높이 하나를 여기 두고 화면이
   * 그것을 따라갑니다. 값이 다르면 나란히 선 둘이 다른 물건처럼 움직입니다.
   */
  private readonly consumableLift = new Map<number, Spring>()
  /** 지금 그려져 있는 소모품 칸들. 매 프레임 높이를 다시 얹습니다. */
  private readonly consumableTiles: {
    uid: number
    tile: Container
    /** 이 칸의 제자리. 들리는 것과 오는 것이 이 자리를 기준으로 얹힙니다. */
    baseX: number
    baseY: number
  }[] = []
  /**
   * 타고 있는 소모품.
   *
   * **쓴 것은 타서 사라집니다.** 그냥 없어지면 무엇이 없어진 것인지 · 정말 쓰인 것인지
   * 눈이 따라가지 못합니다. 조커를 팔 때와 같은 불이고 같은 빠르기입니다.
   */
  private readonly burningItems: {
    tile: Container
    /** 얼굴. **그림자는 뺍니다** — 울렁임과 번쩍임이 그림자에도 걸리면 얼룩이 따로 남습니다. */
    face: Container
    arrive: ArriveFilter
    dissolve: DissolveFilter
    from: { x: number; y: number }
    to: { x: number; y: number }
    /** 쓰기 시작한 뒤 지난 시간. 이것 하나로 네 마디가 갈립니다. */
    life: number
    burn: number
    /** 번쩍임을 한 번 냈는가. 자리에 닿는 그 한 프레임입니다. */
    flashed: boolean
    /** 나오면서 커지는가. 쓴 것만 그렇습니다. */
    grows: boolean
  }[] = []
  /**
   * 지금 걸려 있는 것들.
   *
   * **토스트는 스치고 지나갑니다.** 무엇이 왜 그런지는 판이 도는 내내 볼 수 있어야 합니다 —
   * 손패가 왜 11장인지, 이번 보스가 무엇을 막고 있는지, 들고 있는 태그가 언제 터지는지.
   */
  private readonly activeLayer = new Container()
  /**
   * 정산 판.
   *
   * **돈이 어디서 나왔는지가 한자리에 모여야 합니다.** 동전이 날아가는 것만으로는 격파
   * 보상과 남긴 핸드와 이자가 한 덩어리로 보이고, 다음 판에 무엇을 아껴야 하는지가
   * 남지 않습니다.
   */
  private readonly payout: ModalPanel = {
    view: new Container(),
    size: { width: 380, height: 240 },
    // **다 닫힌 뒤에 상점이 섭니다.** 닫기를 누른 그 순간에 그리면 판이 아직 물러나는
    // 중이라 상점이 비어 보입니다.
    onClosed: () => {
      this.payoutOpen = false
      this.payoutWanted = false
      this.refresh()
    },
  }
  /** 정산에 오른 줄들. 이벤트가 하나씩 더합니다. */
  private readonly payoutRows: { why: string; amount: number }[] = []
  /**
   * 정산 판을 세울 차례인가.
   *
   * **카드가 다 걷힌 뒤에 섭니다.** 판이 떠 있는 채로 그 밑에서 카드가 물러나면, 끝난
   * 것과 끝나는 중인 것이 한 화면에 겹칩니다 — 격파는 이미 정해졌지만 그것을 보는 순서는
   * 카드가 물러나고 나서입니다.
   */
  private payoutWanted = false
  /**
   * 정산 판이 지금 떠 있는가.
   *
   * **`modals.has` 를 쓰지 않습니다.** 그것은 닫히는 중인 판을 곧바로 없는 것으로 세므로,
   * 「받는다」 를 누른 다음 프레임에 이 코드가 판을 다시 엽니다 — 창이 닫히지 않고 계속
   * 눌리던 것이 그것입니다.
   */
  private payoutOpen = false
  /**
   * 정산의 줄이 하나씩 서는 것.
   *
   * 판이 열릴 때 이미 줄이 다 모여 있으므로, 쌓이는 것은 **그리는 쪽에서** 만듭니다 —
   * 상점이 하나씩 서는 것과 같은 계산이고, 다만 목록을 따로 둡니다: `reveals` 는
   * `refresh` 가 비우므로 판이 열리는 그 프레임에 지워집니다.
   */
  private readonly payoutNodes: { node: Container; at: number; from: number }[] = []
  /**
   * 줄이 서기 전에 뜨는 「정산 중」.
   *
   * **판은 먼저 열리고 줄은 하나씩 쌓입니다.** 그 사이가 빈 상자라, 무엇을 기다리는
   * 중인지가 적혀 있지 않으면 판이 잘못 열린 것으로 보입니다.
   */
  private payoutWait?: {
    head: Text
    /** 뼈대 줄. 매 프레임 다시 그립니다 — 줄마다 옅어지는 정도가 다릅니다. */
    bones: Graphics
    width: number
    rows: number
    top: number
    rowH: number
    /** 첫 줄이 서는 시각. 그 뒤로 `PAYOUT_STEP` 마다 하나씩입니다. */
    begin: number
  }

  /** 「적용 중」 을 펼친 판. */
  private readonly activePanel: ModalPanel = {
    view: new Container(),
    size: { width: 460, height: 60 },
  }
  /**
   * 끌고 있는 것.
   *
   * **자리가 규칙입니다.** 득점은 낸 카드의 왼쪽부터이고 조커는 슬롯의 왼쪽부터이므로,
   * 무엇을 어디에 두느냐가 최종 점수를 바꿉니다 — 그것을 정하지 못하면 판을 짜는 일의
   * 절반이 없습니다.
   */
  private drag?: {
    kind: 'hand' | 'joker'
    uid: number
    startX: number
    startY: number
    grabX: number
    moved: boolean
    /** 누른 것과 끈 것을 가르는 거리. **손가락은 마우스보다 넉넉해야 합니다.** */
    slack: number
  }
  /** 손패가 놓인 자리. 끌 때 어느 칸으로 가는지를 이것으로 셉니다. */
  private handSpots = { startX: 0, spacing: 0 }
  /** 도움. 이것도 고르면 더 높은 족보가 되는 카드들입니다. */
  private readonly hinted = new Set<number>()

  private readonly badge = new BlindBadge(PANEL_W)
  private readonly score = new Slot(t('ui.slot.round_score'), PANEL_W, 68, COLOR.ink)
  // **이 둘이 화면에서 가장 큰 두 숫자입니다.** 점수는 이 둘의 곱이고, 나머지 칸들은
  // 그것을 설명하는 것들입니다 — 크기가 그 서열을 그대로 보여야 합니다.
  // 칩은 오른쪽으로, 배수는 왼쪽으로 붙습니다 — 사이의 곱셈표와 함께 한 식으로 읽힙니다.
  /**
   * 고른 것이 무슨 족보인가.
   *
   * **칩과 배수 칸 바로 위입니다.** 그 두 수가 어디서 온 것인지가 바로 위에 적혀 있어야
   * 한 덩어리로 읽힙니다 — 판 가운데에만 띄우면 눈이 왼쪽과 가운데를 오갑니다.
   */
  private readonly handLabel = new Text({
    text: '',
    style: {
      // **12픽셀은 작았습니다.** 지금 고른 것이 무슨 족보인가는 화면에서 점수 다음으로
      // 중요한 글이고, 칩과 배수가 어디서 나온 값인지를 잇는 유일한 줄입니다.
      fontSize: 17, fill: COLOR.ink, fontWeight: '800', letterSpacing: 0.5,
      stroke: { color: 0x0a0f18, width: 3 },
    },
  })
  /**
   * 칩 × 배수의 바탕.
   *
   * **칸이 아니라 이것이 테두리를 대신합니다.** 색이 갈리면 경계가 색으로 읽히므로 선이
   * 필요 없고, 불이 그 색 위에 그대로 얹힙니다.
   */
  private readonly scoreBox = new Graphics()
  /**
   * 칩과 배수.
   *
   * **이름이 없습니다.** 파란 상자의 수와 붉은 상자의 수 사이에 `×` 가 있으면 그것이 무엇인지
   * 더 적을 것이 없습니다 — 이름은 자리만 잡아먹고 숫자를 아래로 밀어냅니다.
   *
   * 숫자는 흰색입니다. 상자의 색이 이미 칩과 배수를 가르므로, 숫자까지 그 색이면 색만
   * 남고 수가 흐려집니다.
   */
  private readonly chips = new Slot('', (PANEL_W - CHIPS_GAP) / 2, CHIPS_H, COLOR.ink, 34, 1, true)
  private readonly mult = new Slot('', (PANEL_W - CHIPS_GAP) / 2, CHIPS_H, COLOR.ink, 34, 0, true)
  /** 두 칸 뒤에서 타오르는 불. 배수가 커지면 불길이 높아집니다. */
  private readonly chipsFire = new Sprite(Texture.WHITE)
  private readonly multFire = new Sprite(Texture.WHITE)
  /**
   * 불의 색.
   *
   * **상자의 색에서 나옵니다.** 손으로 적어 두었더니 상자와 불이 따로 놀았습니다 — 상자는
   * 칩의 파랑인데 불은 다른 파랑이었고, 색을 고치면 한쪽만 따라왔습니다.
   *
   * 바깥 층이 상자의 색이고 안쪽 심지는 그것을 흰쪽으로 끌어온 것입니다 — 그러면 불의
   * 뿌리가 상자와 같은 색이라 그 상자가 타는 것으로 보입니다.
   */
  private readonly chipsFlame = new FlameFilter(...flamePair(COLOR.chips, CORE_COLD))
  private readonly multFlame = new FlameFilter(...flamePair(COLOR.mult, CORE_WARM))
  /** 지금 얼마나 뜨거운가. 박자가 올리고 시간이 내립니다. */
  private fever = 0
  /**
   * 왼쪽 판의 칸들이 마지막으로 보여 준 수.
   *
   * **차이를 적으려면 앞의 값을 들고 있어야 합니다.** 상태에는 지금 값만 있고 「얼마에서
   * 얼마가 되었는가」는 없으므로, 화면이 자기가 보여 준 것을 기억합니다 — 판을 새로 깔면
   * 이것도 함께 되돌립니다.
   *
   * `-1` 은 아직 아무것도 보여 주지 않았다는 뜻이고, 그때는 차이를 적지 않습니다.
   */
  private panelShown = { hands: -1, discards: -1, ante: -1 }
  /**
   * 새로 선 태그가 번쩍이는 것. 태그 하나에 0에서 1로 갑니다.
   *
   * **칩이 아니라 태그 이름으로 셉니다.** 칩은 화면을 다시 그릴 때마다 새로 만들어지므로
   * 그것에 붙여 두면 다음 프레임에 없어집니다 — 번쩍이는 것은 그 태그이지 그 통이
   * 아닙니다.
   */
  /**
   * 번쩍이는 것은 **마지막에 받은 하나뿐입니다.**
   *
   * 표로 두었더니 둘을 받았을 때 둘 다 번쩍였습니다 — 번쩍임은 「새로 생겼다」는 말이고,
   * 이미 서 있던 것이 함께 번쩍이면 그 말이 둘을 가리키게 됩니다.
   */
  private tagFlashId = ''
  private tagFlashLife = 1
  /**
   * 지금 발동하는 태그들. 태그 하나에 `-지연`에서 1로 갑니다.
   *
   * **쓰였다는 것은 사라지는 것이 아니라 켜지는 것입니다.** 쓰인 태그를 곧바로 흐리게
   * 두었더니 「무엇이 없어졌다」로 보였습니다 — 그 태그가 한 일은 그 순간에 발동한
   * 것이고, 발동은 켜졌다가 잦아드는 것입니다. 흐려지는 것은 그 뒤에 남는 상태입니다.
   *
   * **음수에서 시작합니다.** 그 자리에서 쓰이는 태그는 받는 순간과 쓰이는 순간이 같아서,
   * 나오는 번쩍임과 발동하는 번쩍임이 한 프레임에 겹칩니다 — 한 박자 뒤에 켜야 둘이
   * 갈립니다.
   */
  private readonly tagFire = new Map<string, number>()
  /**
   * 이번 안테에 이미 쓰인 태그들. 받은 순서입니다.
   *
   * **쓰였다고 지우지 않습니다.** 태그 24종 중 14종은 건너뛰는 그 순간에 쓰이고 목록에서
   * 빠지므로, 들고 있는 것만 그리면 그 열넷은 화면에 한 프레임도 서지 못합니다 — 무엇을
   * 받았는지가 남지 않는 것이고, 그러면 건너뛴 대가가 없었던 것으로 보입니다.
   *
   * **흐리게 남깁니다.** 아직 들고 있는 것과 이미 쓴 것은 다음에 할 일이 다르므로 같은
   * 밝기로 서 있으면 안 됩니다.
   *
   * 안테가 바뀌면 비웁니다 — 그 안테에 무엇을 받았는가가 이 줄이 답하는 것이고, 판이
   * 끝날 때까지 쌓으면 띠가 그것만으로 찹니다.
   */
  private tagSpent: string[] = []
  /** 이 줄이 어느 안테의 것인가. 바뀌면 비웁니다. */
  private tagSpentAnte = 0
  /**
   * 떠오르는 차이 글의 풀.
   *
   * 안 보이는 것이 노는 것입니다 — 따로 표를 두면 그 표와 화면이 어긋날 수 있고, 어긋나면
   * 보이는 글을 다시 쓰거나 노는 글이 영영 노는 채로 남습니다.
   */
  private readonly deltas: { node: Text; life: number; homeY: number }[] = []
  private readonly hands = new Slot(t('ui.slot.hands'), 124, 52, COLOR.good)
  private readonly discards = new Slot(t('ui.slot.discards'), 124, 52, 0xff9d5c)
  private readonly money = new Slot(t('ui.slot.money'), 124, 52, COLOR.money)
  private readonly anteSlot = new Slot(t('ui.slot.ante'), 124, 52, COLOR.ink)

  private readonly headline = new Text({
    text: '',
    style: {
      fontSize: 34, fill: COLOR.ink, fontWeight: '800',
      stroke: { color: 0x0a0f18, width: 5 },
    },
  })
  private readonly gauge = new Graphics()
  private readonly frames = new Graphics()
  /** 덱 더미. 상점에서는 화면 밖으로 밀려 나갑니다. */
  private readonly deckLayer = new Container()
  /**
   * 덱 더미.
   *
   * **뒷면이 바뀌면 다시 그립니다.** 판마다 덱이 다르고 덱마다 뒷면이 다르므로, 한 번
   * 그려 놓고 두면 두 번째 판의 더미가 첫 판의 뒷면입니다.
   */
  private readonly deckPile = new Container()
  private readonly deckSlide = new Spring(0, 150, 20)
  /** 패널 위에 얹는 빛. `panelGlow` 가 세기입니다. */
  private readonly panelFlash = new Graphics()
  /** 화면 전체에 얹는 빛. */
  private readonly screenFlash = new Graphics()
  /**
   * 조커 칸이 몇 칸 찼는가.
   *
   * **「조커」라고 적지 않습니다.** 칸에 조커가 서는 줄이고, 그 줄 아래의 `0 / 5` 는 그
   * 줄에 관한 것 말고 다른 것일 수 없습니다.
   */
  private readonly jokerCount = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '800' },
  })
  private readonly consumableCount = new Text({
    text: '', style: { fontSize: 12, fill: 0x9b8fd0, fontWeight: '800' },
  })
  private readonly deckLabel = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
  })

  private readonly playButton: Button
  private readonly discardButton: Button
  /** 고른 것을 한 번에 풉니다. **한 장씩 다시 누르는 것은 일입니다.** */
  private readonly clearButton: Button
  private readonly primaryButton: Button
  private readonly skipButton: Button
  private readonly rerollButton: Button
  private readonly sortRankButton: Button
  private readonly sortSuitButton: Button
  private readonly infoButton: Button
  /**
   * 나머지를 모아 둔 자리.
   *
   * **판 아래의 버튼은 둘입니다.** 넷이 늘어서 있으면 그 자리가 화면에서 가장 복잡한
   * 자리가 되는데, 정작 판을 두는 동안에는 하나도 누르지 않습니다 — 자주 쓰는 족보 목록만
   * 남기고 나머지는 이 안으로 들어갑니다.
   */
  private readonly menuButton: Button
  private readonly menu: ModalPanel = {
    view: new Container(),
    size: { width: 260, height: 60 },
  }
  /** 게임 방법. **첫 판에서 저절로 한 번 열립니다.** */
  /**
   * 떠 있는 판들.
   *
   * **여는 순서가 곧 위아래입니다.** 판마다 자기 층에 붙어 있으면 어느 것이 위인지가 붙인
   * 순서로 정해지고, 족보 목록이 블라인드 판 아래로 들어가는 일이 생깁니다.
   */
  private readonly modals = new Modals()
  private readonly guide = new Guide(
    () => this.modals.close(this.guide), () => this.toggleHandList())
  private readonly optionsPanel: OptionsPanel
  /** 조커 풀을 고르고 들여다보는 판. 팀이틀에서만 엽니다. */
  private readonly jokerPool: JokerPoolPanel
  private readonly challengePanel: ChallengePanel
  private readonly setupPanel: SetupPanel
  /** 깬 챌린지의 목록. 저장에 남습니다. */
  private readonly challenges: ChallengeProgress = loadProgress()
  /** 지금 도는 판의 챌린지. 빈 문자열이면 챌린지가 아닙니다. */
  private challengeId = ''
  /** 타이틀. **시작을 누르기 전에는 판이 없습니다.** */
  private readonly title: Title
  /**
   * 지금 어느 씬인가.
   *
   * **이 클래스가 아는 것은 둘입니다** — `loading` 은 이 클래스가 서기 전이라 진입점의
   * 몫이고, 여기서는 타이틀과 판 사이를 오갑니다.
   */
  private scene: Scene = 'title'
  /** 옵션. 타이틀이 고치고 화면이 읽습니다. */
  private readonly settings: Options = loadOptions()

  /**
   * 다음 판을 무엇으로 시작하는가.
   *
   * **표에 있는 것으로 걸러 읽습니다.** 저장된 값이 표에 없는 덱을 가리키면 시작 조건이
   * 하나도 걸리지 않은 판이 조용히 돌아갑니다.
   */
  private setup(): RunSetup {
    return validSetup(this.data,
                      { deckId: this.settings.deck, stake: this.settings.stake })
  }
  /**
   * 지금 무엇을 하면 되는가.
   *
   * **국면마다 한 줄입니다.** 화면에 버튼이 여럿 있어도 다음에 누를 것이 무엇인지가
   * 적혀 있지 않으면 처음 여는 사람은 움직이지 못합니다.
   */
  private readonly hint = new Container()
  /** 지금 적혀 있는 지시문. 같은 글이면 다시 만들지 않습니다. */
  private hintShown = ''
  private readonly shopLayer = new Container()
  /** 뜯어 놓은 팩. 상점 위를 덮습니다. */
  private readonly packLayer = new Container()
  private readonly consumableLayer = new Container()
  /** 들고 있는 태그의 딱지들. */
  private readonly tagLayer = new Container()
  /** 고른 카드가 무슨 족보인지. **이것이 없으면 감으로 내야 합니다.** */
  private readonly preview = new Container()
  private readonly previewPlate = new Graphics()
  private readonly previewHand = new Text({
    text: '', style: { fontSize: 19, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly previewValue = new Text({
    text: '', style: { fontSize: 16, fill: COLOR.inkDim, fontWeight: '700' },
  })
  /** 족보 목록. 무엇이 몇 점인지 볼 수 있어야 무엇을 키울지 정합니다. */
  private readonly handList: ModalPanel = {
    view: new Container(),
    size: { width: 540, height: 60 },
  }
  /** 족보 목록의 줄들. 어느 줄을 가리키고 있는지를 자리로 셉니다. */
  /** 「런 정보」 의 어느 갈래를 보고 있는가. */
  private runInfoTab: RunInfoTab = 'hands'
  private readonly handRows: { hand: PokerHandKind; seen: boolean;
                               y: number; height: number }[] = []
  private handBand?: Graphics
  private handPreview?: Container
  private handHovered = -1
  /**
   * 덱에 남은 카드.
   *
   * **무엇이 남았는지를 모르면 버릴지 낼지를 정할 수 없습니다.** 스트레이트에 한 장이
   * 모자랄 때 그 랭크가 덱에 아직 있는지가 그 판의 판단 전부입니다.
   */
  private readonly deckView: ModalPanel = {
    view: new Container(),
    size: { width: 520, height: 300 },
  }
  /**
   * 블라인드 셋을 한 자리에 세운 판.
   *
   * **셋을 함께 보여야 건너뛸지를 판단할 수 있습니다.** 지금 것 하나만 보여 주면 다음이
   * 무엇인지 모르는 채로 넘길지를 정하게 되고, 그건 선택이 아니라 찍기입니다.
   */
  private readonly blindPick = new Container()
  /**
   * 눌러야 하는 것들의 자리.
   *
   * **자리를 계산하는 곳이 하나여야 합니다.** 판의 밑단은 글의 길이에 따라 자라므로,
   * 바깥에서 같은 계산을 다시 하면 말을 바꾼 날에 어긋납니다.
   */
  private readonly spots: Record<string, { x: number; y: number }> = {}
  /**
   * 블라인드 판이 들어오는 정도. 0 에서 1 로 갑니다.
   *
   * **떡하니 서 있으면 밋밋합니다.** 셋이 왼쪽부터 차례로 아래에서 올라와야 「고르는 자리에
   * 왔다」가 됩니다.
   */
  private blindEnter = 0
  /** 지금 그려진 판이 어느 블라인드의 것인가. 바뀌면 다시 들어옵니다. */
  private blindShown = -1
  /** 끝났을 때 덮는 판. */
  private readonly gameOver = new Container()
  /** 머리글이 얼마나 남았는가. **계속 떠 있으면 지난 일이 지금 일처럼 보입니다.** */
  private headlineLife = 0
  private headlineSpan = 1
  /**
   * 점수가 굴러가는 동안 나는 소리의 시계.
   *
   * **숫자가 굴러가는 1초가 무음이었습니다.** 재어 보면 낸 뒤 8초 동안 소리가 14번이고
   * 그 사이에 1089밀리초가 비었는데, 비는 그 자리가 칩과 배수가 곱해져 점수가 올라가는
   * 바로 그 순간입니다.
   */
  private ratchet = 0
  /** 곱해지기를 기다리는 동안 얼마나 조여들었는가. 0 에서 1 로 갑니다. */
  private build = 0

  /** 예약해 둔 소리. 음이 하나씩 올라가는 아르페지오를 이것으로 냅니다. */
  private readonly chimes: { at: number; cue: string; semitones: number }[] = []

  /**
   * 상점의 것들이 하나씩 서는 것.
   *
   * **한꺼번에 그려 놓으면 무엇이 놓여 있는지 훑어야 합니다.** 정산 판이 줄을 하나씩
   * 쌓았던 것과 같은 이유입니다 — 하나씩 서면 눈이 그 하나를 따라가고, 다 서고 나면
   * 그때가 고르는 때입니다.
   */
  private readonly reveals: { node: Container; at: number; from: number }[] = []
  /** 지금 무엇을 세우고 있는가. 판이 그리기 전에 정합니다. */
  private revealing = { layer: this.shopLayer, base: 0, slot: 0, sound: false }
  /**
   * 잠시 뒤에 한 번 할 것.
   *
   * **닿는 순간에 해야 하는 것들이 있습니다** — 산 조커가 줄에 꽂히는 것은 날아가는
   * 시간만큼 뒤입니다. 그때에 맞춰 소리와 조각을 냅니다.
   */
  private readonly later: { at: number; run: () => void }[] = []

  /**
   * 새 조커가 날아오는 자리.
   *
   * **산 것은 산 자리에서 옵니다.** 위에서 떨어지면 어느 칸을 눌러서 얻은 것인지가
   * 남지 않습니다. 한 장에 한 번만 쓰이므로 쓰고 나면 비웁니다.
   */
  private arriveFrom: { x: number; y: number } | undefined

  /**
   * 판 돈이 나오는 자리.
   *
   * **내놓은 그 물건의 자리입니다.** 판 가운데에서 동전이 솟으면 어느 것을 내놓아 들어온
   * 돈인지가 남지 않습니다 — 특히 바꿀 때는 들어온 것과 나간 것이 잇달아 일어나므로,
   * 둘이 서로 다른 자리에서 시작해야 갈립니다. 한 번 쓰면 비웁니다.
   */
  private sellFrom: { x: number; y: number } | undefined

  /**
   * 사서 오는 소모품 하나.
   *
   * **조커와 달리 뷰가 자기 것을 들고 있지 못합니다** — 소모품 칸은 화면을 다시 그릴
   * 때마다 새로 만들어지므로, 걸어 둔 셰이더가 그 자리에서 없어집니다. 그래서 화면이
   * 들고 있다가 그릴 때마다 다시 겁니다.
   */
  /** 소모품이 올 자리를 잡아 준 횟수와, 잡을 것이 없어 그냥 돌아온 횟수. */
  private flyAsked = 0
  private flyMissed = 0
  private itemArrive?: {
    uid: number
    warp: number
    glow: number
    filter: ArriveFilter
    /**
     * 산 자리.
     *
     * **소모품도 산 자리에서 옵니다.** 조커는 뷰가 용수철을 들고 있어서 날아오는데,
     * 소모품 칸은 화면을 다시 그릴 때마다 새로 만들어지므로 그럴 것이 없습니다 — 그래서
     * 제 칸에 툭 나타났습니다. 오는 동안의 어긋남을 화면이 들고 있다가 매 프레임 얹습니다.
     */
    from: { x: number; y: number }
    /** 오는 길을 얼마나 왔는가. 0 에서 1 로 갑니다. */
    travel: number
  }

  /**
   * 방금 「쓴다」를 누른 소모품.
   *
   * **쓴 것과 판 것을 가릅니다.** 화면은 소모품이 목록에서 없어진 것만 보므로 어느 쪽인지
   * 알 수 없습니다 — 쓴 것은 판 가운데로 나와 번쩍이고, 판 것은 제자리에서 탑니다.
   */
  private usedItem?: number

  /** 상점이 선 시각. 그것을 기준으로 각자의 차례가 정해집니다. */
  private shopRevealAt = 0
  /** 지금 상점이 서 있는가. 서지 않았다가 서는 그 한 번만 차례를 다시 셉니다. */
  private shopStanding = false
  /** 이번에 새로 선 것인가. 그때만 소리가 하나씩 납니다. */
  private shopOpening = false

  /**
   * 펼친 팩의 카드들.
   *
   * **낱장이 자기 용수철을 가집니다.** 한 장을 집으면 남은 것들이 새 자리로 미끄러져야
   * 하고, 매번 다시 만들면 그 자리에 순간이동합니다.
   */
  private readonly packViews = new Map<number, PackView>()
  /** 물러나는 중인 카드들. 집은 그 한 장이 옅어지며 커집니다. */
  private readonly packGone: { node: Container; life: number }[] = []
  /** 지금 그려진 팩. 바뀌면 처음부터 다시 폅니다. */
  private packShown = ''
  /** 덮개가 짙어진 정도. 0 에서 1 로 갑니다. */
  private packEnter = 0
  private packVeil?: Graphics
  private packNote?: Text
  /** 뜯은 팩의 이름. 덮개와 함께 들고 납니다. */
  private packTitle?: Text
  private packSkip?: Button
  /** 뜯은 딱지의 자리. 카드가 거기서 나옵니다. */
  private packFrom?: { x: number; y: number }
  private wasBusy = false
  private gameOverShown = false
  private gameOverPop = 0
  private gameOverBoard?: Container

  private shake = 0
  /**
   * 히트스톱. **때린 순간 시간이 잠깐 멈추면 그 한 방이 무거워집니다.**
   *
   * 멈추는 것은 연출의 시계뿐입니다 — 용수철과 파티클은 계속 움직여야 화면이 얼어붙은 것처럼
   * 보이지 않습니다.
   */
  private freeze = 0
  /** 이번 득점에서 몇 번째 사건인가. 소리의 음과 세기가 이것으로 올라갑니다. */
  private chain = 0
  /** 왼쪽 패널의 번쩍임. 숫자가 바뀌는 자리를 파티클 대신 이것이 알립니다. */
  private panelGlow = 0
  private panelTint: number = COLOR.ink
  /** 화면 전체의 번쩍임. 큰 것에만 씁니다. */
  private screenGlow = 0
  private panelDrawn = false
  private screenDrawn = false
  private screenTint: number = COLOR.ink
  /** 점수가 멈춘 뒤 낸 카드를 얼마나 붙잡아 두었는가. */
  private holdAfterScore = 0
  /**
   * 화면이 지금 주장하고 있는 것.
   *
   * **코어는 액션 하나를 끝까지 처리하고 답을 돌려줍니다.** 그 답을 그대로 그리면 카드가
   * 아직 날아가는 중에 최종 점수가 떠 있고, 다음 패가 이미 깔려 있고, 격파 보상이 이미
   * 들어와 있습니다 — 연출이 도는 의미가 없어집니다.
   *
   * 그래서 화면은 **박자가 도달한 데까지만** 압니다. 연출이 끝나면 상태와 같아집니다.
   */
  private shown = { score: 0, money: 0, hand: [] as number[] }
  /** 배경이 지금 그리고 있는 열기. 목표로 천천히 따라갑니다. */
  private heatShown = 0.1
  private clock = 0
  private pointerAt = { x: 0, y: 0 }
  /** 지금 설명이 떠 있는 조커. 바뀔 때만 다시 그립니다. */
  private hoveredJoker?: JokerView

  /**
   * 꾸욱 누르고 있는 것.
   *
   * **손가락으로 설명을 보는 길입니다.** 마우스는 올리면 뜨지만 손가락에는 「올린다」가
   * 없습니다 — 누른 자리와 시각을 적어 두고, 그 자리에서 오래 있으면 설명을 띄웁니다.
   */
  private press?: {
    at: number
    x: number
    y: number
    show: () => void
    /** 이미 띄웠는가. 한 번만 띄웁니다. */
    fired: boolean
  }
  /**
   * 꾸욱 눌러 설명을 띄웠는가.
   *
   * **그 손가락이 떼어질 때의 누름을 먹습니다.** 그러지 않으면 설명을 보려고 누른 것이
   * 그대로 고르기·사기·쓰기가 됩니다.
   */
  private pressAte = false
  /**
   * 지금 떠 있는 쪽지가 꾸욱 눌러 띄운 것인가.
   *
   * **떼어도 남아 있어야 합니다.** 손가락을 떼면 Pixi 가 「벗어났다」 를 내는데, 그것으로
   * 닫으면 누르고 있는 동안에만 보입니다 — 읽을 시간이 없습니다. 조커는 그 길을 타지 않아서
   * 남아 있었고, 소모품과 태그와 팩은 닫혔습니다.
   *
   * 다음에 무엇을 누르면 지워집니다.
   */
  private pressShown = false
  /**
   * 마지막으로 쓴 것이 손가락인가.
   *
   * **조커와 손패의 설명은 마우스의 자리를 보고 뜁니다.** 손가락은 뗀 뒤에도 그 자리가
   * 그대로 남으므로, 그 길로는 설명이 떼고 나서도 붙어 있습니다.
   */
  private touching = false

  /**
   * 이 런의 액션.
   *
   * **랭크 런을 올리려면 처음부터 끝까지가 필요합니다.** 이어하기도 같은 것을 쓰므로,
   * 상태를 저장하는 것보다 이것을 저장하는 편이 상태 구조가 바뀌어도 살아남습니다.
   */
  private actions: Action[] = []
  /** 이 런의 지표. 이벤트를 지나가며 쌓입니다. */
  private metrics: MetricsAcc = newMetrics()
  private readonly hub: LeaderboardHub
  private readonly login: LoginScene
  private readonly netStatus: NetStatus
  /** 끝난 판에 얹을 순위 한 줄. 판정이 오기 전에는 `undefined` 입니다. */
  private rankLine?: EndLine
  /** 그 줄을 그린 자리. 숫자가 굴러 내려가는 동안 여기에 다시 그립니다. */
  private rankNode?: Container
  /** 굴러 내려가는 중의 값. */
  private rankRoll = 0

  constructor(private readonly app: Application, private readonly data: Data, seed: string,
              pools: JokerPool[] = [JokerPool.Base]) {
    this.feel = readFeel(data.feel)
    this.audio = new Audio(data.tables)
    const first = this.setup()
    this.state = newRun(data, seed, first.deckId, first.stake, pools).state
    this.player = new TimelinePlayer(beat => this.showBeat(beat))
    this.hub = new LeaderboardHub(data, this.modals, this.toasts)
    this.netStatus = new NetStatus(this.toasts)
    this.title = new Title({
      onStart: () => this.enterRun(),
      onGuide: () => this.modals.open(this.guide),
      onOptions: () => this.openOptions(),
      onJokers: () => this.modals.open(this.jokerPool),
      onChallenges: () => this.openChallenges(),
      onLeaderboard: () => this.hub.openLeaderboard(),
      onRanked: () => void this.startRanked(),
      onSetup: () => this.openSetup(),
      onAccount: () => this.openAccount(),
      onSignOut: () => this.signOut(),
    })
    this.hub.onAccountChanged = () => this.syncAccount()
    this.hub.onNeedLogin = () => this.enterLogin()
    this.hub.onSignOut = () => this.signOut()
    this.login = new LoginScene()
    // **로그인 화면에서 고른 말이 옵션에도 남습니다.** 그러지 않으면 다음에 켤 때
    // 되돌아가고, 사람은 같은 것을 두 번 고르게 됩니다.
    this.login.onLanguage = language => {
      this.settings.language = language
      saveOptions(this.settings)
      this.applyOptions()
    }
    this.login.onSingle = () => {
      account.chooseSingle()
      this.enterTitle()
    }
    // 개발용 로그인은 제공자를 지난 것과 같은 자리입니다 — 내 것을 읽고 타이틀로 갑니다.
    this.login.onSignedIn = () => void this.hub.refresh().then(() => this.enterTitle())
    this.jokerPool = new JokerPoolPanel(data, this.settings.pool,
      () => this.modals.close(this.jokerPool))
    // **고른 것은 다음 판에 적용됩니다.** 도는 판의 풀을 바꾸면 상점이 이미 채워진
    // 뒤에 무엇이 나올 수 있었는가가 달라짐니다.
    this.jokerPool.onPick = (choice: PoolChoice) => {
      this.settings.pool = choice
      saveOptions(this.settings)
    }
    this.optionsPanel = new OptionsPanel(data, this.settings, () => this.applyOptions(),
      () => this.modals.close(this.optionsPanel))
    this.optionsPanel.onSeed = next => this.useSeed(next)

    this.setupPanel = new SetupPanel(data, this.setup(),
      () => this.modals.close(this.setupPanel))
    // **고른 그 자리에서 저장하고 단추의 글도 함께 맞춥니다.** 다음 판에 적용되는 것이므로
    // 도는 판은 건드리지 않습니다.
    this.setupPanel.onPick = (next: RunSetup) => {
      this.settings.deck = next.deckId
      this.settings.stake = next.stake
      saveOptions(this.settings)
      this.title.setSetupLabel(setupLabel(data, next))
    }
    // **고른 것으로 곧바로 판을 엽니다.** 챌린지 판과 같은 이유입니다 — 판을 닫고 시작을
    // 다시 누르게 하면 무엇으로 시작하는지가 두 화면에 걸쳐 있게 됩니다.
    this.setupPanel.onStart = (next: RunSetup) => {
      this.modals.close(this.setupPanel)
      this.challengeId = ''
      this.settings.deck = next.deckId
      this.settings.stake = next.stake
      saveOptions(this.settings)
      this.title.setSetupLabel(setupLabel(data, next))
      this.layRun(randomSeed())
      this.enterRun()
    }
    this.title.setSetupLabel(setupLabel(data, this.setup()))

    this.challengePanel = new ChallengePanel(data, this.challenges,
      () => this.modals.close(this.challengePanel))
    // **고른 챌린지로 곧바로 판을 엽니다.** 판을 닫고 시작을 다시 누르게 하면 무엇으로
    // 시작하는지가 두 화면에 걸쳐 있게 됩니다.
    // **저장을 읽은 뒤에 알립니다.** 타이틀의 단추가 잠겨 있어야 하는지는 그때 정해집니다.
    this.title.setChallengesOpen(this.challenges.unlocked)
    this.challengePanel.onStart = (challengeId: string) => {
      this.modals.close(this.challengePanel)
      this.challengeId = challengeId
      this.layRun(randomSeed())
      this.enterRun()
    }

    // 배경은 흰 스프라이트 한 장에 셰이더를 얹은 것입니다.
    this.sheet.filters = [this.background]
    this.backdrop.addChild(this.sheet)

    // **배경은 세계 밖에 있습니다.** 판은 기준 해상도에 맞춰 가운데에 놓이므로 창의 비율이
    // 다르면 옆이나 아래가 남습니다 — 그 자리를 검정으로 두지 않고 배경이 창 전체를 덮습니다.
    app.stage.addChild(this.backdrop, this.world)

    // **타이틀은 독립된 화면입니다.** 시작을 누르기 전에는 판도 조각들도 그리지 않습니다 —
    // 가려 두는 것과 없는 것은 다르고, 반투명한 판 뒤로 카드가 비치면 시작 전인지가
    // 흐려집니다.
    this.board.visible = false
    this.overlay.visible = false
    // **타이틀은 판 바깥입니다.** 판과 조각들을 통째로 끄고 그 위에 홀로 섭니다.
    this.recede.addChild(this.board, this.particles, this.overlay,
      this.coins, this.toasts, this.screenFlash, this.title)
    this.world.addChild(this.recede, this.modals, this.tooltip)

    // **내 카드가 계정 칩의 자리에 놓입니다.** 이름을 두 곳에 적으면 같은 것을 두 번 보게
    // 되고, 카드에는 순위까지 있으므로 칩이 남을 이유가 없습니다.
    this.title.accountSlot.addChild(this.hub.card)
    this.login.visible = false
    this.recede.addChild(this.login)

    // 통신 표시와 입력 막이. **판보다 위입니다.**
    this.world.addChild(this.netStatus)

    // 되돌아온 주소를 보고, 로그인되어 있으면 내 것을 읽습니다. 그다음에 어느 씬으로
    // 갈지가 정해집니다 — **로그인했거나 싱글플레이로 정했으면 타이틀입니다.**
    void this.hub.boot().then(() => this.openingScene())

    // 동전이 꽂힐 때마다 금액 칸이 튀고 음이 하나 올라갑니다.
    this.coins.onLand = (index, gain) => {
      this.audio.play(gain ? 'coin_land' : 'coin_lose', index * 2)
      this.money.target = this.state.money
      if (gain) this.flashPanel(COLOR.money, 0.5)
    }
    this.board.sortableChildren = true

    // **더미를 세우기 전에 뒷면을 정합니다.** `buildPanel` 이 덱 더미를 그리므로, 순서가
    // 거꾸로면 첫 화면의 더미만 첫 덱의 뒷면입니다.
    // **앞면을 먼저 정합니다.** 뒷면의 무늬를 세트가 정하므로 순서가 거꾸로면 첫 화면의
    // 더미만 덱의 무늬로 섭니다.
    setCardSet(setLookOf(data, this.settings.cardSet))
    const back = data.tables.deck.findByDeckId(this.state.deckId)
    if (back) setCardBack({ ...backLookOf(back), motif: cardBackMotif() ?? back.back })

    this.buildPanel()

    this.playButton = new Button(t('ui.button.play'), PLAY_W, PLAY_H, 0x2f6fb5, () => this.play())
    this.discardButton = new Button(t('ui.button.discard'), PLAY_W, PLAY_H, 0xa63f3f,
      () => this.discard())
    // **가운데 버튼이 곧 몇 장 골랐는가입니다.** 점 다섯을 따로 두면 같은 것을 두 곳에서
    // 세게 되고, 그 둘 사이를 눈이 오갑니다.
    this.clearButton = new Button('-', CLEAR_W, PLAY_H, 0x4a5568, () => this.clearSelection())
    this.primaryButton = new Button(t('ui.button.select_blind'), 210, 50, 0x2f6fb5, () => this.primary())
    this.skipButton = new Button(t('ui.button.skip'), 150, 38, 0x4a5568,
      () => {
        this.audio.play('blind_skip')
        this.act({ t: 'skip_blind' })
      })
    this.rerollButton = new Button(t('ui.button.reroll'), 128, 44, 0x3f5f8f, () => this.reroll())
    this.sortRankButton = new Button(t('ui.button.sort_rank'), 92, 32, 0x333e4e, () => this.sortHand('rank'))
    this.sortSuitButton = new Button(t('ui.button.sort_suit'), 92, 32, 0x333e4e, () => this.sortHand('suit'))
    // 위의 칸들과 같은 격자입니다 — 너비도 자리도.
    // **「족보 목록」 이 아니라 「런 정보」 입니다.** 족보는 그 안의 한 갈래가 되었습니다.
    this.infoButton = new Button(t('ui.run_info.title'), 124, 34, 0x3a4658,
      () => this.toggleHandList())
    this.menuButton = new Button(t('ui.button.menu'), 124, 34, 0x3a4658, () => this.openMenu())

    // **상점은 판 안에 섭니다.** 조커와 소모품 줄이 그 위로 지나가야 — 무엇을 가지고
    // 있는지를 보면서 사고, 산 것이 줄에 꽂히는 것도 보입니다.
    this.shopLayer.zIndex = -1
    this.board.addChild(this.shopLayer)

    this.overlay.addChild(this.playButton, this.discardButton, this.primaryButton,
      this.clearButton, this.skipButton, this.rerollButton, this.packLayer,
      this.sortRankButton, this.sortSuitButton, this.infoButton, this.menuButton,
      this.preview, this.blindPick, this.gameOver, this.heldBar)
    // **뜯은 팩은 판 위의 모든 것을 덮습니다.** 붙인 순서로만 두면 그 뒤에 붙는 버튼들이
    // 덮개 위로 올라옵니다 — 왼쪽 아래 버튼 둘이 팩을 뜯은 화면에 그대로 떠 있었습니다.
    this.overlay.sortableChildren = true
    this.packLayer.zIndex = 500
    // **고른 것의 단추는 그보다 위입니다.** 팩에서 집는 단추가 그 팩의 카드들 뒤로
    // 들어가 있었습니다 — 무엇을 고르는지를 그 카드들이 보여 주고, 집는 것은 그 위에서
    // 눌러야 합니다.
    this.heldBar.zIndex = 600

    this.preview.addChild(this.previewPlate, this.previewHand, this.previewValue)
    this.preview.visible = false
    this.gameOver.visible = false
    // 낸다 · 취소 · 버린다. **취소가 가운데인 것이 맞습니다** — 둘 중 어느 쪽으로도
    // 가기 전에 되돌리는 것이기 때문입니다.
    // **줄을 세어 가운데에 놓습니다.** 자리를 하나하나 적어 두면 버튼 크기를 고친 날에
    // 가운데가 어긋납니다.
    const row = splitX(
      box(BOARD_X - (PLAY_W * 2 + CLEAR_W + BUTTON_GAP * 2) / 2, BUTTON_Y,
        PLAY_W * 2 + CLEAR_W + BUTTON_GAP * 2, PLAY_H),
      [PLAY_W, CLEAR_W, PLAY_W], BUTTON_GAP)
    this.playButton.position.set(row[0].x, row[0].y)
    this.clearButton.position.set(row[1].x, row[1].y)
    this.discardButton.position.set(row[2].x, row[2].y)
    this.primaryButton.position.set(BOARD_X - 105, 520)
    this.skipButton.position.set(BOARD_X - 75, 586)
    this.rerollButton.position.set(BOARD_X - 64, 578)
    // 정렬 둘은 그 줄의 세로 가운데에 섭니다.
    this.sortRankButton.position.set(LEFT + PANEL_W + 30, BUTTON_Y + (PLAY_H - 32) / 2)
    this.sortSuitButton.position.set(LEFT + PANEL_W + 130, BUTTON_Y + (PLAY_H - 32) / 2)
    // **판의 밑단에 붙입니다.** 위에 두면 그 아래가 통째로 빈 자리로 남습니다 — 왼쪽 판은
    // 화면 아래 22픽셀까지 내려오고, 버튼은 그 안쪽에 있으면 됩니다.
    this.infoButton.position.set(LEFT, 726)
    this.menuButton.position.set(RIGHT_COL, 726)

    app.canvas.addEventListener('pointerdown', () => this.audio.unlock())
    // **누르는 순간 툴팁이 닫힙니다.** 툴팁은 마우스가 그것에서 벗어날 때 닫히는데, 누른
    // 것이 사라지면(사거나 팔거나 쓰거나) 벗어나는 일이 영영 없어서 그 자리에 남습니다.
    //
    // **꾸욱 누르기는 여기서 시작하지 않습니다** — 누른 것이 무엇인지는 그 물건이 알고,
    // 이 자리는 화면 전체라 무엇을 눌렀는지 모릅니다. 여기서는 앞의 것을 걷을 뿐입니다.
    //
    // **잡는 단계에서 받습니다.** 이벤트는 눌린 것에서 시작해 화면까지 올라오므로, 여기서
    // 그냥 받으면 그 물건이 방금 걸어 둔 꾸욱 누르기를 이 줄이 곧바로 걷어냅니다 —
    // 잡는 단계는 그 반대로 화면에서 물건으로 내려가므로 여기가 먼저입니다.
    app.stage.addEventListener('pointerdown', event => {
      this.touching = event.pointerType !== 'mouse'
      this.pressAte = false
      this.pressShown = false
      this.press = undefined
      this.tooltip.hide()
    }, { capture: true })
    app.stage.eventMode = 'static'
    app.stage.hitArea = { contains: () => true }
    app.stage.on('globalpointermove', event => {
      this.pointerAt = this.world.toLocal(event.global)
      if (event.pointerType === 'mouse') this.touching = false
      this.advanceDrag()
      this.advancePressMove()
    })
    // **판 밖에서 떼어도 끝나야 합니다.** 카드 위에서만 받으면 손가락이 판 밖으로 나간
    // 채 떼었을 때 그 카드가 커서에 붙어 남습니다.
    app.stage.on('pointerup', () => {
      this.endDrag()
      this.press = undefined
    })
    app.stage.on('pointerupoutside', () => {
      this.endDrag()
      this.press = undefined
    })
    window.addEventListener('keydown', event => {
      this.audio.unlock()
      // 판이 떠 있으면 맨 위의 것을 닫습니다. **연출을 넘기는 것보다 앞섭니다** — 판을
      // 보고 있는 사람에게 아무 키나는 「닫기」입니다.
      if (this.modals.busy) {
        if (event.key === 'Escape') this.modals.closeTop()
        return
      }
      // 고른 조커를 놓습니다. **판이 없을 때의 ESC 는 「무르기」입니다.**
      if (event.key === 'Escape' && this.held) {
        this.held = undefined
        this.refresh()
        return
      }
      if (this.player.busy) this.player.hurry(this.feel)
    })

    // 그림이 새로 들어오면 다시 그립니다. 문양이 그림으로 바뀝니다.
    onArtReady(() => {
      this.repaintPack()
      this.refresh()
    })

    // **카드가 다 닿은 뒤에 셉니다.** 카드는 실제 시계로 날아가고 연출은 배속과 히트스톱을
    // 타므로, 시간으로만 맞추면 배속을 올린 순간 어긋납니다.
    this.player.blocked = () => this.slams.length > 0 || this.clock < this.playLanded

    // **버튼과 판의 소리는 여기 한 자리입니다.** 부르는 쪽마다 걸면 새로 만드는 것에서
    // 반드시 하나가 빠지고, 그것만 소리 없이 눌립니다.
    Button.onPressed = () => this.audio.play('button')
    this.modals.onOpened = () => this.audio.play('panel_open')
    this.modals.onClosed = () => this.audio.play('panel_close')

    this.refresh()
    app.ticker.add(ticker => this.tick(ticker.deltaMS))

  }

  /**
   * 옵션이 정한 것을 화면에 겁니다.
   *
   * **여기 있는 것은 전부 실제로 무언가를 합니다.** 값만 저장하고 아무 데도 쓰지 않으면
   * 그것은 옵션이 아니라 장식입니다.
   */
  private applyOptions(): void {
    this.audio.muted = !this.settings.sound
    this.audio.volume = this.settings.volume / 100
    this.audio.music.muted = !this.settings.music
    this.audio.music.volume = this.settings.musicVolume / 100
    this.player.base = this.settings.speed
    this.particles.enabled = this.settings.particles

    // **말이 바뀌면 화면을 다시 그립니다.** 글은 그릴 때 한 번 읽히므로, 다시 그리지 않으면
    // 고른 그 순간에는 아무것도 바뀌지 않고 다음 판부터 바뀝니다.
    const want = chosen(this.settings)
    const changed = want !== language()
    setLanguage(want)
    if (changed) useFont(want)

    saveOptions(this.settings)
    // **고른 그 자리에서 갈아입습니다.** 다음 판까지 기다릴 이유가 없습니다 — 겉모습이므로
    // 도는 판의 규칙에 닿지 않습니다.
    setCardSet(setLookOf(this.data, this.settings.cardSet))
    // **뒷면도 함께 갈아입습니다.** 무늬를 세트가 정하므로, 여기서 다시 정하지 않으면
    // 앞면만 바뀌고 뒷면은 앞 세트의 무늬로 남습니다.
    this.syncCardBack()
    // 도움 표시는 켜고 끄는 그 자리에서 바로 사라져야 합니다.
    this.updateHints()
    this.syncCards()
    if (changed) this.relabel()
  }

  /**
   * 말이 바뀌었을 때 글을 다시 읽습니다.
   *
   * **`refresh` 로는 모자랍니다.** 그것은 매번 다시 그리는 것들을 그리고, 여기 있는 것들은
   * 만들 때 한 번 읽은 글을 그대로 들고 있습니다 — 칸의 이름, 왼쪽 아래 버튼, 타이틀.
   */
  private relabel(): void {
    this.score.caption = t('ui.slot.round_score')
    this.chips.caption = t('ui.slot.chips')
    this.mult.caption = t('ui.slot.mult')
    this.hands.caption = t('ui.slot.hands')
    this.discards.caption = t('ui.slot.discards')
    this.money.caption = t('ui.slot.money')
    this.anteSlot.caption = t('ui.slot.ante')

    this.playButton.text = t('ui.button.play')
    this.discardButton.text = t('ui.button.discard')
    this.primaryButton.text = t('ui.button.select_blind')
    this.skipButton.text = t('ui.button.skip')
    this.rerollButton.text = t('ui.button.reroll')
    this.sortRankButton.text = t('ui.button.sort_rank')
    this.sortSuitButton.text = t('ui.button.sort_suit')
    this.infoButton.text = t('ui.run_info.title')
    this.menuButton.text = t('ui.button.menu')

    this.title.relabel()
    this.login.relabel()
    this.hub.relabel()
    this.syncAccount()
    this.optionsPanel.relabel()
    this.guide.relabel()
    this.jokerPool.relabel()
    this.setupPanel.relabel()
    this.title.setSetupLabel(setupLabel(this.data, this.setup()))
    this.refresh()
  }

  /**
   * 타이틀에서 시작합니다.
   *
   * 게임 방법은 **처음 여는 사람에게만** 저절로 펼쳐집니다. 두 번째부터는 타이틀의 버튼과
   * 왼쪽 아래 버튼으로 엽니다.
   */
  /**
   * 옵션을 엽니다.
   *
   * **시드는 판 밖에서만 고칩니다.** 판이 돌기 시작하면 그 판의 시드이고, 도는 중에
   * 바꾸면 보고 있는 패와 적힌 시드가 어긋납니다.
   */
  private openOptions(): void {
    this.optionsPanel.setSeed(this.state.seed, this.scene === 'title')
    this.modals.open(this.optionsPanel)
  }

  /**
   * 이긴 것을 저장에 남깁니다.
   *
   * **챌린지 런이면 그 챌린지가 깬 것으로 들어갑니다.** 다음 하나가 열리는 것이 그
   * 목록의 길이로 정해지므로, 여기 적히지 않으면 20종이 다섯에서 멈춥니다.
   */
  private recordWin(): void {
    let changed = false
    if (!this.challenges.unlocked) {
      this.challenges.unlocked = true
      changed = true
    }
    if (this.challengeId !== '' && !this.challenges.beaten.includes(this.challengeId)) {
      this.challenges.beaten.push(this.challengeId)
      changed = true
    }
    if (changed) {
      saveProgress(this.challenges)
      this.title.setChallengesOpen(true)
    }
  }

  /**
   * 챌린지 판.
   *
   * **아직 안 열렸어도 판을 엽니다.** 처음에는 쪽지로 알렸는데, 쪽지가 서는 자리는 판
   * 안의 덱 옆이라 타이틀에서는 누른 곳과 먼 빈 구석에 떴습니다 — 알릴 것은 판 안에
   * 적히고, 20칸이 잠긴 채로 보이는 것이 무엇이 남았는지를 함께 알립니다.
   */
  /**
   * 덱과 스테이크를 고르는 판을 엽니다.
   *
   * **지금 저장된 것에 표시를 맞춰 엽니다** — 판을 한 번 세워 두고 다시 여는 것이므로,
   * 맞추지 않으면 처음 열 때의 자리에 표시가 남습니다.
   */
  private openSetup(): void {
    this.setupPanel.relabel()
    this.setupPanel.setSetup(this.setup())
    this.modals.open(this.setupPanel)
  }

  private openChallenges(): void {
    this.challengePanel.relabel()
    this.modals.open(this.challengePanel)
  }

  /**
   * 시드를 갈아 끼웁니다.
   *
   * **시드는 판 하나를 정하는 문자열입니다.** 덱 섞기 · 상점 · 팩 · 확률 발동이 저마다
   * 다른 난수 흐름을 쓰지만 그 흐름 전부가 이 문자열에서 갈라져 나오므로, 같은 시드는
   * 같은 판입니다.
   *
   * 시작하기 전에만 됩니다 — 판이 돌기 시작하면 그 판의 시드입니다.
   */
  private useSeed(seed: string): void {
    if (this.scene !== 'title') return
    this.layRun(seed)
  }

  /**
   * 시드 하나로 판을 새로 깝니다.
   *
   * **화면이 주장하던 것도 함께 맞춥니다** — 상태만 갈아 끼우면 화면은 앞 판의 점수와
   * 패를 그대로 들고 있습니다.
   */
  private layRun(seed: string): void {
    const setup = this.setup()
    this.state = newRun(this.data, seed, setup.deckId, setup.stake,
                        poolsOf(this.settings.pool), this.challengeId).state
    this.actions = []
    this.metrics = newMetrics()
    this.rankLine = undefined
    this.rankNode = undefined
    // **뒷면부터입니다.** 손패를 다시 그리기 전에 정해야, 새로 깔리는 카드가 이 판의
    // 뒷면으로 깔립니다.
    this.syncCardBack()
    this.settleShown()
    this.refresh()

    // 주소도 함께 바꿉니다. **그 주소를 열면 같은 판입니다** — 지금 페이지를 다시 읽지는
    // 않으므로 타이틀에 그대로 있습니다.
    try {
      history.replaceState(null, '',
        new URL(`?seed=${encodeURIComponent(seed)}`, location.href).toString())
      document.title = `clover — ${seed}`
    } catch {
      // 주소를 바꿀 수 없는 자리에서는 판만 바뀝니다.
    }
  }

  /**
   * 랭크 런을 시작합니다.
   *
   * **서버가 준 시드로만 시작합니다.** 시드를 고르게 두면 좋은 시드를 오프라인에서 찾아
   * 오는 것이 가능하고, 그것은 실력이 아니라 계산입니다.
   *
   * 받지 못하면 시작하지 않습니다 — 그냥 시작은 옆의 단추가 그대로 합니다.
   */
  private async startRanked(): Promise<void> {
    const seed = await this.hub.requestRanked({
      deck: 'red_deck',
      stake: 'White',
      pool: this.settings.pool,
    })
    if (seed === undefined) return

    this.challengeId = ''
    this.layRun(seed)
    this.enterRun()
    this.toasts.push(t('ui.lb.ranked'), t('ui.lb.ranked.on'), COLOR.good, 2.6)
  }

  /**
   * 부팅이 끝나고 처음 서는 자리.
   *
   * **묻지 않은 사람에게만 로그인 화면입니다.** 로그인했거나 싱글플레이로 정했으면
   * 곧바로 타이틀이고, 타이틀의 계정 칩이 다시 이 화면으로 보냅니다.
   */
  private openingScene(): void {
    this.syncAccount()
    if (account.mode() === 'undecided') this.enterLogin()
    else this.enterTitle()
  }

  /** 계정 상태를 화면에 알립니다. 로그인·로그아웃·이름 바꾸기 뒤에 부릅니다. */
  private syncAccount(): void {
    this.title.setAccount(this.hub.signedIn, this.hub.handle)
  }

  /** 계정 칩을 눌렀습니다. */
  private openAccount(): void {
    if (this.hub.signedIn) this.hub.openProfile()
    else this.enterLogin()
  }

  /**
   * 로그아웃합니다.
   *
   * **묻고 나서 합니다.** 한 번 눌러서 일어나면 잘못 누른 사람에게는 사고입니다.
   *
   * **끝나면 로그인 화면입니다.** 로그아웃은 「계정을 쓰지 않겠다」가 아니라 「이 계정에서
   * 나가겠다」이므로, 다음에 무엇으로 할지를 다시 정하는 자리로 갑니다 — 다시 켰을 때도
   * 로그인 화면인 것이 그 때문입니다.
   */
  private signOut(): void {
    const ask = new ConfirmPanel(
      t('ui.account.signOutAsk'), t('ui.account.signOutBody'), t('ui.button.logout'), false,
      () => void this.doSignOut(),
      () => this.modals.close(ask))
    this.modals.open(ask)
  }

  private async doSignOut(): Promise<void> {
    await this.hub.signOut()
    // **다시 묻습니다.** 「싱글플레이로 정했다」가 아니므로 그 표시를 지웁니다.
    account.askAgain()
    this.syncAccount()
    this.enterLogin()
  }

  /** 로그인 화면으로. **나가는 길은 「계정 없이 시작하기」 하나입니다.** */
  private enterLogin(): void {
    this.scene = 'login'
    // **알림이 서는 자리가 씬마다 다릅니다.** 판 안에서는 낸 카드를 덮지 않으려고
    // 오른쪽에 붙지만, 카드가 없는 화면에서는 그냥 구석에 붙은 것이 됩니다.
    this.toasts.setCenter(Toasts.OUT_RUN)
    this.login.visible = true
    this.title.visible = false
    this.board.visible = false
    this.overlay.visible = false
  }

  /**
   * 판으로 들어갑니다. **타이틀에서만 갑니다.**
   */
  private enterRun(): void {
    if (this.scene === 'run') return
    this.scene = 'run'
    this.toasts.setCenter(Toasts.IN_RUN)
    this.login.visible = false
    this.title.visible = false
    this.board.visible = true
    this.overlay.visible = true
    this.audio.unlock()
    this.applyOptions()
    this.settleShown()
    this.refresh()
    // **처음 값은 굴러가지 않습니다.** 판에 들어서는 순간의 금액은 「바뀐 것」이 아니라
    // 「원래 그런 것」이고, 0에서 세어 올라가면 무언가를 벌어들인 것으로 보입니다.
    this.money.reset(this.state.money)
    this.score.reset(0)
    this.chips.reset(0)
    this.mult.reset(0)

    try {
      if (localStorage.getItem('clover.guide.seen') === null) {
        this.modals.open(this.guide)
        localStorage.setItem('clover.guide.seen', '1')
      }
    } catch {
      // 저장소가 막힌 브라우저에서는 그냥 열지 않습니다.
    }
  }

  /**
   * 타이틀로 돌아갑니다.
   *
   * **페이지를 다시 읽지 않습니다.** 다시 읽으면 데이터·글꼴·그림을 처음부터 읽으므로
   * 로딩 씬이 한 번 더 보이는데, 판을 접는 것과 데이터를 읽는 것은 아무 관계가 없습니다 —
   * 접는 것은 `dropRun` 이 하고, 읽어 둔 것은 그대로 둡니다.
   */
  private enterTitle(): void {
    this.dropRun()
    this.scene = 'title'
    this.toasts.setCenter(Toasts.OUT_RUN)
    this.login.visible = false
    this.title.visible = true
    this.board.visible = false
    this.overlay.visible = false

    // **챌린지는 타이틀로 돌아갈 때 놓습니다.** 들고 있으면 타이틀의 시작 단추가 조용히
    // 챌린지를 여는 것이 됩니다.
    this.challengeId = ''

    // 새 판을 새 시드로 미리 깔아 둡니다. 타이틀에서 옵션을 열면 이 시드가 적혀 있고,
    // 시작을 누르면 이 판이 펼쳐집니다.
    this.useSeed(randomSeed())
  }

  /**
   * 판을 새로 시작합니다.
   *
   * **접고 나서 폅니다.** 끝난 판의 카드 한 장이 남아 있으면 그것이 새 판에 섞입니다 —
   * 접는 길은 타이틀로 가는 것과 같은 길이고, 다른 것은 곧바로 다시 편다는 것뿐입니다.
   */
  private restartRun(): void {
    this.enterTitle()
    this.enterRun()
  }

  /**
   * 판에 딸린 것을 전부 걷습니다.
   *
   * **상태를 새로 만드는 것으로는 모자랍니다.** 카드 뷰·조커 뷰·상점 딱지·날고 있는 칩은
   * 저마다 자기 목록에 있고, 상태를 갈아 끼워도 그 자리에 그대로 남습니다 — 여기서 빠뜨린
   * 것 하나가 곧 타이틀 위에 떠 있는 카드 한 장입니다.
   */
  private dropRun(): void {
    // 남은 박자를 버립니다. **`finish` 가 아닙니다** — 보여 줄 판이 이미 없습니다.
    this.player.drop()
    this.modals.closeAll()
    this.tooltip.hide()

    // 카드와 조커. 뷰는 `board` 의 자식이라 지워야 사라집니다.
    for (const view of this.cards.values()) view.destroy()
    this.cards.clear()
    for (const view of this.playedViews) view.destroy()
    this.playedViews.length = 0
    for (const view of this.jokers.values()) view.destroy()
    this.jokers.clear()
    for (const view of this.burning) view.destroy()
    this.burning.length = 0
    this.slams.length = 0
    this.fades.length = 0
    this.deals.length = 0
    // 돌아오는 중이던 카드들. **판을 접으면 갈 곳이 없습니다** — 덱째로 사라지므로,
    // 남겨 두면 타이틀 화면 오른쪽에 뒷면 몇 장이 떠 있습니다.
    for (const one of this.recalls) one.node.destroy()
    this.recalls.length = 0
    this.retired = 0
    // 떠오르던 차이 글. **글은 두고 상태만 되돌립니다** — 풀이므로 다시 쓰입니다.
    for (const one of this.deltas) one.node.visible = false
    this.panelShown = { hands: -1, discards: -1, ante: -1 }
    this.tagFlashId = ''
    this.tagFlashLife = 1
    this.tagSpent = []
    this.tagSpentAnte = 0
    this.tagFire.clear()

    // 판 위에 그려 둔 겹들. 매번 다시 그리는 것들이므로 비우면 됩니다.
    for (const layer of [this.shopLayer, this.packLayer, this.consumableLayer, this.tagLayer,
                         this.activeLayer, this.blindPick, this.gameOver, this.heldBar,
                         this.hint, this.payout.view, this.activePanel.view, this.deckView.view,
                         this.handList.view, this.menu.view]) {
      layer.removeChildren().forEach(child => child.destroy())
    }
    this.gameOver.visible = false

    // 날고 있는 것들.
    this.particles.clear()
    this.coins.clear()
    this.toasts.clear()

    // 고른 것 · 끄는 것 · 가리키는 것.
    this.selected.clear()
    this.hinted.clear()
    this.held = undefined
    this.drag = undefined
    this.hoveredJoker = undefined
    this.handHovered = -1
    this.handBand = undefined
    this.handPreview = undefined
    this.handRows.length = 0

    // 상점과 팩.
    this.packViews.clear()
    this.packGone.length = 0
    this.packShown = ''
    this.packEnter = 0
    this.packVeil = undefined
    this.packNote = undefined
    this.packSkip = undefined
    this.packTitle = undefined
    this.packFrom = undefined
    this.reveals.length = 0
    this.later.length = 0
    this.chimes.length = 0
    this.arriveFrom = undefined
    this.sellFrom = undefined
    this.shopRevealAt = 0
    this.shopStanding = false
    this.shopOpening = false

    // 소모품.
    this.itemArrive = undefined
    this.usedItem = undefined
    this.consumableLift.clear()
    this.consumableTiles.length = 0
    this.burningItems.length = 0

    // 정산.
    this.payoutRows.length = 0
    this.payoutNodes.length = 0
    this.payoutWait = undefined
    this.payoutWanted = false
    this.payoutOpen = false

    // 세고 있던 것들.
    this.playLanded = 0
    this.dealtUntil = 0
    this.blindEnter = 0
    this.blindShown = -1
    this.headlineLife = 0
    this.ratchet = 0
    this.build = 0
    this.fever = 0
    this.chain = 0
    this.holdAfterScore = 0
    this.wasBusy = false
    this.hintShown = ''

    // 번쩍임과 흔들림. **남겨 두면 타이틀이 흔들린 채로 섭니다.**
    this.shake = 0
    this.freeze = 0
    this.panelGlow = 0
    this.screenGlow = 0
    this.gameOverShown = false
    this.gameOverPop = 0
    this.gameOverBoard = undefined
  }

  // ---------------------------------------------------------------- 뼈대

  /**
   * 덱 더미를 그립니다.
   *
   * **다섯 장이 다 진짜 뒷면입니다.** 맨 위 한 장만 무늬를 그리고 아래 넉 장은 색만 칠한
   * 네모였습니다 — 옆구리만 보이니 무늬가 보이지 않는다는 이유였는데, 옆구리가 보인다는
   * 것은 그 옆구리에 테두리와 점선 띠가 있다는 뜻입니다. 색만 칠한 네모는 카드가 아니라
   * 카드 두께를 흉내 낸 무엇이고, 덱에서 나가는 카드와 덱으로 돌아오는 카드는 진짜 뒷면을
   * 들고 다니므로 더미만 다른 것을 쓰면 그 둘이 같은 카드로 보이지 않습니다.
   *
   * 매 프레임이 아니라 뒷면이 바뀔 때만 부릅니다.
   */
  private drawDeckPile(): void {
    this.deckPile.removeChildren().forEach(child => child.destroy())
    const look = cardBack()
    for (let i = 4; i >= 0; i--) {
      const sheet = new Container()
      sheet.position.set(DECK_X - SIZE.cardWidth / 2 + i * 2,
                         DECK_Y - SIZE.cardHeight / 2 - i * 3)
      drawCardBack(sheet, SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius, look)
      this.deckPile.addChild(sheet)
    }
  }

  /**
   * 이 판의 뒷면을 정합니다.
   *
   * **판이 시작될 때 한 번입니다.** 덱이 뒷면을 정하고 덱은 판이 도는 동안 바뀌지 않으므로,
   * 매 프레임 표를 뒤질 이유가 없습니다. 표에 없는 덱이면 첫 덱의 뒷면 그대로입니다 —
   * 뒷면이 없다고 판이 서지 못할 이유는 없습니다.
   */
  private syncCardBack(): void {
    const row = this.data.tables.deck.findByDeckId(this.state.deckId)
    // **무늬는 세트가, 색 두 개는 덱이 정합니다.** 덱이 정하는 것 중 한 판 내내 보이는
    // 것이 뒷면이므로, 세트가 그것을 통째로 가져가면 어느 덱으로 하고 있는지가 화면에서
    // 사라집니다 — 「붉은 덱 + 뼈의 궁정」은 붉은 룬 뒷면입니다.
    if (row) setCardBack({ ...backLookOf(row), motif: cardBackMotif() ?? row.back })
    this.drawDeckPile()
  }

  private buildPanel(): void {
    const panel = new Panel(PANEL_W + 24, SIZE.height - 44, 0x141b26)
    panel.position.set(LEFT - 12, 22)
    // **빈 자리는 상점 아래에 그립니다.** 상점이 판 안에 서므로, 이 테두리가 위에 있으면
    // 상점의 머리띠를 가로질러 빈 칸이 그려집니다.
    this.frames.zIndex = -2
    this.board.addChild(panel, this.frames)

    this.badge.position.set(LEFT, 34)
    this.score.position.set(LEFT, 210)
    // **칩 × 배수와 사이를 벌립니다.** 이 넷은 판이 도는 동안 가끔 보는 것이고 칩과 배수는
    // 매 순간 보는 것인데, 12픽셀을 사이에 두고 붙어 있으면 여섯 칸이 한 덩어리로 보여서
    // 그중 어느 둘이 지금 중요한지가 자리로 드러나지 않습니다.
    this.hands.position.set(LEFT, 424)
    this.discards.position.set(RIGHT_COL, 424)
    this.money.position.set(LEFT, 488)
    this.anteSlot.position.set(RIGHT_COL, 488)

    // **상자 둘과 그 사이의 곱셈표입니다.** 원작의 배치이고, 붙여 놓는 것보다 이 편이
    // 「칩 곱하기 배수」 라는 식으로 읽힙니다.
    const block = box(LEFT, CHIPS_Y, PANEL_W, CHIPS_H)
    const [chipsBox, gapBox, multBox] =
      splitX(block, [1, CHIPS_GAP / (PANEL_W - CHIPS_GAP) * 2, 1])
    this.paintScoreBox(chipsBox, multBox)
    this.chips.position.set(chipsBox.x, chipsBox.y)
    this.mult.position.set(multBox.x, multBox.y)

    // **곱셈표는 글자가 아니라 그림입니다.** 글꼴마다 `×` 의 굵기와 세로 자리가 달라서,
    // 글자로 두면 말을 바꿀 때마다 두 칸 사이에서 비뚤어집니다.
    //
    // **두 색이 만나는 자리에 딱지로 앉습니다.** 사이의 빈 자리에 글자 하나로 두면 어느
    // 칸의 것도 아닌 것이 되고, 그것이 애매하게 걸쳐 보이던 까닭입니다.
    const times = new Graphics()
    for (const angle of [Math.PI / 4, -Math.PI / 4]) {
      const dx = Math.cos(angle) * 8.5
      const dy = Math.sin(angle) * 8.5
      times.moveTo(-dx, -dy).lineTo(dx, dy)
        .stroke({ color: 0xdfe8f5, width: 4, cap: 'round' })
    }
    const seam = pointOf(gapBox, CENTER)
    times.position.set(seam.x, seam.y)

    // 불은 **바탕 위, 숫자 아래**입니다. 각자 자기 반에 갇혀서 그 반의 바탕이 타는 것으로
    // 보입니다 — 예전에는 칸 뒤에 두고 칸을 비치게 했고, 그러면 테두리 밖으로 자락이
    // 새어 나왔습니다.
    for (const [fire, area] of [[this.chipsFire, chipsBox], [this.multFire, multBox]] as const) {
      fire.blendMode = 'add'
      fire.visible = false
      // 뿌리가 상자의 윗변 안으로 조금 물리고, 거기서 위로 솟습니다.
      fire.position.set(area.x, area.y + FIRE_BITE - FIRE_RISE)
      fire.scale.set(area.width, FIRE_RISE)
      // **숫자 위로는 넘어오지 않습니다.** 물린 만큼까지만 보입니다.
      const mask = new Graphics()
      mask.rect(area.x, area.y - FIRE_RISE, area.width, FIRE_RISE + FIRE_BITE).fill(0xffffff)
      fire.mask = mask
      this.board.addChild(mask)
    }
    this.chipsFire.filters = [this.chipsFlame]
    this.multFire.filters = [this.multFlame]

    // 족보 이름. **불꽃 위입니다.**
    //
    // 위에서 아래로 라운드 점수 · 족보 이름 · 불꽃 · 칩 × 배수 순서이고, 눈이 한 번
    // 내려오면서 「이 판은 무슨 족보이고, 그래서 이만큼 타고 있고, 값은 이것이다」 로
    // 읽힙니다 — 아래에 두면 그 순서가 끊깁니다.
    // **위와 아래의 한가운데입니다.** 점수 칸 바로 밑에 붙어 있었고, 그러면 이 글이 그 칸에
    // 딸린 부제로 보입니다 — 이것은 점수의 설명이 아니라 지금 고른 것이 무엇인가입니다.
    // 점수 칸이 276에서 끝나고 칩 × 배수가 345에서 시작하므로 그 사이의 한가운데입니다.
    putText(this.handLabel, box(LEFT, 288, PANEL_W, 24), CENTER)

    // **딱지 아래의 것들은 한 통에 담습니다.** 블라인드 딱지는 들고 있는 태그만큼 자라고,
    // 그러면 그 아래가 통째로 내려가야 합니다 — 낱개로 자리를 다시 세면 여섯 곳을 고쳐야
    // 하고 그중 하나를 빠뜨리면 그것만 겹칩니다.
    this.panelStack.addChild(this.score, this.handLabel, this.scoreBox,
      this.chipsFire, this.multFire, this.chips, this.mult, times,
      this.hands, this.discards, this.money, this.anteSlot)
    this.board.addChild(this.badge, this.panelStack)

    // 가운데에서 커집니다. 위쪽을 붙잡고 키우면 글씨가 아래로 자라 보입니다.
    this.headline.anchor.set(0.5, 0.5)
    this.headline.position.set(BOARD_X, 214)

    // **칸 아래입니다.** 위에 두면 줄과 화면 위쪽 사이가 좁아 글이 끼어 있는 것으로
    // 보이고, 아래에 두면 줄에 딸린 것으로 읽힙니다.
    this.jokerCount.anchor.set(0.5, 0)
    this.jokerCount.position.set(
      JOKER_X + (SIZE.jokerWidth + 12) * 2, JOKER_Y + SIZE.jokerHeight / 2 + 9)
    this.consumableCount.anchor.set(0.5, 0)
    this.consumableCount.position.set(
      CONSUMABLE_X + (SIZE.jokerWidth + 12) / 2, JOKER_Y + SIZE.jokerHeight / 2 + 9)

    this.deckLabel.anchor.set(0.5, 0)
    this.deckLabel.position.set(DECK_X, DECK_Y + 76)

    const pile = this.deckPile
    this.drawDeckPile()

    // **지시문은 누를 버튼 바로 위입니다.** 패널 아래에 두면 눈이 화면 왼쪽 끝까지 갔다
    // 와야 하고, 정작 누를 것은 가운데에 있습니다.
    this.hint.position.set(BOARD_X, BUTTON_Y - 30)

    // **덱은 판이 도는 동안만 화면에 있습니다.** 상점에서는 오른쪽으로 밀려 나가고,
    // 다음 블라인드로 가면 다시 들어옵니다 — 상점의 물건과 자리를 다투지 않습니다.
    // **덱을 누르면 남은 카드가 보입니다.** 그것을 여는 버튼을 따로 두면 판 아래가
    // 복잡해지고, 정작 눌러야 할 것은 화면에 이미 그려져 있습니다.
    pile.eventMode = 'static'
    pile.cursor = 'pointer'
    // 꾸욱 눌러 설명을 본 것이면 열지 않습니다. 누름을 다루는 자리마다 `ate` 를 먼저
    // 물어봅니다 — 그러지 않으면 설명을 보려던 손가락이 그대로 판을 엽니다.
    pile.on('pointertap', () => {
      if (this.ate()) return
      this.toggleDeckView()
    })
    this.tipOn(pile, () => {
      this.tooltip.show(t('ui.button.deck_view'), '', 0, [t('ui.deck.tip')],
        DECK_X, DECK_Y + 96, SIZE)
    })

    this.deckLayer.addChild(pile, this.deckLabel)

    // **고른 것의 단추는 판이 아니라 그 위의 층입니다.** 판에 두면 뜯은 팩이 판 전체를
    // 덮으므로 그 팩에서 집는 단추가 자기가 덮은 것 뒤로 들어갑니다.
    this.board.addChild(this.deckLayer, this.headline, this.gauge, this.jokerCount,
      this.consumableCount, this.consumableLayer, this.tagLayer, this.activeLayer,
      this.hint, this.panelFlash)
  }

  /** 조커와 소모품의 빈 자리. **비어 있어도 자리가 보여야 무엇을 모으는 게임인지 압니다.** */
  /**
   * 칩과 배수의 상자.
   *
   * **깔끔한 단색 둘입니다.** 숫자가 앉는 자리이므로 그 자리는 조용해야 하고, 불은 이 상자
   * **위에** 붙습니다 — 안에서 태우면 불이 숫자를 덮어 아무것도 읽히지 않습니다.
   */
  private paintScoreBox(chipsBox: Box, multBox: Box): void {
    const g = this.scoreBox
    g.clear()
    for (const [area, tint] of [[chipsBox, COLOR.chips], [multBox, COLOR.mult]] as const) {
      // 짙게 눌러 씁니다. **원색 그대로는 흰 숫자가 눌러앉지 못합니다.**
      // **단색 하나입니다.** 광택이나 그라디언트를 얹으면 그 위에 앉는 흰 숫자가 자리마다
      // 다른 바탕을 만나 흐릿해집니다 — 숫자가 앉는 자리는 조용해야 합니다.
      g.roundRect(area.x, area.y, area.width, area.height, CHIPS_R)
        .fill(mix(tint, 0x0a1018, 0.52))
    }
  }

  private drawFrames(): void {
    const g = this.frames
    g.clear()

    for (let i = 0; i < this.state.rules.jokerSlots; i++) {
      const x = JOKER_X + i * (SIZE.jokerWidth + 12)
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .fill({ color: 0x161d29, alpha: 0.6 })
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .stroke({ color: COLOR.panelEdge, width: 1.5, alpha: 0.8 })
    }

    for (let i = 0; i < this.state.rules.consumableSlots; i++) {
      const x = CONSUMABLE_X + i * (SIZE.jokerWidth + 12)
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .fill({ color: 0x1d1a2c, alpha: 0.6 })
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .stroke({ color: 0x5a4d80, width: 1.5, alpha: 0.9 })
    }
  }

  layout(width: number, height: number): void {
    const scale = Math.min(width / SIZE.width, height / SIZE.height)
    this.world.scale.set(scale)
    // 자리를 정수로 맞춥니다. 반 픽셀이 남으면 글씨가 흐려집니다.
    this.world.position.set(
      Math.round((width - SIZE.width * scale) / 2),
      Math.round((height - SIZE.height * scale) / 2))

    this.sheet.width = width
    this.sheet.height = height
    this.background.setAspect(width / Math.max(1, height))
    this.sharpen(scale)
  }

  /**
   * 글씨를 화면 배율에 맞춰 다시 굽습니다.
   *
   * **월드를 통째로 확대하므로 글씨가 그대로면 뿌옇습니다.** 글씨는 한 번 그림으로 구워서
   * 쓰는 것이라, 구울 때의 배율이 화면 배율보다 작으면 늘려 놓은 그림이 됩니다.
   */
  private sharpen(scale: number): void {
    const want = Math.min(3, Math.max(1, scale) * (this.app.renderer.resolution ?? 1))
    const walk = (node: Container) => {
      if (node instanceof Text && node.resolution !== want) node.resolution = want
      for (const child of node.children) walk(child as Container)
    }
    walk(this.world)
    this.textScale = want
  }

  private textScale = 1

  // ---------------------------------------------------------------- 액션

  private act(action: Action): void {
    if (this.player.busy) return
    // 무엇이 일어나면 가리키던 것이 그대로 있으리라는 보장이 없습니다.
    this.tooltip.hide()
    const before = this.shown.hand
    const step = apply(this.data, this.state, action)
    // **코어를 지난 액션만 적습니다.** 화면이 막은 것은 런에 들어가지 않았습니다.
    this.actions.push(action)
    observe(this.metrics, step.events)
    this.rewind(step.events, before)
    this.announce(step.events)
    this.startTimeline(step.events)
    this.refresh()
  }

  /**
   * 이 액션이 낸 이벤트들을 되짚어, **연출이 아직 도달하지 않은 것을 화면에서 뺍니다.**
   *
   * 점수와 금액은 늘어난 만큼 되돌리고, 패는 뽑기 전의 모습으로 되돌립니다. 그다음은 박자가
   * 하나씩 도로 채웁니다.
   */
  private rewind(events: readonly GameEvent[], before: readonly number[]): void {
    let money = this.state.money
    let score = Number(this.state.score)
    const drawn = new Set<number>()

    for (const event of events) {
      switch (event.t) {
        case 'MoneyChanged': if (!paidNow(event.reason)) money -= event.delta; break
        case 'ScoreResolved': score -= event.score; break
        case 'HandDrawn': for (const uid of event.uids) drawn.add(uid); break
        default: break
      }
    }

    this.shown = {
      money,
      score,
      // **누르기 전의 패 그대로입니다.** 뽑은 것만 뺍니다 — 낸 것은 박자가 도달할 때
      // 물러나고, 남은 것은 계속 손에 있어야 합니다.
      //
      // 코어의 패를 보고 정하면 **마지막 핸드에서 남은 카드가 즉시 사라집니다.** 그 한
      // 판으로 격파하면 코어가 라운드를 끝내며 패를 비우는데, 화면은 아직 카드가 날아가는
      // 중이고 득점도 시작하지 않았습니다.
      hand: before.filter(uid => !drawn.has(uid)),
    }
  }

  /** 연출이 끝났습니다. 화면이 주장하는 것을 상태와 맞춥니다. */
  private settleShown(): void {
    this.deals.length = 0
    this.dealtUntil = 0
    this.shown = {
      money: this.state.money,
      score: Number(this.state.score),
      hand: this.state.hand.slice(),
    }
  }

  /**
   * 무엇이 일어났는지 글로 알립니다.
   *
   * **소모품은 결과가 화면 여러 곳에 흩어집니다** — 카드가 바뀌고 족보 레벨이 오르고 조커가
   * 사라지는데, 그것들이 각자의 자리에서 조용히 바뀌면 무엇을 쓴 것인지 남지 않습니다.
   *
   * 같은 갈래는 묶어서 한 줄로 냅니다. 카드 5장이 바뀌었다고 토스트가 5개 뜨면 읽을 수
   * 없습니다.
   */
  private announce(events: readonly GameEvent[]): void {
    let modified = 0
    let destroyed = 0
    let added = 0

    for (const event of events) {
      switch (event.t) {
        case 'ConsumableUsed': {
          const kind = this.state.consumables.find(item => item.id === event.id)?.kind
          const name = this.consumableName(kind ?? 1, event.id)
          this.toasts.push(tf('ui.toast.used', { name }),
            this.consumableLines(kind ?? 1, event.id).join(' · ') || t('ui.note.applied'),
            0xb9a8ff, 3)
          break
        }

        case 'HandLevelled':
          this.toasts.push(tf('ui.hand.level', { name: this.handName(event.hand), level: event.level }),
            t('ui.hand.leveled'), COLOR.chips, 2.8)
          break

        // **태그를 받은 것이 보여야 합니다.** 받은 것이 화면 어디에도 나타나지 않으면
        // 건너뛴 대가가 없는 것으로 보입니다.
        case 'TagGained': {
          const lines = describe(this.data, this.data.tagEffects.get(event.tagId) ?? [])
          this.toasts.push(nameOf(this.data, 'tag', event.tagId, event.tagId),
            lines.join(' · ') || t('ui.note.applied'), COLOR.accentTerm, 3.2)
          // **조각을 터뜨리지 않습니다.** 받은 자리를 짚어 터뜨렸는데, 태그가 실제로 서는
          // 자리는 머리띠의 오른쪽 끝이라 그 둘이 늘 어긋났습니다 — 새로 선 칩 자체가
          // 한 번 하얗게 번쩍이는 편이 어디에 생겼는지를 그대로 알립니다.
          this.tagFlashId = event.tagId
          this.tagFlashLife = 0
          // **받은 것은 무조건 보입니다.** 태그는 둘로 갈리는데 — 상점에서 도는 것은 들고
          // 있고, 그 자리에서 도는 것은 받자마자 쓰이고 목록에서 빠집니다 — 뒤엣것은 들고
          // 있는 목록만 보고 그리면 **한 프레임도 화면에 서지 못합니다.** 받았다는 것은
          // 보여야 하므로, 목록에서 빠진 뒤에도 잠깐 남겨 둡니다.
          this.audio.play('joker_add', 4)
          break
        }

        // **쓰인 태그도 보여야 합니다.** 태그는 둘로 갈립니다 — 상점에 들어갈 때 도는
        // 것은 들고 있다가 그때 돌지만, 그 자리에서 도는 것은 받자마자 쓰이고 사라집니다.
        // 그 사라짐을 알리지 않으니 둘을 건너뛰고 하나만 남은 것으로 보였습니다.
        case 'TagUsed': {
          // **쓴 것도 남깁니다.** 지우면 그 자리에서 쓰이는 태그는 아무것도 뜨지 않은 채로
          // 지나가고, 무엇을 받았는지가 화면에 남지 않습니다.
          if (!this.tagSpent.includes(event.tagId)) this.tagSpent.push(event.tagId)
          // 받자마자 쓰이는 태그는 나오는 번쩍임이 아직 도는 중입니다. 그것이 잦아든
          // 뒤에 켭니다.
          this.tagFire.set(event.tagId, this.tagFlashId === event.tagId ? -TAG_FIRE_WAIT : 0)
          this.toasts.push(nameOf(this.data, 'tag', event.tagId, event.tagId),
            t('ui.note.applied'), COLOR.good, 2.6)
          this.audio.play('joker_add', 8)
          break
        }

        case 'JokerDestroyed': {
          this.toasts.push(tf('ui.toast.destroyed',
            { name: nameOf(this.data, 'joker', event.jokerId, event.jokerId) }),
          t('ui.note.joker_slot_free'),
            COLOR.bad, 2.6)
          break
        }

        // **무엇이 어떻게 바뀌었는가**가 두 줄입니다. 「규칙이 바뀌었습니다」와 식별자
        // 하나로는 무엇을 얻은 것인지 알 수 없습니다.
        case 'RuleChanged':
          this.toasts.push(this.ruleName(event.rule), ruleChange(event), COLOR.money, 2.8)
          break

        case 'CardModified': modified++; break
        case 'CardDestroyed': destroyed++; break
        case 'CardAdded': added++; break
        default: break
      }
    }

    if (modified > 0) {
      this.toasts.push(tf('ui.toast.cards_changed', { n: modified }), t('ui.deck.changed'),
        COLOR.good, 2.4)
    }
    if (destroyed > 0) {
      this.toasts.push(tf('ui.toast.cards_destroyed', { n: destroyed }), t('ui.deck.removed'), COLOR.bad, 2.4)
    }
    if (added > 0) {
      this.toasts.push(tf('ui.toast.cards_added', { n: added }), t('ui.deck.added'), COLOR.good, 2.4)
    }
  }

  private primary(): void {
    this.audio.play(this.state.phase === 'blind-select' ? 'blind_select' : 'shop_enter')
    if (this.state.phase === 'blind-select') this.act({ t: 'select_blind' })
    else if (this.state.phase === 'shop') this.act({ t: 'leave_shop' })
  }

  private reroll(): void {
    this.audio.play('shop_reroll')
    this.act({ t: 'reroll' })
  }

  private play(): void {
    if (this.selected.size === 0 || this.player.busy) return
    const cards = this.orderedSelection()
    this.selected.clear()
    // **카드를 올리는 것도 박자입니다.** 여기서 올리고 득점을 따로 세면 둘의 간격이 코드에
    // 고정되고, `Const_Feel` 을 고쳐도 화면이 바뀌지 않습니다.
    this.act({ t: 'play', cards })
  }

  private discard(): void {
    if (this.selected.size === 0 || this.player.busy) return
    const cards = this.orderedSelection()
    this.selected.clear()
    // 버리는 것도 한 장씩입니다. **한 덩어리로 사라지면 몇 장을 버렸는지가 남지 않습니다.**
    this.act({ t: 'discard', cards })
  }

  /** 고른 카드를 패의 순서대로. **낸 순서가 득점 순서입니다.** */
  private orderedSelection(): number[] {
    return this.state.hand.filter(uid => this.selected.has(uid))
  }

  /** 패를 정렬합니다. **낼 것을 고르는 일이 훨씬 쉬워집니다.** */
  private clearSelection(): void {
    if (this.selected.size === 0 || this.player.busy) return
    this.selected.clear()
    this.audio.play('card_select', -6)
    this.refresh()
  }

  private sortHand(by: 'rank' | 'suit'): void {
    if (this.player.busy) return
    const cards = this.state.hand
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    cards.sort((a, b) => by === 'rank'
      ? b.rank - a.rank || a.suit - b.suit
      : a.suit - b.suit || b.rank - a.rank)

    this.state.hand = cards.map(card => card.uid)
    // **화면이 그리는 것은 `shown.hand` 입니다.** 연출이 도달한 것만 그리기 위한 것이라,
    // 정렬이 그것을 함께 바꾸지 않으면 자리가 하나도 움직이지 않습니다.
    //
    // 화면에 이미 있는 것만 그 차례로 다시 세웁니다 — 아직 날아오는 중인 카드를 여기서
    // 끌어오면 뽑는 연출이 끊깁니다.
    const seen = new Set(this.shown.hand)
    this.shown.hand = this.state.hand.filter(uid => seen.has(uid))

    this.audio.play('card_select')
    this.refresh()
  }

  /** 족보 목록을 열고 닫습니다. */
  private toggleHandList(): void {
    if (this.modals.has(this.handList)) {
      this.modals.close(this.handList)
      return
    }
    this.drawHandList()
    this.modals.open(this.handList)
  }

  /** 남은 카드를 열고 닫습니다. */
  private toggleDeckView(): void {
    if (this.modals.has(this.deckView)) {
      this.modals.close(this.deckView)
      return
    }
    this.drawDeckView()
    this.modals.open(this.deckView)
  }

  private toggle(uid: number): void {
    if (this.player.busy) return
    if (this.selected.has(uid)) this.selected.delete(uid)
    else if (this.selected.size < this.data.run.maxPlayedCards) this.selected.add(uid)
    this.audio.play('card_select')
    this.refresh()
  }

  /**
   * 낸 카드를 판으로 올립니다.
   *
   * **한꺼번에 움직이지 않습니다.** 왼쪽부터 한 장씩 차례로, 빠르게 가서 자리에 달라붙습니다 —
   * 다섯 장이 같이 미끄러지면 무엇을 냈는지가 한 덩어리로 보이고, 하나씩 「짝」 붙으면
   * 다섯 번의 사건이 됩니다.
   */
  private liftToPlayArea(uids: number[]): void {
    const spacing = SIZE.cardWidth + 16
    const startX = BOARD_X - ((uids.length - 1) * spacing) / 2

    uids.forEach((uid, index) => {
      const view = this.cards.get(uid)
      if (!view) return
      this.cards.delete(uid)
      this.playedViews.push(view)
      view.eventMode = 'none'
      view.hovered = false
      view.selected = false
      view.setPick(0, PICK_TINT)
      view.hint = false
      view.idle = 0.4
      view.zIndex = 100 + index
      this.slams.push({
        view, x: startX + index * spacing,
        at: this.clock + index * (this.feel.playStaggerMs / 1000),
      })
    })
  }

  /**
   * 버린 카드를 한 장씩 내보냅니다.
   *
   * **곧바로 지우지 않습니다** — 사라지는 것이 보여야 몇 장을 버렸는지가 남습니다.
   *
   * **낸 카드가 물러나는 것과 같은 몸짓입니다.** 버리는 것과 득점하고 물러나는 것은 다음에
   * 일어나는 일이 다르지만 화면에서 하는 일은 하나입니다 — 그 카드가 이 판에서 없어지는
   * 것입니다. 나가는 자리도, 나가는 길도, 조각을 흩는지도 같습니다.
   */
  private throwAway(uids: readonly number[]): void {
    uids.forEach((uid, index) => {
      const view = this.cards.get(uid)
      if (!view) return
      this.cards.delete(uid)
      this.playedViews.push(view)
      view.eventMode = 'none'
      view.hovered = false
      view.selected = false
      view.setPick(0, PICK_TINT)
      view.hint = false
      // **제자리에서 곧바로 나갑니다.** 판 가운데로 한 번 올려 보냈는데, 그러면 버린 카드가
      // 낸 카드처럼 판에 올라섰다가 없어지는 것으로 보입니다 — 버리는 것은 그 자리에서
      // 화면 밖으로 치우는 것입니다.
      this.fades.push({
        view, at: this.clock + index * (this.feel.playStaggerMs / 1000),
      })
    })
  }

  /** 예약해 둔 한 장씩의 깔기. */
  private advanceDeals(): void {
    // **끝난 판에는 깔지 않습니다.** 다음 패는 득점 연출이 끝난 뒤에 한 장씩 깔리는데,
    // 그 예약이 이미 잡혀 있는 채로 판이 끝날 수 있습니다 — 그러면 「패배」 판이 선 뒤에
    // 그 밑으로 새 패가 마저 깔립니다. 코어는 진 판의 손패를 비우지 않으므로 화면이
    // 걷어야 하고, 걷은 다음에 깔리면 걷은 것이 헛일이 됩니다.
    if (this.state.phase === 'lost' || this.state.phase === 'won') {
      if (this.deals.length === 0 && this.shown.hand.length === 0) return
      this.deals.length = 0
      this.shown.hand = []
      this.refresh()
      return
    }

    let dealt = false
    while (this.deals.length > 0 && this.deals[0].at <= this.clock) {
      const next = this.deals.shift()
      if (!next) break
      this.shown.hand = [...this.shown.hand, next.uid]
      dealt = true
    }
    if (!dealt) return
    this.dealtUntil = this.clock + 0.34
    this.refresh()
  }

  /**
   * 나갔던 카드들이 덱으로 돌아옵니다.
   *
   * **한 판을 도는 동안 카드는 나가기만 했습니다.** 낸 것도 버린 것도 오른쪽 화면 밖으로
   * 빠지고 그것으로 끝이라, 덱은 줄기만 하고 다음 블라인드의 첫 패가 어디에서 오는지가
   * 화면에 없었습니다 — 카드는 없어진 것이 아니라 덱으로 돌아간 것입니다.
   *
   * **아주 빠릅니다.** 이것은 볼 것이 아니라 셈이 맞는다는 표시입니다: 눈이 따라갈 만큼
   * 느리면 격파한 뒤의 그 한숨이 카드 세는 시간이 되고, 그 자리에 서야 할 것은 정산입니다.
   * 스무 장이 0.4초 안에 다 들어옵니다.
   *
   * 돌아오는 것은 뒷면입니다 — 어느 카드가 어느 자리로 가는지는 아무도 세지 않으므로,
   * 얼굴을 그리는 것은 그리는 값만 치르고 아무것도 알리지 않습니다.
   */
  private recallToDeck(): void {
    const many = this.retired
    this.retired = 0
    if (many === 0) return

    for (let i = 0; i < many; i++) {
      const sheet = new Container()
      drawCardBack(sheet, SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius, cardBack())
      sheet.pivot.set(SIZE.cardWidth / 2, SIZE.cardHeight / 2)

      const motion = new Motion()
      // 나갔던 그 자리에서 돌아옵니다. **한 줄로 오면 한 장이 길어진 것으로 보입니다** —
      // 나갈 때 흩어졌던 만큼 높이를 벌려 둡니다.
      motion.snap(SIZE.width + 130, PLAY_Y + ((i % 5) - 2) * 11)
      motion.rotation.snap(((i % 3) - 1) * 7)
      motion.hard()
      sheet.position.set(motion.x.value, motion.y.value)
      sheet.zIndex = 60 + i

      // 덱과 같은 층입니다. 덱이 물러나기 시작해도 돌아오는 카드가 그것을 따라갑니다 —
      // 판이 끝나면 덱은 오른쪽으로 빠지는데, 층이 다르면 카드만 빈자리로 들어갑니다.
      this.deckLayer.addChild(sheet)
      this.recalls.push({ node: sheet, motion, at: this.clock + i * 0.018, sent: false })
    }
  }

  /**
   * 고른 상점 칸이 들리는 것.
   *
   * **누른 것이 올라와야 골랐다는 것이 됩니다.** 단추가 그 밑에 서는 것만으로는 어느 칸을
   * 고른 것인지가 단추의 자리로만 읽히고, 칸 자체는 아무 일도 없었던 것처럼 남습니다 —
   * 조커와 소모품을 고를 때와 같은 몸짓이고, 같은 용수철입니다.
   */
  private advanceShopLift(seconds: number): void {
    this.shopLift.target = this.held?.kind === 'shop' || this.held?.kind === 'pack_slot' ? 10 : 0
    this.shopLift.advance(seconds)
    this.hub.advance(seconds)
    this.login.advance(seconds)
    this.netStatus.advance(seconds)
    this.rollRank(seconds)
    const lift = this.shopLift.value

    // 표를 지우는 자리와 여기가 갈라져 있으므로 한 겹 더 막습니다 — 지워진 것의 자리를
    // 만지면 그 프레임의 나머지가 통째로 죽습니다.
    // **단추가 값을 대신합니다.** 고른 칸에는 그 밑에 「산다」 가 서는데, 값이 그대로
    // 남아 있으면 단추 위로 그 값이 삐죽 보입니다 — 둘은 같은 자리의 것이고, 값을 보고
    // 고른 다음에 필요한 것은 살지 말지뿐입니다.
    for (const [slot, one] of this.shopTiles) {
      if (one.tile.destroyed) continue
      const here = this.held?.kind === 'shop' && this.held.uid === slot
      one.tile.y = one.baseY - (here ? lift : 0)
      one.price.visible = !here
    }
    for (const [slot, one] of this.packSlotTiles) {
      if (one.tile.destroyed) continue
      const here = this.held?.kind === 'pack_slot' && this.held.uid === slot
      one.tile.y = one.baseY - (here ? lift : 0)
      one.price.visible = !here
    }
  }

  /**
   * 새로 선 태그가 번쩍이는 것.
   *
   * **매 프레임 딱지를 다시 그립니다.** 칩은 통이 매번 새로 만들어지므로 셰이더를 그 통에
   * 붙여 두고 잦아들게 할 수가 없습니다 — 세기를 여기서 세고, 칩을 만드는 쪽이 그 값을
   * 읽어 붙입니다.
   */
  private advanceTagFlash(seconds: number): void {
    if (this.tagFlashLife >= 1 && this.tagFire.size === 0) return

    if (this.tagFlashLife < 1) {
      this.tagFlashLife = Math.min(1, this.tagFlashLife + seconds / TAG_FLASH)
    }
    for (const [tagId, life] of this.tagFire) {
      const next = life + seconds / TAG_FIRE
      if (next >= 1) this.tagFire.delete(tagId)
      else this.tagFire.set(tagId, next)
    }
    // 번쩍이거나 발동하는 동안은 매 프레임 다시 그립니다.
    this.badge.setTags(this.tagChips())
  }

  /** 돌아오는 카드들. 덱에 닿은 것부터 지웁니다. */
  private advanceRecalls(seconds: number): void {
    for (let i = this.recalls.length - 1; i >= 0; i--) {
      const one = this.recalls[i]
      if (one.at > this.clock) continue

      if (!one.sent) {
        one.sent = true
        one.motion.to(DECK_X, DECK_Y, 0)
        one.motion.scale.target = 0.92
      }
      one.motion.advance(seconds)
      one.node.position.set(one.motion.x.value, one.motion.y.value)
      one.node.rotation = one.motion.rotation.value * (Math.PI / 180)
      one.node.scale.set(one.motion.scale.value)

      // 덱에 닿았습니다. **소리는 몇 장에 한 번입니다** — 스무 장이 저마다 소리를 내면
      // 그것은 카드가 쌓이는 소리가 아니라 잡음입니다.
      if (one.motion.x.value > DECK_X + 12) continue
      this.recalls.splice(i, 1)
      one.node.destroy()
      if (i % 4 === 0) this.audio.play('card_flip', 6 + (i % 5) * 2)
    }
  }

  /** 예약해 둔 한 장씩의 내보내기. */
  private advanceFades(): void {
    while (this.fades.length > 0 && this.fades[0].at <= this.clock) {
      const next = this.fades.shift()
      if (!next) break
      // **밖으로 나갑니다.** 버린 것도 낸 것도 화면 밖으로 물러납니다 — 태워 없애는 것은
      // 조커와 소모품의 것이고, 카드는 치우는 것입니다.
      //
      // **높이는 판의 높이 하나입니다.** 저마다 자기 자리의 높이로 나가면 손에서 나가는
      // 카드가 손의 높이로 오른쪽에 빠지는데, 그 높이 그 자리에 덱이 있습니다 — 버린
      // 카드가 덱으로 되돌아가는 것으로 보였습니다. 득점하고 물러나는 카드가 나가는
      // 그 자리로 함께 나갑니다.
      next.view.retire(PLAY_Y)
      this.retired++
      // **조용히 나갑니다.** 버린 카드에만 조각을 흩뿌렸는데, 그러면 버리는 것과 득점하고
      // 물러나는 것이 화면에서 다른 일로 보입니다 — 둘 다 그 카드가 이 판에서 없어지는
      // 것이고, 무엇이 없어졌는지는 카드가 나가는 것으로 이미 보입니다.
      this.audio.play('card_destroy')
    }
  }

  /** 예약해 둔 한 장씩의 이동. */
  private advanceSlams(): void {
    while (this.slams.length > 0 && this.slams[0].at <= this.clock) {
      const next = this.slams.shift()
      if (!next) break
      next.view.slam(next.x, PLAY_Y)
      this.audio.play('card_slam')
      this.jolt(2.2, 0.35)
      // **마지막 카드가 닿을 때까지 세지 않습니다.** 날아가는 중인 카드 위에 숫자가 뜨면
      // 다섯 장이 한 덩어리로 보입니다.
      this.playLanded = this.clock + this.feel.playLandMs / 1000
    }
  }

  /**
   * 낸 카드를 물러나게 합니다. 화면 밖으로 나가면 그때 지웁니다.
   *
   * **한 장씩 나갑니다.** 다섯 장이 한꺼번에 미끄러지면 한 덩어리가 빠져나가는 것으로
   * 보이고, 낸 것이 다섯 장이었다는 것이 마지막에 지워집니다.
   */
  private clearPlayArea(): void {
    // **들린 카드는 먼저 내려옵니다.** 득점한 카드는 8픽셀 들려 있는데, 그 채로 나가면
    // 매칭된 것과 아닌 것이 어긋난 줄로 물러나고 들렸던 것이 도로 내려오는 것을 보지
    // 못합니다 — 올라간 것은 내려와서 없어져야 한 몸짓으로 읽힙니다.
    for (const view of this.playedViews) view.scoring = false
    this.playedViews.forEach((view, index) => {
      if (view.retiring) return
      this.fades.push({
        view,
        at: this.clock + ITEM_SETTLE + ITEM_LINGER + index * (this.feel.playStaggerMs / 1000),
      })
    })
  }

  /**
   * 판이 끝났습니다. 손에 남은 카드를 걷습니다.
   *
   * **끝났다는 판은 빈 자리 위에 섭니다.** 손에 카드가 그대로 있는데 그 위에 판이 덮이면,
   * 끝난 것과 아직 쥐고 있는 것이 한 화면에 겹칩니다 — 코어는 진 판의 손패를 비우지 않으므로
   * 화면이 걷습니다.
   *
   * 태우지 않고 물러나게 합니다. 버리는 것은 없애는 것이고, 이것은 치우는 것입니다.
   */
  private sweepHand(): void {
    const left = [...this.cards.keys()]
    if (left.length === 0) return
    this.throwAway(left)
    this.shown.hand = []
  }

  /** 물러난 카드를 치웁니다. */
  private reapPlayArea(): void {
    for (let i = this.playedViews.length - 1; i >= 0; i--) {
      if (!this.playedViews[i].gone) continue
      this.playedViews[i].destroy()
      this.playedViews.splice(i, 1)
    }
  }

  // ---------------------------------------------------------------- 연출

  private startTimeline(events: GameEvent[]): void {
    const beats = buildTimeline(events, this.feel)
    if (beats.length === 0) {
      this.settleShown()
      this.chips.reset(0)
      this.mult.reset(0)
      this.score.target = Number(this.state.score)
      return
    }

    this.chips.reset(0)
    this.mult.reset(0)
    // 옵션의 배속. **연출을 끄지는 못하고 빨리 넘길 수만 있습니다.**
    this.player.base = this.settings.speed
    this.player.play(beats)
  }

  private showBeat(beat: Beat): void {
    const event = beat.event
    const semitones = semitonesOf(beat.intensity, this.feel)
    const dust = particlesOf(beat.intensity, this.feel)

    switch (event.t) {
      // 낸 카드가 왼쪽부터 한 장씩 판으로 올라갑니다. **이 박자가 끝날 때까지 아무것도
      // 세지 않습니다.**
      case 'HandPlayed':
        this.shown.hand = this.shown.hand.filter(uid => !event.uids.includes(uid))
        this.liftToPlayArea(event.uids)
        this.refresh()
        break

      // 다음 패. **득점이 끝난 뒤에, 한 장씩** 덱에서 옵니다 — 여러 장이 한꺼번에 깔리면
      // 뽑았다는 느낌이 없고 그냥 패가 바뀌어 있습니다.
      case 'HandDrawn':
        event.uids.forEach((uid, index) => {
          this.deals.push({ uid, at: this.clock + index * (this.feel.drawStaggerMs / 1000) })
        })
        break

      case 'HandDiscarded':
        this.shown.hand = this.shown.hand.filter(uid => !event.uids.includes(uid))
        this.throwAway(event.uids)
        this.refresh()
        break

      case 'HandEvaluated':
        // **득점하지 않는 카드는 물러납니다.** 다섯 장을 냈는데 셋만 세는 것이 화면에
        // 보이지 않으면, 점수가 왜 그것뿐인지 알 수 없습니다.
        this.dimNonScoring(event.cards)
        this.say(tf('ui.hand.level', { name: this.handName(event.hand), level: event.level }), COLOR.ink, 3, 0.35)
        this.audio.play('score_count', semitones)
        this.flashPanel(COLOR.ink, 0.5)
        break

      case 'CardScored': {
        const view = this.viewOf(event.uid)
        // 카드가 차례로 득점할수록 세집니다. **뒤로 갈수록 커지는 것이 기대를 만듭니다.**
        const step = Math.min(1, this.chain / 5)
        const mul = event.op === 'MulMult'
        const tint = event.source === 'rank' || event.chips !== 0 ? COLOR.chips
          : event.money !== 0 ? COLOR.money : COLOR.mult
        this.chain++
        if (view) {
          // **조각을 터뜨리지 않고 빛을 돌립니다.** 카드가 차례로 터지면 화면이 시끄러워지고,
          // 정작 카드 위에 뜬 숫자가 그 조각에 묻힙니다.
          view.pop(0.5 + beat.intensity * 0.4 + step * 0.25 + (mul ? 0.35 : 0))
          view.shine(rgbOf(tint), 1)
        }
        this.popAt(view, valueText(event.op, event.chips, event.mult, event.money),
          tint, beat.intensity + step * 0.4 + (mul ? 0.5 : 0))
        // 랭크의 칩과 강화·인장·에디션이 낸 것은 소리가 달라야 갈립니다.
        // **카드가 낸 것과 조커가 낸 것은 소리가 갈립니다.** 같은 배수라도 어디서 온
        // 것인지가 들려야 무엇을 세는 중인지 따라갈 수 있습니다.
        this.audio.play(event.source === 'rank' ? 'card_chip'
          : event.chips !== 0 ? 'card_chip'
            : mul ? 'card_mult' : 'joker_add', semitones + this.chain * 2)
        // **화면은 흔들지 않습니다.** 한 장이 점수를 내는 것은 다섯 번, 여덟 번 이어지는
        // 일이고, 그때마다 화면이 흔들리면 카드 위의 숫자를 읽을 수 없습니다 — 일어난 자리를
        // 가리키는 것은 그 카드에 도는 빛 하나로 충분합니다.
        this.flashPanel(tint, 0.4 + step * 0.3)
        this.stop(28 + step * 26 + (mul ? 60 : 0))
        if (event.money !== 0 && view) {
          this.coins.fly(event.money, { x: view.x, y: view.y }, this.moneySpot())
        }
        break
      }

      case 'JokerTriggered': {
        const view = this.jokers.get(this.jokerUidAt(event.slot))
        const mul = event.op === 'MulMult'
        const money = event.op === 'AddMoney'
        const grow = event.op === 'GrowSelf'
        const cue = mul ? 'joker_mul' : money ? 'joker_money' : 'joker_add'
        const text = valueText(event.op, event.chips, event.mult, event.money)
        const tint = grow ? COLOR.good
          : money ? COLOR.money
            : mul || event.chips === 0 ? COLOR.mult : COLOR.chips

        this.chain++
        // **조각을 터뜨리지 않습니다.** 조커는 한 판에 열 번도 발동하고, 그때마다 조각이
        // 터지면 화면이 시끄러워집니다 — 좌우로 흔들리는 것 하나로 충분합니다.
        if (view) view.pop(mul ? 1.6 : 1.1)
        this.popAt(view, text, tint, beat.intensity + (mul ? 0.6 : 0.2))
        this.audio.play(cue, semitones + this.chain)
        // **조커가 웅얼거립니다.** 값이 오르는 소리만으로는 그것이 누가 낸 값인지가 남지
        // 않습니다 — 목소리는 조커마다 고정이라, 같은 조커가 두 번 발동하면 같은 목소리로
        // 두 번 웅얼거립니다.
        //
        // 이어질수록 잦아듭니다. 한 판에 열 번 발동하는 것이라, 매번 같은 크기로 나면
        // 웅얼거림이 득점 소리를 덮습니다.
        if (view) this.audio.mumble(view.uid, Math.max(0.35, 1 - this.chain * 0.12))

        // **배수를 곱하는 것이 이 게임에서 가장 큰 사건입니다.** 그 하나만 크게 다룹니다.
        if (mul) {
          this.jolt(12 + beat.intensity * 10, 2 + beat.intensity * 2, 0.62)
          this.flashScreen(COLOR.mult, 0.2 + beat.intensity * 0.16)
          this.flashPanel(COLOR.mult, 1)
          this.stop(120)
        } else {
          this.jolt(5 + beat.intensity * 6, 0.8 + beat.intensity, 0.24)
          this.flashPanel(tint, 0.6)
          this.stop(48)
        }

        if (money && event.money !== 0 && view) {
          this.coins.fly(event.money, { x: view.x, y: view.y }, this.moneySpot())
        }
        break
      }

      // 덱과 바우처와 보스가 낸 것. **조커가 아닌 것도 임자가 있습니다** — 판돈 딱지가
      // 그 자리입니다.
      case 'RunTriggered': {
        const mul = event.op === 'MulMult'
        const tint = event.money !== 0 ? COLOR.money
          : event.chips !== 0 ? COLOR.chips : COLOR.mult
        this.popAt(this.badge, valueText(event.op, event.chips, event.mult, event.money),
          tint, beat.intensity + (mul ? 0.5 : 0.1))
        this.audio.play(mul ? 'joker_mul' : 'joker_add', semitones)
        this.jolt(4 + beat.intensity * 5, 0.7 + beat.intensity, 0.2)
        this.flashPanel(tint, 0.6)
        this.stop(mul ? 90 : 40)
        break
      }

      case 'JokerFizzled': {
        const view = this.jokers.get(this.jokerUidAt(event.slot))
        this.popAt(view, `${event.num}/${event.den}`, COLOR.inkDim, 0)
        this.audio.play('joker_fizzle')
        break
      }

      case 'Retriggered': {
        const view = this.viewOf(event.uid)
        this.chain++
        if (view) {
          view.pop(1)
          view.shine(rgbOf(COLOR.good), 0.9)
        }
        this.popAt(view, t('ui.button.again'), COLOR.good, beat.intensity + 0.3)
        this.audio.play('retrigger', semitones + this.chain * 2)
        this.jolt(5, 0.9, 0.2)
        this.flashPanel(COLOR.good, 0.5)
        this.stop(40)
        break
      }

      case 'MoneyChanged': {
        if (event.delta === 0) break
        // **상점에서 오간 돈은 되감지 않았으니 다시 더하지도 않습니다.** 상점은 누른
        // 그 자리에서 잡액이 바뀌어야 하므로 `rewind` 가 그만큼을 되돌리지 않습니다 —
        // 동전은 그대로 날아가고, 잡액만 발보다 먼저 갑니다.
        if (!paidNow(event.reason)) this.shown.money += event.delta
        this.money.target = this.shown.money
        const spot = this.moneySpot()
        // **판 돈은 내놓은 그 자리에서 나옵니다.** 그것이 어느 것을 내놓아 들어온 돈인지를
        // 말하는 유일한 표시입니다.
        const sold = event.reason === 'sell' ? this.sellFrom : undefined
        if (event.reason === 'sell') this.sellFrom = undefined
        const from = sold
          ?? (this.state.phase === 'shop' && event.reason === 'shop'
            ? { x: BOARD_X, y: SHOP_CARD_Y + 81 }
            : { x: BOARD_X, y: PLAY_Y })
        this.coins.fly(event.delta, from, spot)
        this.flashPanel(event.delta > 0 ? COLOR.money : COLOR.bad, 0.7)
        this.audio.play(event.delta > 0 ? 'joker_money' : 'shop_reroll')
        if (event.delta > 0) this.jolt(3, 0.6, 0.18)

        // **무엇으로 번 돈인가**를 적습니다. 합계만 굴러가면 이유를 알 수 없습니다.
        const why = moneyReason(event.reason)
        if (why) {
          // 정산에 오를 것이면 그 판의 한 줄이 됩니다. 아니면 그 자리에 한 번 뜹니다.
          if (this.payoutWanted) {
            this.payoutRows.push({ why, amount: event.delta })
            if (this.modals.has(this.payout)) this.drawPayout()
          } else {
            // **판 돈은 내놓은 자리에 뜹니다.** 금액 칸 옆에 뜨면 사는 것과 파는 것이 같은
            // 자리에서 잇달아 떠서 뒤의 것이 앞의 것을 덮습니다.
            const line = `${why}  ${event.delta > 0 ? '+' : ''}$${event.delta}`
            const tint = event.delta > 0 ? COLOR.money : COLOR.bad
            // **칸 아래에서 올라옵니다.** 조커와 소모품 줄은 화면 맨 위라, 칸의 가운데에서
            // 시작하면 글이 화면 밖으로 올라가 잘립니다.
            if (sold) this.popAt({ x: sold.x, y: sold.y + SIZE.jokerHeight }, line, tint, 0.7)
            else this.popAt(this.moneyLabelAnchor(), line, tint, 0.3)
          }
        }
        break
      }

      case 'ScoreResolved':
        // **더해집니다.** 이 판의 점수가 아니라 라운드에 쌓인 점수가 칸에 뜹니다.
        this.shown.score += event.score
        this.score.target = this.shown.score
        this.audio.play('score_settle', semitones)

        // **마지막 한 방이 앞의 것들보다 확실히 커야 합니다.** 그것이 없으면 득점이
        // 어디서 끝났는지 읽히지 않습니다.
        this.jolt(14 + shakeOf(beat.intensity, this.feel), 2.4 + beat.intensity * 2, 0.9)
        this.flashScreen(COLOR.ink, 0.26 + beat.intensity * 0.2)
        this.flashPanel(COLOR.ink, 1)
        this.stop(150)

        // 낸 카드가 멈춘 자리에서 크게 터집니다.
        this.burstAcrossPlayArea(26 + dust * 4, COLOR.mult, 1.8 + beat.intensity)
        this.chain = 0
        break

      case 'BlindCleared':
        // **정산 판이 상점보다 먼저 섭니다.** 돈이 들어오는 것을 보고 나서 쓰는 것이
        // 순서이고, 상점이 먼저 열리면 그 돈이 어디서 왔는지가 지나가 버립니다.
        this.payoutRows.length = 0
        this.payoutWanted = true
        // **이 게임에서 사람이 기다리는 순간입니다.** 채널을 전부 씁니다 — 화면이
        // 번쩍이고, 판이 흔들리고, 배경이 밝아지고, 음이 여섯 번 올라갑니다.
        //
        // **글은 적지 않습니다.** 곧 정산 판이 서서 무엇을 얼마나 받는지가 적히므로,
        // 「넘겼습니다」는 그 판이 할 말을 한 번 미리 하는 것일 뿐입니다.
        this.audio.play('blind_clear')
        this.chime('coin_land', 6, 3, 0.07)
        this.burstAcrossPlayArea(46, COLOR.good, 2.4, 2.6)
        this.particles.burst(BOARD_X, PLAY_Y - 60, 70, COLOR.money, 2.6, 2.8)
        this.particles.burst(BOARD_X, 210, 44, COLOR.good, 2.2, 2.4)
        // **국면이 넘어가는 자리입니다.** 흔들림은 판 전체를 움직이므로, 여기서 큰 값을
        // 쓰면 격파한 것이 아니라 땅이 흔들린 것으로 읽힙니다 — 알릴 것은 이미 터지는
        // 것과 번쩍이는 것과 소리 셋이 하고 있습니다.
        this.jolt(9, 4.2, 1)
        this.flashScreen(COLOR.good, 0.46)
        this.recallToDeck()
        this.stop(280)
        this.chain = 0
        this.fever = 0
        break

      case 'RunLost':
        this.sweepHand()
        // **글은 적지 않습니다.** 끝났다는 판이 곧 서고 거기에 몇 점이 모자랐는지까지
        // 적히므로, 머리글은 그 판이 할 말을 미리 하는 것입니다.
        this.audio.play('blind_fail')
        this.jolt(5, 1.6, 0.5)
        this.flashScreen(COLOR.bad, 0.2)
        this.stop(160)
        break

      case 'RunWon':
        // **이긴 것을 저장에 남깁니다.** 챌린지를 열어 주는 것도 여기입니다 — 원작은 덱
        // 여러 종으로 이겨야 열리지만 우리에게는 덱을 고르는 화면이 아직 없으므로, 한 번
        // 이기는 것을 조건으로 둡니다.
        this.recordWin()
        this.sweepHand()
        this.say(t('ui.label.all_cleared'), COLOR.money, 2.8)
        this.chime('coin_land', 10, 2, 0.06)
        this.audio.play('blind_clear')
        this.particles.burst(BOARD_X, SIZE.height / 2, 120, COLOR.money, 2.6)
        this.jolt(8, 3.4, 1)
        this.flashScreen(COLOR.money, 0.44)
        this.stop(220)
        break

      default:
        break
    }

    // **박자가 값을 들고 옵니다.** 화면이 이벤트마다 값을 다시 세지 않는 이유가 이것입니다 —
    // 누적값과 에디션처럼 세는 자리가 여럿이면 반드시 한쪽이 빠집니다.
    if (beat.chips !== undefined) this.chips.target = beat.chips
    if (beat.mult !== undefined) this.mult.target = Math.round(beat.mult / 10_000)
    this.chips.emphasize(scaleOf(beat.intensity, this.feel))
    this.mult.emphasize(scaleOf(beat.intensity, this.feel))
    // **오를 때는 즉시.** 배수가 커진 그 박자에서 불이 붙어야 그 조커가 한 일로 읽힙니다.
    this.fever = Math.max(this.fever, beat.intensity)
  }

  /**
   * 득점하지 않는 카드를 물러나게 합니다.
   *
   * **원작이 그렇습니다** — 다섯 장을 냈는데 족보에 드는 것이 둘뿐이면 나머지 셋은 회색이
   * 되고, 뜨지도 세지도 않습니다.
   */
  private dimNonScoring(scoring: readonly number[]): void {
    for (const view of this.playedViews) {
      const counts = scoring.includes(view.uid)
      view.setPick(counts ? 0 : -1, PICK_TINT)
      view.idle = counts ? 0.4 : 0.15
      // **안착한 다음에 살며시 올라갑니다.** 이 박자는 카드가 다 닿은 뒤에 오므로, 여기서
      // 올리면 날아가는 중에 들리는 일이 없습니다.
      view.scoring = counts
    }
  }

  /**
   * 두 칸이 타오릅니다.
   *
   * **세기는 흔들림과 같은 값입니다** — `Const_Feel` 의 문턱을 넘은 배수부터 불이 붙고,
   * 상한에서 가장 높습니다. 채널마다 따로 재면 어느 하나만 사납게 반응하는 화면이 됩니다.
   *
   * 불은 곧바로 꺼지지 않습니다. 박자마다 한 번씩 붙었다 꺼지면 깜빡이는 것으로 보이므로,
   * 오를 때는 즉시 오르고 내릴 때만 잦아듭니다.
   */
  private advanceFire(deltaMs: number): void {
    const seconds = deltaMs / 1000
    // **잦아드는 데 여유를 둡니다.** 빠르게 꺼지면 득점이 끝나기도 전에 불이 없어져서,
    // 그 판이 뜨거웠다는 것이 남지 않습니다.
    // **잦아드는 데 여유를 둡니다.** 0.5는 두 배수가 이어질 때 그 사이에 불이 꺼져서,
    // 한 판이 뜨거웠다는 것이 남지 않고 번쩍임 여럿으로 흩어집니다 — 득점이 끝나고도
    // 잠깐 타고 있어야 그 판이 뜨거웠던 것이 됩니다.
    this.fever = Math.max(0, this.fever - seconds * 0.22)

    const heat = this.fever
    // **칩 쪽이 조금 낮습니다.** 배수가 이 게임의 큰 수이므로 그쪽이 더 타야 하고, 다만
    // 너무 낮추면 문턱을 넘지 못해 파란 쪽이 꺼진 것처럼 보입니다.
    this.chipsFlame.heat = heat * 0.88
    this.multFlame.heat = heat
    this.chipsFlame.advance(seconds)
    this.multFlame.advance(seconds)

    // **자리는 한 번만 잡습니다.** 불은 이제 자기 반 안에 갇혀 있고 그 반은 움직이지
    // 않으므로, 매 프레임 따라다닐 것이 없습니다.
    const lit = heat > 0.004
    this.chipsFire.visible = lit
    this.multFire.visible = lit
  }

  /**
   * 판 뒤를 흐립니다.
   *
   * **필요할 때만 겁니다.** 흐림은 화면 전체를 한 번 더 굽는 것이라, 판이 없는 동안에도
   * 걸어 두면 매 프레임 그 값을 냅니다.
   */
  private advanceBlur(seconds: number): void {
    const want = this.modals.busy ? 1 : 0
    this.blurShown += (want - this.blurShown) * Math.min(1, seconds * 11)

    const on = this.blurShown > 0.01
    const filtered = (this.recede.filters as unknown[] | null)?.length ?? 0
    if (on && filtered === 0) {
      this.recede.filters = [this.blur]
      this.backdrop.filters = [this.blurBack]
    } else if (!on && filtered > 0) {
      this.recede.filters = []
      this.backdrop.filters = []
    }
    // **약하게.** 뒤가 무엇인지는 알아볼 수 있어야 합니다 — 판을 닫고 어디로 돌아가는지가
    // 보이지 않으면 판이 화면을 갈아치운 것으로 보입니다.
    if (on) {
      this.blur.strength = this.blurShown * 3
      this.blurBack.strength = this.blurShown * 2
    }
  }

  /** 한 방. 흔들림과 색수차를 함께 겁니다. */
  /**
   * 패널을 번쩍입니다.
   *
   * **왼쪽의 숫자들이 바뀌는 자리를 파티클로 알리면 숫자를 가립니다.** 패널 자체가 빛나면
   * 무엇이 바뀌었는지가 가려지지 않고 눈에 들어옵니다.
   */
  /** 금액이 왜 바뀌었는지를 띄울 자리. 판 가운데입니다. */
  private moneyLabelAnchor(): Container {
    const anchor = new Container()
    // 낸 카드 아래입니다. **카드 위에 겹치면 흰 종이에 흰 글씨가 됩니다.**
    anchor.position.set(BOARD_X, PLAY_Y + 200)
    return anchor
  }

  /** 금액 칸의 가운데. 동전이 여기로 꽂힙니다. */

  private moneySpot(): { x: number; y: number } {
    return { x: this.money.x + 62, y: this.money.y + 26 }
  }

  /** 낸 카드가 늘어선 폭 전체에서 터뜨립니다. 한 점에서 터지면 찔끔 나온 것으로 보입니다. */
  private burstAcrossPlayArea(perCard: number, tint: number, power: number,
                              linger = 1.9): void {
    if (this.playedViews.length === 0) {
      this.particles.burst(BOARD_X, PLAY_Y, perCard * 3, tint, power, linger)
      return
    }
    for (const view of this.playedViews) {
      this.particles.burst(view.x, view.y, perCard, tint, power, linger)
      this.particles.burst(view.x, view.y - 40, Math.round(perCard * 0.6),
        COLOR.chips, power * 0.8, linger)
    }
  }

  private flashPanel(tint: number, strength: number): void {
    this.panelTint = tint
    this.panelGlow = Math.min(1, Math.max(this.panelGlow, strength))
  }

  /** 화면 전체를 번쩍입니다. **큰 것에만 씁니다** — 잦으면 눈이 아픕니다. */
  private flashScreen(tint: number, strength: number): void {
    this.screenTint = tint
    this.screenGlow = Math.min(0.72, Math.max(this.screenGlow, strength))
  }

  /** 때린 순간 연출의 시계를 잠깐 멈춥니다. 그 한 방이 무거워집니다. */
  private stop(ms: number): void {
    this.freeze = Math.max(this.freeze, ms)
  }

  /**
   * 한 방.
   *
   * **채널을 한꺼번에 씁니다** — 흔들림 · 색수차 · 배경의 번쩍임. 하나만 쓰면 「움직였다」로
   * 읽히고, 셋이 같이 오면 「맞았다」로 읽힙니다.
   */
  /**
   * 연출이 다 끝났는가.
   *
   * **국면이 바뀌었어도 앞 국면의 연출이 돌고 있으면 화면을 갈지 않습니다.** 낸 카드가 아직
   * 판에 있는데 상점이 그 위에 그려지면 무엇을 보고 있는지 알 수 없습니다.
   */
  /**
   * 화면 가운데에 한 줄.
   *
   * **머리글은 사라져야 합니다.** 「넘겼습니다」가 상점에 가도 떠 있으면 지난 일이 지금 일처럼
   * 보입니다. 뜰 때 크게 튀었다가 잦아들고, 정해진 시간이 지나면 없어집니다.
   */
  private say(text: string, tint: number, seconds: number, pop = 1): void {
    this.headline.text = text
    this.headline.style.fill = tint
    this.headlineLife = seconds
    this.headlineSpan = seconds
    this.headline.visible = true
    this.headline.alpha = 1
    this.headline.scale.set(0.3 + 0.35 * (1 - pop))
  }

  private advanceHeadline(seconds: number): void {
    if (this.headlineLife <= 0) {
      if (this.headline.visible) this.headline.visible = false
      return
    }

    this.headlineLife = Math.max(0, this.headlineLife - seconds)
    const gone = 1 - this.headlineLife / this.headlineSpan

    // 처음 한 순간은 튀어나오는 구간입니다. 1을 넘겼다가 돌아옵니다.
    const grow = gone < 0.08
      ? 0.3 + 1.05 * (gone / 0.08)
      : 1.35 - 0.35 * Math.min(1, (gone - 0.08) / 0.14)
    const shiver = Math.max(0, 1 - gone / 0.3)
    const jitter = shiver * shiver

    this.headline.scale.set(grow)
    this.headline.position.set(
      BOARD_X + (Math.random() - 0.5) * 18 * jitter,
      214 + (Math.random() - 0.5) * 13 * jitter)
    this.headline.rotation = (Math.random() - 0.5) * 0.055 * jitter
    // 마지막 구간에서 사라집니다.
    this.headline.alpha = Math.min(1, (1 - gone) / 0.35)
  }

  /** 음이 하나씩 올라가는 소리 여러 개. **오르는 음이 「해냈다」로 읽힙니다.** */
  private chime(cue: string, count: number, step = 3, gap = 0.075): void {
    for (let i = 0; i < count; i++) {
      this.chimes.push({ at: this.clock + i * gap, cue, semitones: i * step })
    }
  }

  private advanceChimes(): void {
    while (this.chimes.length > 0 && this.chimes[0].at <= this.clock) {
      const next = this.chimes.shift()
      if (next) this.audio.play(next.cue, next.semitones)
    }
  }

  /**
   * 굴러가는 숫자에 소리를 붙입니다.
   *
   * **간격이 좁아지고 음이 오릅니다.** 남은 거리가 줄면 빨라지므로, 끝으로 갈수록 촘촘해지고
   * 높아집니다 — 그 조여드는 것이 「쌓이고 있다」입니다.
   */
  private advanceRatchet(seconds: number): void {
    if (!this.settings.sound) return

    // **곱해지기를 기다리는 동안.** 마지막 카드가 득점하고 두 숫자가 곱해질 때까지가
    // 이 게임에서 사람이 가장 크게 기다리는 자리인데, 재어 보니 그 1초가 무음이었습니다.
    // 조여들며 올라가는 소리로 채웁니다.
    if (this.player.coming === 'ScoreResolved') {
      this.ratchet -= seconds
      if (this.ratchet > 0) return
      this.build = Math.min(1, this.build + 0.12)
      this.ratchet = 0.10 - this.build * 0.055
      this.audio.play('score_count', Math.round(this.build * 16) - 4)
      return
    }
    this.build = 0

    // **숫자가 굴러가는 동안.** 남은 거리가 줄면 촘촘해지고 음이 오릅니다.
    const rolling = this.score.rolling
    if (rolling <= 0) {
      this.ratchet = 0
      return
    }

    this.ratchet -= seconds
    if (this.ratchet > 0) return
    this.ratchet = 0.05 + rolling * 0.06
    this.audio.play('score_count', Math.round((1 - rolling) * 14) - 4)
  }

  /**
   * 정산 판이 서는 때.
   *
   * **카드가 다 걷혀야 섭니다** — 낸 카드가 물러났고, 타서 사라지는 것도 끝났고, 손패도
   * 걷혔을 때입니다. 그러고 나서 줄이 하나씩 쌓입니다.
   */
  private advancePayout(seconds: number): void {
    void seconds

    if (this.payoutWanted && !this.payoutOpen) {
      const swept = this.playedViews.length === 0 && this.fades.length === 0
        && this.shown.hand.length === 0 && this.deals.length === 0
      if (swept && !this.player.busy) {
        this.payoutOpen = true
        this.drawPayout()
        this.modals.open(this.payout)
        // **상점은 정산 뒤입니다.** 판이 열린 것을 상점이 알아야 물러납니다 — 카드가
        // 걷히는 그 프레임에 상점이 이미 그려져 있습니다.
        this.refresh()
      }
    }

    this.advancePayoutBones()

    for (const one of this.payoutNodes) {
      if (one.node.alpha < 1) this.advanceOne(one)
    }
  }

  /**
   * 뼈대 줄.
   *
   * **줄마다 자기 차례에 걷힙니다.** 한꺼번에 걷으면 빈 상자가 한 번 보이고, 그러면
   * 뼈대를 깔아 둔 뜻이 없어집니다 — 실제 줄이 그 자리에 서는 그때 그 자리의 뼈대만
   * 사라집니다.
   */
  private advancePayoutBones(): void {
    const wait = this.payoutWait
    if (!wait) return

    wait.bones.clear()
    let left = 0
    for (let i = 0; i < wait.rows; i++) {
      const at = wait.begin + i * PAYOUT_STEP
      const fade = Math.max(0, Math.min(1, (at - this.clock) / 0.18))
      if (fade <= 0) continue
      left++

      // 물결. **가만히 있는 회색 막대는 멈춘 화면으로 보입니다.**
      const wave = 0.42 + 0.26 * Math.sin(this.clock * 5 - i * 0.9)
      const alpha = fade * wave
      const y = wait.top + i * wait.rowH + 8
      // 왼쪽이 이유, 오른쪽이 금액. 실제 줄과 같은 자리입니다.
      wait.bones.roundRect(24, y, 132, 15, 7).fill({ color: 0x8ea2bd, alpha })
      wait.bones.roundRect(wait.width - 24 - 62, y, 62, 15, 7)
        .fill({ color: 0x8ea2bd, alpha: alpha * 0.86 })
    }

    wait.head.alpha = Math.max(0, Math.min(1, (wait.begin - this.clock) / 0.2))
    if (left > 0) return
    wait.bones.destroy()
    wait.head.destroy()
    this.payoutWait = undefined
  }

  /** 때가 된 것을 합니다. */
  private advanceLater(): void {
    for (let i = this.later.length - 1; i >= 0; i--) {
      if (this.later[i].at > this.clock) continue
      const one = this.later[i]
      this.later.splice(i, 1)
      one.run()
    }
  }

  private get presented(): boolean {
    return this.cardsQuiet && !this.coins.busy
  }

  /**
   * 카드가 다 물러났는가.
   *
   * **동전은 세지 않습니다.** 동전이 나는 동안에도 서 있어야 하는 것들이 있고 — 상점이
   * 그렇습니다 — 카드가 아직 걷히는 중인 것과는 다른 일입니다.
   */
  /**
   * 상점이 서도 되는가.
   *
   * **카드가 다 걷혔는가만 봅니다.** 연출이 도는 중인지는 보지 않습니다 — 사는 것도 연출
   * 하나이므로 그것까지 세면 하나 살 때마다 큰 판이 사라졌다 다시 섭니다. 카드가 걷히는
   * 동안 서지 않는 것은 그것과 다른 일입니다: 낸 카드가 아직 물러나는 중인데 판이 그 위에
   * 서면 그 둘이 겹칩니다.
   */
  private get shopReady(): boolean {
    return this.scene === 'run' && this.playedViews.length === 0
      && this.deals.length === 0 && this.fades.length === 0
  }

  private get cardsQuiet(): boolean {
    return this.scene === 'run' && !this.player.busy && this.playedViews.length === 0
      && this.deals.length === 0 && this.fades.length === 0
  }

  private jolt(shake: number, chroma: number, pulse = 0): void {
    // 흔들림과 색수차는 **꺼 둘 수 있습니다.** 배경이 밝아지는 것은 남깁니다 — 그것이
    // 없으면 큰 값이 온 것을 알릴 채널이 하나도 없습니다.
    if (this.settings.shake) this.shake = Math.max(this.shake, shake)
    if (this.settings.chromatic) {
      this.punch.hit(Math.min(chroma, this.feel.chromaticMaxPx * 2))
    }
    if (pulse > 0) this.background.pulse(pulse)
  }

  private popAt(target: { x: number; y: number } | undefined, text: string, tint: number,
                intensity: number): void {
    const label = new Text({
      text,
      style: {
        fontSize: 20 + intensity * 16, fill: tint, fontWeight: '800',
        stroke: { color: 0x0a0f18, width: 4 },
      },
    })
    label.anchor.set(0.5, 1)
    label.position.set(
      target ? target.x : BOARD_X, (target ? target.y : SIZE.height / 2) - 46)
    label.resolution = this.textScale
    this.overlay.addChild(label)

    // **그냥 뜨면 심심합니다.** 튀어나왔다가 부르르 떨며 올라갑니다.
    const homeX = label.x
    const homeY = label.y
    const drift = (Math.random() - 0.5) * 34
    const rumble = 3 + intensity * 7
    const span = 760
    let life = 0

    label.scale.set(0.4)
    const rise = () => {
      life += this.app.ticker.deltaMS
      const t = Math.min(1, life / span)

      // 처음 120밀리초는 튀어나오는 구간입니다. 1을 넘겼다가 돌아옵니다.
      const grow = life < 120
        ? 0.4 + 0.85 * (life / 120)
        : 1.25 - 0.25 * Math.min(1, (life - 120) / 220)
      label.scale.set(grow + t * 0.18)

      const fade = Math.max(0, 1 - t)
      const shiver = rumble * fade * fade
      label.x = homeX + drift * t + (Math.random() - 0.5) * shiver
      label.y = homeY - t * 46 + (Math.random() - 0.5) * shiver
      label.rotation = (Math.random() - 0.5) * 0.05 * fade
      label.alpha = fade

      if (life >= span) {
        this.app.ticker.remove(rise)
        label.destroy()
      }
    }
    this.app.ticker.add(rise)
  }

  /**
   * 왼쪽 판의 수가 오르내린 것을 그 자리에 적습니다.
   *
   * **바뀐 것을 보여 주는 것과 지금 값을 보여 주는 것은 다른 일입니다.** 칸의 숫자는
   * 언제나 지금 값이라 「4」 가 「3」 이 되는 것은 눈을 그 칸에 두고 있어야만 보이고,
   * 화면 한가운데를 보고 있으면 그 사이에 무엇이 줄었는지 모른 채 지나갑니다 — 그래서
   * 줄어든 만큼이 그 칸에서 한 번 떠오릅니다.
   *
   * **글을 만들지 않고 돌려 씁니다.** 이것이 뜨는 자리는 사람이 누르는 자리가 아니라
   * 상태가 바뀌는 자리라, 조커 하나가 라운드마다 버리기를 주고 태그가 핸드를 주고 하는
   * 판에서는 한 프레임에 여럿이 겹칠 수 있습니다 — 그때마다 `Text` 하나와 티커 콜백
   * 하나를 만들면 만드는 값이 보여 주는 값보다 커집니다.
   *
   * **여덟이 넘으면 가장 오래된 것을 빼앗습니다.** 아홉째를 그리지 않고 버리는 쪽은
   * 그 순간 무엇이 바뀌었는지를 통째로 잃는 것이고, 가장 오래된 것은 이미 옅어져
   * 사라지는 중이므로 잃는 것이 적습니다.
   */
  private slotDelta(slot: Container, before: number, after: number, tint: number): void {
    if (before < 0 || after === before) return

    const delta = after - before
    const one = this.freeDelta()
    one.life = 0
    one.homeY = slot.y + 16
    one.node.text = `${delta > 0 ? '+' : ''}${delta}`
    one.node.style.fill = delta > 0 ? tint : COLOR.bad
    one.node.position.set(slot.x + 108, one.homeY)
    one.node.scale.set(0.6)
    one.node.alpha = 1
    one.node.visible = true
  }

  /**
   * 쓸 수 있는 글 하나.
   *
   * 노는 것이 있으면 그것이고, 없으면 만들고, 다 찼으면 가장 오래된 것입니다.
   */
  private freeDelta(): { node: Text; life: number; homeY: number } {
    const idle = this.deltas.find(one => !one.node.visible)
    if (idle) return idle

    if (this.deltas.length < DELTA_POOL) {
      const node = new Text({
        text: '', style: { fontSize: 19, fontWeight: '800', fill: COLOR.ink,
                           stroke: { color: 0x0a0f18, width: 4 } },
      })
      // 칸의 오른쪽 위에서 뜹니다. **칸 위가 아니라 모서리입니다** — 위에 두면 그 칸의
      // 이름표를 덮고, 안에 두면 지금 값과 겹쳐 어느 것이 값인지 흐려집니다.
      node.anchor.set(0.5, 1)
      node.resolution = this.textScale
      node.visible = false
      this.board.addChild(node)
      const made = { node, life: 0, homeY: 0 }
      this.deltas.push(made)
      return made
    }

    return this.deltas.reduce((oldest, one) => one.life > oldest.life ? one : oldest)
  }

  /** 떠오르는 차이 글들. 다 떠오른 것은 다시 풀로 돌아갑니다. */
  private advanceDeltas(seconds: number): void {
    for (const one of this.deltas) {
      if (!one.node.visible) continue
      one.life += seconds
      const t = Math.min(1, one.life / DELTA_LIFE)
      // 튀어올랐다가 천천히 올라가며 옅어집니다. 앞의 0.1초가 튀는 구간입니다.
      one.node.scale.set(one.life < 0.1
        ? 0.6 + 0.55 * (one.life / 0.1)
        : 1.15 - 0.15 * Math.min(1, (one.life - 0.1) / 0.26))
      one.node.y = one.homeY - t * 26
      one.node.alpha = 1 - t * t
      if (t >= 1) one.node.visible = false
    }
  }

  private handName(hand: PokerHandKind): string {
    const key = `hand.${PokerHandKind[hand]}.name`
    return text(this.data, key)
  }

  private viewOf(uid: number): CardView | undefined {
    return this.cards.get(uid) ?? this.playedViews.find(view => view.uid === uid)
  }

  private jokerUidAt(slot: number): number {
    return this.state.jokers[slot]?.uid ?? -1
  }

  // ---------------------------------------------------------------- 매 프레임

  private tick(deltaMs: number): void {
    const seconds = deltaMs / 1000
    this.clock += seconds

    // **히트스톱.** 연출의 시계만 멈춥니다 — 용수철과 파티클은 계속 움직여야 화면이
    // 얼어붙은 것으로 보이지 않습니다.
    if (this.freeze > 0) this.freeze = Math.max(0, this.freeze - deltaMs)
    else this.player.advance(deltaMs)
    this.publishPeek()

    this.advanceConsumableLift(seconds)
    this.advanceItemArrive(seconds)
    this.advancePress()
    this.advanceReveals()
    this.advancePack(seconds)
    this.advanceLater()
    this.advancePayout(seconds)
    this.advanceRatchet(seconds)
    this.advanceBurningItems(seconds)
    this.coins.advance(seconds)
    this.toasts.advance(seconds)
    this.decayFlashes(seconds)

    this.background.advance(seconds)
    this.punch.advance(seconds)

    // **필터는 필요할 때만 겁니다.** 늘 걸어 두면 판이 매 프레임 그림으로 한 번 구워지고,
    // 그 그림이 화면 배율에 늘어나 글씨가 뿌옇게 됩니다.
    const punching = !this.punch.quiet
    const filtered = (this.board.filters as unknown[] | null)?.length ?? 0
    if (punching && filtered === 0) this.board.filters = [this.punch]
    else if (!punching && filtered > 0) this.board.filters = []
    // **배경의 빠르기는 천천히 따라갑니다.** 점수가 한 박자에 크게 뛰므로 그대로 먹이면
    // 블라인드가 그대로인데도 배경이 휘리릭 돕니다.
    this.heatShown += (this.heat() - this.heatShown) * Math.min(1, seconds * 0.9)
    this.background.setHeat(this.heatShown)
    this.particles.advance(seconds)

    for (const slot of [this.score, this.chips, this.mult, this.money]) slot.advance(deltaMs)
    // 칩과 배수 칸은 제자리에 있습니다. **모였다 흩어지는 것은 걷어냈습니다** — 그 사이에
    // 두 수를 읽을 수 없고 옆의 칸들과 줄도 어긋납니다.
    this.advanceFire(deltaMs)

    this.updateHover()
    this.updateHandHover()

    for (const view of this.cards.values()) {
      view.pointer = this.tiltFor(view)
      view.advance(seconds, this.clock)
    }
    for (const view of this.playedViews) view.advance(seconds, this.clock)
    const before = this.playedViews.length
    this.reapPlayArea()
    if (before > 0 && this.playedViews.length === 0) {
      this.holdAfterScore = 0
      this.refresh()
    }
    for (const view of this.jokers.values()) {
      view.pointer = this.tiltFor(view)
      view.advance(seconds, this.clock)
    }
    for (let i = this.burning.length - 1; i >= 0; i--) {
      const view = this.burning[i]
      view.advance(seconds, this.clock)
      if (!view.gone) continue
      view.destroy()
      this.burning.splice(i, 1)
    }

    // 연출이 끝난 순간에 한 번 다시 그립니다. **그때가 다음 국면의 화면을 띄울 때입니다.**
    const busyNow = !this.presented
    if (this.wasBusy && !busyNow) {
      this.settleShown()
      this.refresh()
    }
    this.wasBusy = busyNow

    this.advanceHeadline(seconds)
    this.advanceChimes()
    this.advanceSlams()
    this.advanceFades()
    this.advanceDeals()

    // 덱은 판이 도는 동안만 자리에 있습니다.
    // **판이 도는 동안만 자리에 있습니다.** 블라인드를 고르는 중에도 아직 없습니다 —
    // 시작을 누르면 오른쪽에서 들어옵니다.
    const away = this.state.phase !== 'round'
    this.deckSlide.target = away ? 300 : 0
    this.deckSlide.advance(seconds)
    this.deckLayer.x = this.deckSlide.value
    this.deckLayer.visible = this.deckSlide.value < 296
    this.advanceRecalls(seconds)
    this.advanceDeltas(seconds)
    this.advanceShopLift(seconds)
    this.advanceTagFlash(seconds)
    this.advanceGameOver(seconds)
    this.title.advance(seconds)
    this.modals.advance(seconds)
    // 조커 풀의 카드도 살아 있어야 합니다 — 에디션 무늬가 시간을 읽고 손을 올리면 들립니다.
    this.jokerPool.advance(seconds, this.clock)

    // 블라인드 판이 들어오는 동안 매 프레임 다시 그립니다.
    if (this.state.phase === 'blind-select' && this.blindEnter < 1) {
      this.blindEnter = Math.min(1, this.blindEnter + seconds * 1.5)
      this.drawBlindPick()
    } else if (this.state.phase !== 'blind-select') {
      this.blindEnter = 0
      this.blindShown = -1
    }
    this.advanceBlur(seconds)

    // **끝났다는 판은 연출이 다 끝난 뒤에 띄웁니다.** 마지막 카드의 결과를 보기 전에 덮이면
    // 무엇 때문에 끝난 것인지 알 수 없습니다.
    // **카드가 다 걷힌 뒤에 섭니다.** 손패와 낸 카드가 물러나는 중인데 그 위에 판이 덮이면,
    // 끝난 것과 끝나는 중인 것이 한 화면에 겹칩니다 — 정산 판과 같은 규칙입니다.
    const finished = this.state.phase === 'lost' || this.state.phase === 'won'
    const swept = this.playedViews.length === 0 && this.fades.length === 0
      && this.cards.size === 0 && this.deals.length === 0
    if (finished && swept && !this.gameOverShown
        && !this.player.busy && this.score.settled && !this.coins.busy) {
      this.drawGameOver()
    }

    this.gauge.visible = this.state.phase === 'round'
    if (this.gauge.visible) this.drawGauge()

    // 흔들림은 줄어듭니다. **판만 흔들고 배경은 가만히 둡니다** — 둘 다 흔들면 무엇이
    // 맞은 것인지 읽히지 않습니다.
    if (this.shake > 0.08) {
      const angle = Math.random() * Math.PI * 2
      this.board.position.set(
        Math.cos(angle) * this.shake, Math.sin(angle) * this.shake)
      this.overlay.position.set(this.board.x * 0.5, this.board.y * 0.5)
      this.shake *= 0.84
    } else if (this.board.x !== 0 || this.board.y !== 0) {
      this.board.position.set(0, 0)
      this.overlay.position.set(0, 0)
      this.shake = 0
    }

    if (!this.player.busy) {
      this.chips.emphasize(1)
      this.mult.emphasize(1)
      // **결과를 읽을 시간을 둡니다.** 점수가 다 굴러간 뒤에도 잠깐 남아 있어야
      // 무엇을 냈고 얼마가 되었는지가 보입니다.
      if (this.playedViews.length > 0 && this.score.settled) {
        this.holdAfterScore += deltaMs
        if (this.holdAfterScore > 1_100 && !this.playedViews[0].retiring) {
          this.clearPlayArea()
        }
      } else {
        this.holdAfterScore = 0
      }
      if (this.state.phase !== 'round') {
        this.chips.target = 0
        this.mult.target = 0
      }
    }
  }

  /** 번쩍임은 줄어듭니다. 패널은 빠르게, 화면은 더 빠르게 — 오래 남으면 눈이 아픕니다. */
  private decayFlashes(seconds: number): void {
    if (this.panelGlow > 0.002) {
      this.panelGlow = Math.max(0, this.panelGlow - seconds * 3.6)
      const ease = this.panelGlow * this.panelGlow
      this.panelFlash.clear()
      this.panelFlash.roundRect(LEFT - 12, 22, PANEL_W + 24, SIZE.height - 44, 12)
        .fill({ color: this.panelTint, alpha: ease * 0.3 })
      this.panelFlash.roundRect(LEFT - 11, 23, PANEL_W + 22, SIZE.height - 46, 11)
        .stroke({ color: this.panelTint, width: 1 + ease * 4, alpha: ease })
      this.panelDrawn = true
    } else if (this.panelDrawn) {
      this.panelFlash.clear()
      this.panelDrawn = false
    }

    if (this.screenGlow > 0.002) {
      this.screenGlow = Math.max(0, this.screenGlow - seconds * 4.4)
      this.screenFlash.clear()
      this.screenFlash.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000)
        .fill({ color: this.screenTint, alpha: this.screenGlow * this.screenGlow })
      this.screenDrawn = true
    } else if (this.screenDrawn) {
      this.screenFlash.clear()
      this.screenDrawn = false
    }
  }

  /** 배경이 얼마나 뜨거운가. 점수가 요구에 가까울수록 올라갑니다. */
  private heat(): number {
    if (this.state.phase === 'shop') return 0.15
    if (this.state.target <= 0) return 0.1
    return Math.max(0.08, Math.min(1, this.shown.score / Number(this.state.target)))
  }

  /**
   * 마우스가 무엇 위에 있는가.
   *
   * **올라간 자리가 아니라 쉬는 자리로 판정합니다.** 마우스가 올라오면 카드가 위로
   * 들리는데, 들린 카드로 판정하면 카드가 마우스 밑에서 빠져나가 곧바로 내려오고, 내려오면
   * 다시 들립니다 — 카드 아래쪽 몇 픽셀에서 카드가 떨던 것이 그것입니다.
   *
   * 겹쳐 있을 때는 오른쪽 것이 위입니다. 손패를 그리는 순서가 그렇습니다.
   */
  /**
   * 꾸욱 누르기를 시작합니다.
   *
   * **마우스에는 걸지 않습니다.** 마우스는 올리면 그 자리에서 뜨므로, 누르고 기다리게 하면
   * 마우스로는 되던 것이 느려지기만 합니다.
   */
  private armPress(event: FederatedPointerEvent, show: () => void): void {
    if (event.pointerType === 'mouse') return
    const at = this.world.toLocal(event.global)
    this.press = { at: this.clock, x: at.x, y: at.y, show, fired: false }
  }

  /**
   * 손가락이 움직였으면 누른 것이 아닙니다.
   *
   * 카드를 끌어 자리를 바꾸는 것과 겹치기 때문입니다 — 끄는 중에 설명이 뜨면 끌고 있는
   * 카드를 그 쪽지가 덮습니다.
   */
  private advancePressMove(): void {
    const press = this.press
    if (!press || press.fired) return
    const dx = this.pointerAt.x - press.x
    const dy = this.pointerAt.y - press.y
    if (dx * dx + dy * dy > HOLD_SLACK * HOLD_SLACK) this.press = undefined
  }

  /** 오래 눌렀으면 띄웁니다. 한 번만입니다. */
  private advancePress(): void {
    const press = this.press
    if (!press || press.fired) return
    if (this.clock - press.at < HOLD_TIP) return
    press.fired = true
    // **그 손가락이 떼어질 때의 누름을 먹습니다.** 설명을 보려고 누른 것이 그대로
    // 고르기·사기·쓰기가 되면 안 됩니다.
    this.pressAte = true
    this.pressShown = true
    this.audio.play('button')
    press.show()
  }

  /**
   * 이 누름이 설명을 띄운 것이었는가.
   *
   * **한 번만 참입니다.** 누름을 다루는 자리마다 맨 앞에서 물어봅니다 — 참이면 그 누름은
   * 설명을 본 것이므로 아무것도 하지 않습니다.
   */
  private ate(): boolean {
    if (!this.pressAte) return false
    this.pressAte = false
    return true
  }

  /**
   * 설명이 뜨는 자리 하나를 답니다.
   *
   * **마우스와 손가락의 길이 다릅니다.** 마우스는 올리면 뜨고 벗어나면 닫히고, 손가락은
   * 꾸욱 누르면 뜹니다 — 부르는 자리마다 그 둘을 따로 적으면 언젠가 한쪽이 빠집니다.
   */
  private tipOn(node: Container, show: () => void): void {
    node.on('pointerover', event => {
      if (event.pointerType !== 'mouse') return
      show()
    })
    node.on('pointerdown', event => this.armPress(event, show))
    // **꾸욱 눌러 띄운 것은 떼어도 닫지 않습니다.** 손가락을 떼면 「벗어났다」 가 나므로,
    // 그것으로 닫으면 누르고 있는 동안에만 보입니다 — 읽을 시간이 없습니다.
    node.on('pointerout', () => {
      if (!this.pressShown) this.tooltip.hide()
    })
  }

  private updateHover(): void {
    // **손가락에는 「올려 둔다」 가 없습니다.** 손가락은 떼고 나서도 그 자리가 마지막으로
    // 지나간 자리로 남으므로, 누른 조커가 계속 올려진 것으로 셉니다 — 골랐다가 다시 눌러
    // 놓아도 그 카드만 조금 들린 채로 있었고, 다른 카드를 눌러 그 자리가 옮겨질 때에야
    // 내려왔습니다.
    const blocked = this.touching || this.modals.busy || this.state.pack !== null
      || this.state.phase === 'lost' || this.state.phase === 'won'
    // **뽑는 동안에는 카드에 올려지지 않습니다.** 마우스가 나오는 길목에 있으면 지나가는
    // 카드마다 차례로 들려 올라가고, 그것은 고르는 것으로도 지나가는 것으로도 읽히지
    // 않습니다.
    const dealing = this.deals.length > 0 || this.clock < this.dealtUntil

    let card: CardView | undefined
    let joker: JokerView | undefined

    if (!blocked) {
      for (const view of this.cards.values()) {
        if (dealing) break
        if (!near(this.pointerAt, view.motion, SIZE.cardWidth, SIZE.cardHeight)) continue
        if (!card || view.motion.x.target > card.motion.x.target) card = view
      }
      for (const view of this.jokers.values()) {
        if (!near(this.pointerAt, view.motion, SIZE.jokerWidth, SIZE.jokerHeight)) continue
        if (!joker || view.motion.x.target > joker.motion.x.target) joker = view
      }
    }

    for (const view of this.cards.values()) view.hovered = view === card
    for (const view of this.jokers.values()) view.hovered = view === joker

    // **손가락으로는 이 길을 쓰지 않습니다.** 손가락은 뗀 뒤에도 그 자리가 그대로 남아서,
    // 설명이 떼고 나서도 붙어 있습니다 — 손가락은 꾸욱 눌러서 봅니다.
    if (joker && !this.touching) {
      if (joker !== this.hoveredJoker) this.showTooltip(joker)
    } else if (this.hoveredJoker) {
      this.tooltip.hide()
    }
    this.hoveredJoker = joker
  }

  private tiltFor(view: Container): number {
    return Math.max(-1, Math.min(1, (this.pointerAt.x - view.x) / 90))
  }

  private drawGauge(): void {
    const x = BOARD_X - 220
    const y = 240
    const width = 440
    const height = 12

    this.gauge.clear()
    const ratio = this.state.target > 0
      ? this.shown.score / Number(this.state.target) : 0

    this.gauge.roundRect(x, y, width, height, 6).fill(0x101724)
    this.gauge.roundRect(x, y, width * Math.min(1, ratio), height, 6)
      .fill(ratio >= 1 ? COLOR.good : COLOR.chips)
    this.gauge.roundRect(x - 0.5, y - 0.5, width + 1, height + 1, 6)
      .stroke({ color: COLOR.panelEdge, width: 1 })
  }

  private publishPeek(): void {
    const state = this.state
    ;(window as unknown as { __clover?: unknown }).__clover = {
      // 어느 씬인가. **판을 접고 타이틀로 갔는지를 이것으로 봅니다.**
      scene: this.scene,
      // 설명 쪽지가 지금 떠 있는가. 꾸욱 누르기를 재는 도구가 씁니다.
      tip: this.tooltip.visible,
      // 지금 몇 장 골라 두었는가. 꾸욱 눌렀을 때 골라지지 않는지를 봅니다.
      picked: this.selected.size,
      // 화면에 카드 뷰가 몇 장 살아 있는가. 접었으면 0 이어야 합니다.
      views: this.cards.size + this.playedViews.length + this.jokers.size,
      // 어느 통에 남아 있는가. **「카드가 남아 있다」만으로는 손패인지 낸 것인지 걷는
      // 중인 것인지 갈리지 않고**, 갈리지 않으면 어디를 고쳐야 할지 알 수 없습니다.
      bins: {
        hand: this.cards.size,
        played: this.playedViews.length,
        fades: this.fades.length,
        deals: this.deals.length,
        shown: this.shown.hand.length,
      },
      seed: state.seed,
      // 무엇으로 시작한 판인가. **고른 것이 실제로 걸렸는지는 이 둘로만 확인됩니다** —
      // 화면에는 뒷면과 시작 조건으로만 나타나므로 그림으로는 갈리지 않습니다.
      deck: state.deckId,
      stake: state.stake,
      // 상점 판이 지금 서 있는가. 하나 사는 동안 접히지 않는지를 봅니다.
      shopUp: this.shopLayer.visible,
      // 연출의 시계. 스크린샷 사이의 시간을 재는 데 씁니다.
      clock: this.clock,
      phase: state.phase, ante: state.ante, blind: state.blind,
      money: state.money, score: Number(state.score), target: Number(state.target),
      // **화면에 적힌 잔액입니다.** 위의 `money` 는 코어의 값이라 연출이 어디까지
      // 왜는지와 무관하게 항상 맞습니다 — 「잔액이 언제 바뀌는가」를 재려면 이쪽입니다.
      shownMoney: this.shown.money,
      jokers: state.jokers.length, discards: state.discardsLeft,
      hands: state.handsLeft,
      packOpen: state.pack !== null, packs: state.shop.packs.length,
      // 상점 칸마다 무엇이 서 있는가. **도구가 칸을 짚어야 합니다** — 소모품이 오는 길을
      // 재는 도구가 네 칸을 차례로 눌러 보고 있었고, 조커만 서 있는 상점에서는 아무것도
      // 사지 못한 채 「사지 못했습니다」 로 끝났습니다.
      shopKinds: state.shop.cards.map(card => card.kind),
      // **들고 있는 태그와, 딱지에 실제로 그린 칩 수.** 둘이 갈라져야 어디가 틀렸는지
      // 나옵니다 — 상태에 없으면 규칙이고, 있는데 안 그렸으면 화면입니다.
      // 리더보드. **로그아웃 상태의 게임이 지금과 같은지**를 도구가 이것으로 봅니다.
      signedIn: this.hub.signedIn,
      // 판이 하나라도 떠 있는가. 리더보드와 로그인 판이 떴는지를 봅니다.
      modalUp: this.modals.busy,
      // 맨 위 판이 화면에서 차지한 사각형. **판을 누르는 도구가 자리를 다시 세지 않습니다.**
      modalBox: this.modals.box,
      // 통신이 지금 오가는가. 도는 동안 입력이 막힙니다.
      netBusy: netBusy(),
      // 랭크 런인가.
      ranked: this.hub.isRanked(state.seed),
      // 끝난 판이 떴는가. **제출은 그 판이 뜰 때 나갑니다** — 카드가 다 걷힌 뒤입니다.
      gameOver: this.gameOverShown,
      tags: state.tagsPending.slice(),
      tagChips: this.badge.chipCount,
      // **칩이 실제로 어디에 그려졌는가.** 「잠깐 왼쪽 위로 튄다」는 프레임 몇 개짜리라
      // 눈으로는 어느 프레임에 어디였는지를 말할 수 없습니다.
      tagAt: this.badge.chipSpots,
      played: this.playedViews.length, coins: this.coins.busy,
      // **머리글이 아니라 국면으로 봅니다.** 「넘겼습니다」 라는 글은 걷어냈습니다 —
      // 곧 정산 판이 서서 무엇을 얼마나 받는지가 적히므로, 그 글은 그 판이 할 말을 한 번
      // 미리 하는 것이었습니다.
      cleared: this.payoutWanted || this.payoutOpen,
      consumables: state.consumables.length,
      // **자리가 규칙입니다.** 득점은 낸 카드의 왼쪽부터이고 조커는 슬롯의 왼쪽부터이므로,
      // 자리를 바꾸는 것이 되는지는 이 두 줄로만 확인할 수 있습니다.
      //
      // 패는 **화면이 그리는 차례**를 알립니다. 코어의 차례를 알리면 끌어다 놓아도 화면은
      // 제자리로 돌아가는데 도구는 통과합니다 — 실제로 그런 결함이 있었습니다.
      // **버튼의 자리도 알립니다.** 도구가 같은 계산을 베껴 적으면 배치를 고칠 때 한쪽만
      // 고쳐지고, 그 도구는 엉뚱한 곳을 눌러 놓고 아무 말도 하지 않습니다.
      spots: this.spots,
      // 소리가 비는 자리를 찾을 때 씁니다 — 시계로 재면 배속과 히트스톱에서 어긋납니다.
      coming: this.player.coming ?? '',
      // 최근에 난 소리들. **「이 순간에 왜 이 소리가 나느냐」를 도구가 물을 자리입니다.**
      sounds: this.audio.played.slice(),
      payout: this.payoutOpen,
      // **사서 오는 중인 소모품의 가로 자리.** 조커는 뷰가 용수철을 들고 있어 오는 길이
      // 있지만 소모품 칸은 매번 새로 만들어지므로, 오는 길이 있는지는 이 값이 프레임마다
      // 달라지는지로만 확인됩니다 — 눈으로는 0.5초짜리를 놓칩니다.
      //
      // **이 줄이 없어져 있었습니다.** `check-item-fly.ts` 가 이것을 읽는데 없으면 `-1` 을
      // 받고, 그 도구는 「사지 못했습니다」 로 끝나 무엇이 틀렸는지 말하지 않았습니다.
      // 사서 오는 중인 소모품 한 장. **함수가 아니라 값입니다** — 이 손잡이는 프레임마다
      // 다시 놓이므로 값이면 충분하고, 함수로 두면 도구가 부르는 그 순간의 것이라
      // 「없다」와 「부르지 못했다」가 같은 답으로 돌아옵니다.
      //
      // **가로만 재면 반만 봅니다.** 상점의 칸과 소모품 칸이 세로로는 멀고 가로로는 가까울
      // 수 있습니다.
      fly: this.flyPeek(),
      // 소모품이 올 자리를 잡아 준 횟수와, 잡지 못한 횟수. **오는 길이 없을 때 그것이
      // 「부르지 않았다」인지 「불렀는데 잡을 것이 없었다」인지 갈립니다.**
      flyAsked: this.flyAsked,
      flyMissed: this.flyMissed,
      handOrder: this.shown.hand.slice(),
      jokerOrder: state.jokers.map(joker => joker.uid),
      // **판을 끝까지 두는 도구를 위한 손잡이입니다.** 사람이 보라고 넣은 뜸이 도구에게는
      // 기다림일 뿐이고, 그 기다림이 실행 시간의 대부분입니다. 옵션의 속도와 같은 값입니다.
      hurry: (times: number) => { this.player.base = times },
      // **개발 서버에서만 있습니다.** 자리를 바꾸는 것이 되는지 보려면 조커가 둘 있어야
      // 하는데, 그것을 사려고 판을 열 판 두는 동안 확인하려던 것과 상관없는 곳에서 도구가
      // 멈춥니다. 구운 것에는 이 줄이 들어가지 않습니다.
      ...(import.meta.env.DEV ? {
        grantJoker: (count: number) => {
          const rows = this.data.tables.joker.records
          for (let i = 0; i < count && i < rows.length; i++) {
            this.state.jokers.push({
              uid: this.state.nextUid++,
              jokerId: rows[i].jokerId,
              edition: 0 as never,
              sticker: 0 as never,
              counters: newCounters(),
              age: 0,
              disabled: false,
            })
          }
          this.refresh()
        },
        // **블라인드를 넘긴 것으로 칩니다.** 상점에서 도는 코드를 재려면 상점에 닿아야
        // 하는데, 도구의 자동 진행은 안테 1을 넘기지 못하고 집니다 — 그래서 「터진 것
        // 0건」 이 상점에 대해서는 아무 말도 아니었고, 실제로 상점에서 매 프레임 터지는
        // 것을 이 도구가 지나쳤습니다.
        clearBlind: () => {
          this.state.score = Number(this.state.target)
          this.shown.score = this.state.score
          this.act({ t: 'play', cards: this.state.hand.slice(0, 1) })
        },
        grantActive: () => {
          this.state.tagsPending = ['voucher', 'juggle']
          this.state.vouchers = this.data.tables.voucher.records.slice(0, 2)
            .map(row => row.voucherId)
          this.refresh()
        },
        setFever: (value: number) => {
          this.fever = value
        },
        // 소모품 첫 칸이 지금 그려진 자리. 사서 오는 길을 재는 도구가 씁니다.
        itemX: () => this.consumableTiles[this.consumableTiles.length - 1]?.tile.x,
        jokerX: () => {
          const first = this.state.jokers[0]
          return first ? this.jokers.get(first.uid)?.x : undefined
        },
        // **돈이 없어서 못 사는 것과 자리가 없어서 못 넣는 것은 다른 일입니다.** 자리
        // 쪽을 보려면 돈은 걸림돌이 아니어야 합니다.
        // **소리는 조용히 실패합니다.** WebAudio 는 잘못된 값에 예외를 내는데 그것을 받는
        // 곳이 없어서, 웅얼거림이 안 나는 것과 예외로 죽은 것을 화면에서 가릴 수 없습니다.
        mumble: (voice: number) => this.audio.mumble(voice),
        grantMoney: (amount: number) => {
          this.state.money += amount
          this.money.reset(this.state.money)
          this.settleShown()
          this.refresh()
        },
        grantConsumable: (count: number) => {
          const rows = this.data.tables.tarot.records
          for (let i = 0; i < count && i < rows.length; i++) {
            this.state.consumables.push({
              uid: this.state.nextUid++,
              kind: 1 as never,
              id: rows[i].tarotId,
              edition: 0 as never,
            })
          }
          this.refresh()
        },
      } : {}),
      busy: this.player.busy || !this.score.settled || this.coins.busy,
      // **화면이 주장하는 패입니다.** 도구가 눌러야 하는 것은 지금 그려져 있는 카드입니다.
      hand: this.shown.hand.map(uid => {
        const card = state.deck.find(entry => entry.uid === uid)
        return { rank: card?.rank ?? 0, suit: card?.suit ?? 0 }
      }),
    }
  }

  // ---------------------------------------------------------------- 다시 그리기

  private editionLook(edition: EditionKind): EditionLook | undefined {
    const row = this.data.tables.editionVisual.findByEdition(edition)
    if (!row || row.shader === 'none') return undefined
    return {
      shader: row.shader as EditionLook['shader'],
      strength: row.strength, flowSpeed: row.flowSpeed, noise: row.noise,
    }
  }

  private refresh(): void {
    const state = this.state
    this.publishPeek()

    // **세울 것 목록은 여기서 비웁니다.** 상점과 팩이 같이 쓰므로, 어느 한쪽이 비우면
    // 다른 쪽이 이미 담아 둔 것을 지우게 됩니다.
    this.reveals.length = 0

    // **오르내린 만큼이 그 칸에서 한 번 떠오릅니다.** 칸의 숫자는 언제나 지금 값이므로,
    // 눈을 그 칸에 두고 있지 않으면 무엇이 줄었는지 모른 채 지나갑니다.
    //
    // 돈은 여기서 세지 않습니다 — 동전이 날아가 꽂히는 것이 이미 그 일을 하고 있고,
    // 둘이 겹치면 같은 말이 한 자리에서 두 번입니다.
    this.slotDelta(this.hands, this.panelShown.hands, state.handsLeft, COLOR.good)
    this.slotDelta(this.discards, this.panelShown.discards, state.discardsLeft, 0xff9d5c)
    this.slotDelta(this.anteSlot, this.panelShown.ante, state.ante, COLOR.ink)
    this.panelShown.hands = state.handsLeft
    this.panelShown.discards = state.discardsLeft
    this.panelShown.ante = state.ante

    this.money.target = this.shown.money
    this.score.target = this.shown.score
    this.hands.text = String(state.handsLeft)
    this.discards.text = String(state.discardsLeft)
    this.anteSlot.text = `${state.ante} / ${this.data.run.winAnte}`
    this.deckLabel.text = tf('ui.stat.deck', { left: state.drawPile.length, all: state.deck.length })
    this.jokerCount.text = `${state.jokers.length} / ${state.rules.jokerSlots}`
    this.consumableCount.text =
      `${state.consumables.length} / ${state.rules.consumableSlots}`

    this.updateHints()

    this.syncBadge()
    this.drawFrames()
    this.syncCards()
    this.syncJokers()
    this.syncConsumables()
    this.syncTags()
    this.syncActive()
    this.syncShop()
    this.syncPack()
    this.syncButtons()
    this.syncMood()
    this.syncMusic()
    this.drawPreview()
    this.previewSlots()
    // 떠 있는 판만 다시 그립니다. **닫힌 판을 그리는 것은 낭비이고**, 남은 카드는 덱
    // 52장을 매번 만듭니다.
    if (this.modals.has(this.handList)) this.drawHandList()
    if (this.modals.has(this.deckView)) this.drawDeckView()
    this.drawBlindPick()
    // 다시 시작하면 판을 걷습니다. **띄우는 것은 `tick` 이 합니다** — 연출이 끝난 뒤여야
    // 하기 때문입니다.
    if (this.state.phase !== 'lost' && this.state.phase !== 'won') this.drawGameOver()
    // **국면을 말하는 글은 연출이 끝난 뒤에 바뀝니다.** 코어는 액션 하나를 끝까지 처리해
    // 두므로, 그대로 그리면 아직 득점을 보고 있는데 상점의 지시문이 떠 있습니다.
    // **지난 지시문을 붙잡아 두지 않습니다.** 이미 낸 뒤에 「5장 골랐습니다」가 남아 있으면
    // 무엇을 하라는 말인지가 아니라 무엇을 했었는지가 됩니다.
    this.drawHint(this.presented ? this.hintText() : '')
    this.drawPips()
    this.sharpen(this.world.scale.x)
  }

  /**
   * 도움을 다시 셉니다.
   *
   * **패에서 가장 값이 높은 조합을 찾아, 그 조합에 들어가는데 아직 고르지 않은 카드를
   * 표시합니다.** 지금 고른 것이 이미 그만큼 값이 나오면 아무것도 표시하지 않습니다 —
   * 잘 고른 사람에게 계속 권하면 방해입니다.
   *
   * 조커를 세지 않으므로 「더 높은 족보」이지 「더 높은 점수」가 아닙니다. 조커가 붙으면
   * 사람의 판단이 더 나을 수 있고, 그때 이 표시는 무시하면 됩니다.
   */
  private updateHints(): void {
    this.hinted.clear()
    if (this.state.phase !== 'round') return
    if (!this.settings.hints) return
    // **핸드가 도는 동안에는 권하지 않습니다.** 득점을 보고 있는데 패의 카드들이 따로
    // 깜빡이면 눈이 둘로 갈리고, 그때는 고를 수도 없습니다.
    if (!this.presented) return

    const held = this.state.hand
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)
    if (held.length === 0) return

    const best = bestHand(this.data, this.state, held)
    if (!best) return

    const picked = held.filter(card => this.selected.has(card.uid))
    const now = picked.length > 0 ? valueOf(this.data, this.state, picked) : undefined
    if (now && now.value >= best.value) return

    for (const card of best.cards) {
      if (!this.selected.has(card.uid)) this.hinted.add(card.uid)
    }
    // 고른 것 전부가 최선의 조합에 들어 있지 않으면 권할 것이 없습니다 — 지금 고른 것을
    // 풀어야 하는 상황이므로 카드 표시로는 알릴 수 없습니다.
    const inBest = new Set(best.cards.map(card => card.uid))
    if (picked.some(card => !inBest.has(card.uid))) this.hinted.clear()
  }

  /**
   * 몇 장 골랐는가.
   *
   * **「최대 5장」 이라고 적어 두는 것으로는 부족합니다** — 칸 다섯이 채워지는 것이 보여야
   * 몇 장 더 고를 수 있는지가 세지 않고 읽힙니다.
   */
  /**
   * 몇 장 골랐는가.
   *
   * **가운데 버튼에 적습니다.** 고른 것을 푸는 자리와 몇 장 골랐는지가 같은 자리에 있으면
   * 눈이 한 번만 갑니다. 아무것도 고르지 않았으면 셀 것이 없으므로 `-` 입니다.
   */
  private drawPips(): void {
    const picked = this.selected.size
    // 켜고 끄는 것은 `syncButtons` 가 정합니다 — 여기는 적는 것만 합니다.
    this.clearButton.text = picked === 0
      ? '-'
      : `${picked} / ${this.data.run.maxPlayedCards}`
  }

  /**
   * 지시문 한 줄.
   *
   * **수와 이름은 다른 색입니다.** 「최대 5장」 · 「남은 핸드 3회」에서 사람이 찾는 것은 그
   * 수이고, 문장과 같은 색이면 문장을 처음부터 읽어야 찾습니다.
   */
  private drawHint(text: string): void {
    if (text === this.hintShown) return
    this.hintShown = text
    this.hint.removeChildren().forEach(child => child.destroy())
    if (text === '') return
    const line = richLine(text, {
      base: { fontSize: 13, fill: 0x9fb0c6, fontWeight: '700' },
      number: COLOR.accentNumber,
      term: COLOR.accentTerm,
    })
    line.position.set(-line.width / 2, 0)
    this.hint.addChild(line)
  }

  /** 지금 국면에서 다음에 할 것. */
  private hintText(): string {
    const state = this.state
    switch (state.phase) {
      // 블라인드 선택에는 지시문을 두지 않습니다. **판마다 자기 버튼에 적혀 있습니다.**
      case 'blind-select': return ''
      case 'round':
        if (this.selected.size === 0) {
          return this.hinted.size > 0
            ? tf('ui.hint.best_hand', { n: this.hinted.size })
              + tf('ui.hint.hands_left', { n: state.handsLeft })
            : tf('ui.hint.pick_cards', { n: this.data.run.maxPlayedCards })
              + tf('ui.hint.hands_left', { n: state.handsLeft })
        }
        return tf('ui.hint.selected', { n: this.selected.size })
          + t('ui.hint.discard_to_swap')
      case 'shop':
        // **뜯은 팩에는 그 판이 적습니다.** 여기에도 적으면 덮개 뒤에서 흐릿하게 읽히고,
        // 그것은 남은 글자로 보입니다. 상점도 판 하나이고 그 안에 다 적혀 있습니다.
        return ''
      default:
        return ''
    }
  }

  /**
   * 고른 카드가 무슨 족보이고 얼마짜리인지.
   *
   * **조커를 뺀 순수한 값입니다** — 조커까지 미리 세면 득점 연출이 볼 것이 없어집니다.
   */
  /**
   * 고른 것의 칩과 배수를 먼저 보입니다.
   *
   * **내기 전에 보여야 고를 수 있습니다.** 두 수가 득점할 때에야 나타나면, 무엇을 고를지는
   * 판 가운데의 작은 상자를 읽어서 정하게 됩니다 — 값이 나오는 자리에 값이 미리 있어야
   * 합니다.
   *
   * 연출이 도는 동안에는 손대지 않습니다. 그때의 두 칸은 지금 세고 있는 값입니다.
   */
  private previewSlots(): void {
    // 라운드가 아니면 지웁니다. **그대로 두면 상점에 든 뒤에도 족보 이름이 남습니다.**
    if (this.state.phase !== 'round') {
      this.handLabel.text = ''
      return
    }
    if (!this.presented) return

    const picked = this.orderedSelection()
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    if (picked.length === 0) {
      this.handLabel.text = ''
      this.chips.target = 0
      this.mult.target = 0
      return
    }

    const { hand } = evaluate(picked, this.state.rules)
    const row = this.data.tables.pokerHand.findByHand(hand)
    const level = this.state.handLevels[PokerHandKind[hand]] ?? 1
    this.handLabel.text = tf('ui.hand.level', { name: this.handName(hand), level })
    this.chips.target = (row?.baseChips ?? 0) + (row?.chipsPerLevel ?? 0) * (level - 1)
    this.mult.target = (row?.baseMult ?? 0) + (row?.multPerLevel ?? 0) * (level - 1)
  }

  private drawPreview(): void {
    const picked = this.orderedSelection()
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    const hinting = picked.length === 0 && this.hinted.size > 0
    this.preview.visible = this.state.phase === 'round' && (picked.length > 0 || hinting)
    if (!this.preview.visible) return

    // 고른 것이 없으면 **권하는 조합**을 보여 줍니다. 무엇을 고르면 되는지가 글로도
    // 적혀 있어야 카드의 표시가 무슨 뜻인지 압니다.
    const cards = picked.length > 0
      ? picked
      : this.state.hand
        .map(uid => this.state.deck.find(card => card.uid === uid))
        .filter((card): card is CardInstance => card !== undefined && this.hinted.has(card.uid))

    const { hand } = evaluate(cards, this.state.rules)
    const row = this.data.tables.pokerHand.findByHand(hand)
    const level = this.state.handLevels[PokerHandKind[hand]] ?? 1
    const chips = (row?.baseChips ?? 0) + (row?.chipsPerLevel ?? 0) * (level - 1)
    const mult = (row?.baseMult ?? 0) + (row?.multPerLevel ?? 0) * (level - 1)

    const ink = hinting ? COLOR.money : COLOR.chips
    this.previewHand.text = hinting
      ? tf('ui.hand.preview', { n: cards.length, name: this.handName(hand) })
      : tf('ui.hand.level', { name: this.handName(hand), level })
    this.previewHand.style.fill = ink
    this.previewValue.text = tf('ui.hand.preview_math', { chips, mult, total: chips * mult })

    const width = Math.max(this.previewHand.width, this.previewValue.width) + 40
    this.previewPlate.clear()
    this.previewPlate.roundRect(0, 0, width, 66, 10).fill({ color: 0x101724, alpha: 0.92 })
    this.previewPlate.roundRect(0.5, 0.5, width - 1, 65, 10)
      .stroke({ color: ink, width: 1.5, alpha: 0.8 })

    this.previewHand.position.set(20, 10)
    this.previewValue.position.set(20, 36)
    this.preview.position.set(BOARD_X - width / 2, PREVIEW_Y)
  }

  /**
   * 족보 목록.
   *
   * **줄에 마우스를 올리면 그 족보를 카드로 보여 줍니다.** 「투 페어」가 무엇인지는 낱말이
   * 아니라 카드 다섯 장의 모양이고, 그 모양을 본 적이 없으면 이름만으로는 배울 수 없습니다.
   */
  private drawHandList(): void {
    const layer = this.handList.view
    layer.removeChildren().forEach(child => child.destroy())
    this.handRows.length = 0

    const rows = this.data.tables.pokerHand.records
    const width = 540
    const rowH = 36
    // 갈래 단추가 머리띠 아래 한 줄을 차지합니다.
    const top = TITLE_BAR + 54
    const height = top + rows.length * rowH + 14 + FOOTER_BAR

    layer.addChild(panelFrame(width, height, t('ui.run_info.title'), () => this.toggleHandList()))

    // 세 갈래. **한 판을 도는 동안 궁금해지는 것이 셋입니다** — 어느 족보가 몇 점인지,
    // 이 안테의 블라인드가 무엇인지, 지금 난이도가 무엇을 바꾸는지. 판을 셋 만들면 그
    // 셋을 여는 방법이 저마다 달라지므로 한 판 안의 갈래로 둡니다.
    const tabs: { key: RunInfoTab; label: string }[] = [
      { key: 'hands', label: t('ui.kind.poker_hand') },
      { key: 'blinds', label: t('ui.tab.blinds') },
      { key: 'stakes', label: t('ui.tab.stakes') },
    ]
    const tabW = 128
    const tabGap = 8
    const tabsX = (width - (tabs.length * tabW + (tabs.length - 1) * tabGap)) / 2
    tabs.forEach((tab, index) => {
      const here = this.runInfoTab === tab.key
      const button = new Button(tab.label, tabW, 30, here ? 0x2f6fb5 : 0x333e4e, () => {
        this.runInfoTab = tab.key
        this.drawHandList()
      })
      button.position.set(tabsX + index * (tabW + tabGap), TITLE_BAR + 12)
      layer.addChild(button)
    })

    if (this.runInfoTab !== 'hands') {
      this.drawRunInfoRows(layer, width, top)
      this.handList.size.width = width
      this.handList.size.height = height
      layer.eventMode = 'static'
      return
    }

    const band = new Graphics()
    layer.addChild(band)

    rows.forEach((row, index) => {
      const key = PokerHandKind[row.hand]
      const level = this.state.handLevels[key] ?? 1
      const chips = row.baseChips + row.chipsPerLevel * (level - 1)
      const mult = row.baseMult + row.multPerLevel * (level - 1)
      const seen = row.visibleFromStart || (this.state.handPlayCounts[key] ?? 0) > 0
      const y = top + index * rowH

      const name = new Text({
        text: seen ? this.handName(row.hand) : '???',
        style: { fontSize: 15, fill: seen ? COLOR.ink : COLOR.inkDim, fontWeight: '700' },
      })
      name.position.set(28, y + 2)

      const lv = new Text({
        text: `Lv.${level}`,
        style: { fontSize: 13, fill: level > 1 ? COLOR.good : COLOR.inkDim, fontWeight: '700' },
      })
      lv.position.set(246, y + 3)

      const value = new Text({
        text: seen ? `${chips}  ×  ${mult}` : '—',
        style: { fontSize: 15, fill: seen ? COLOR.chips : COLOR.inkDim, fontWeight: '700' },
      })
      value.position.set(318, y + 2)

      const played = new Text({
        text: tf('ui.hand.times', { n: this.state.handPlayCounts[key] ?? 0 }),
        style: { fontSize: 12, fill: COLOR.inkDim },
      })
      played.anchor.set(1, 0)
      played.position.set(width - 28, y + 4)

      layer.addChild(name, lv, value, played)
      this.handRows.push({ hand: row.hand, seen, y, height: rowH })
    })

    // **가리킨 줄의 그림은 맨 위입니다.** 줄보다 먼저 붙이면 글자가 그림 위에 겹칩니다.
    const preview = new Container()
    preview.visible = false
    layer.addChild(preview)
    this.handBand = band
    this.handPreview = preview
    this.handHovered = -1

    // 자리는 모달 더미가 정합니다. 이쪽은 넓이만 알립니다.
    this.handList.size.width = width
    this.handList.size.height = height
    layer.eventMode = 'static'
  }

  /**
   * 블라인드와 스테이크의 줄들.
   *
   * **둘이 같은 모양입니다** — 이름과 값 몇 개가 한 줄이고, 지금 것에 표가 붙습니다.
   * 갈래마다 판을 따로 만들면 그 셋이 서로 다르게 생기고, 그러면 한 판 안의 갈래가
   * 아니라 판 셋이 됩니다.
   */
  private drawRunInfoRows(layer: Container, width: number, top: number): void {
    const rowH = 36
    const rows: { name: string; note: string; value: string; here: boolean }[] = []

    if (this.runInfoTab === 'blinds') {
      // 이 안테의 세 라운드. **요구 점수는 안테가 정하므로 판마다 다릅니다.**
      for (const blind of [BlindKind.Small, BlindKind.Big, BlindKind.Boss]) {
        const bossRow = blind === BlindKind.Boss
          ? this.data.tables.bossBlind.findByBossId(this.state.bossId) : undefined
        rows.push({
          name: bossRow
            ? nameOf(this.data, 'boss', this.state.bossId, bossRow.name)
            : blindName(blind),
          note: bossRow
            ? describe(this.data, this.data.bossEffects.get(this.state.bossId) ?? []).join(' · ')
            : t('ui.note.no_rules'),
          value: `${targetOf(this.data, this.state, blind).toLocaleString('en-US')}`
            + `   ${tf('ui.blind.reward', { n: rewardOf(this.data, this.state, blind) })}`,
          here: this.state.blind === blind,
        })
      }
    } else {
      // 난이도. **누적입니다** — 뒤의 것은 앞의 것을 전부 포함합니다.
      const here = stakeRow(this.data, this.state.stake)?.stake
      for (const row of this.data.tables.stake.records) {
        rows.push({
          name: nameOf(this.data, 'stake', stakeSlug(row.stake), row.name),
          note: tf('ui.stake.note', {
            column: row.anteColumn, reward: row.smallBlindReward, discards: row.discardsDelta,
          }),
          value: '',
          here: row.stake === here,
        })
      }
    }

    rows.forEach((row, index) => {
      const y = top + index * rowH

      if (row.here) {
        const band = new Graphics()
        band.roundRect(16, y - 4, width - 32, rowH - 4, 6)
          .fill({ color: COLOR.accentTerm, alpha: 0.14 })
        layer.addChild(band)
      }

      const name = new Text({
        text: row.name,
        style: { fontSize: 15, fill: row.here ? COLOR.ink : COLOR.inkDim, fontWeight: '800' },
      })
      name.position.set(28, y)

      const note = new Text({
        text: row.note,
        style: {
          fontSize: 11, fill: COLOR.inkDim,
          wordWrap: true, wordWrapWidth: width - 220, breakWords: true, lineHeight: 13,
        },
      })
      note.position.set(28, y + 18)

      const value = new Text({
        text: row.value,
        style: { fontSize: 14, fill: COLOR.chips, fontWeight: '700' },
      })
      value.anchor.set(1, 0)
      value.position.set(width - 28, y + 2)

      layer.addChild(name, note, value)
    })
  }

  /**
   * 어느 줄을 가리키고 있는가.
   *
   * **화면이 이미 재고 있는 커서 자리를 씁니다.** 줄마다 사건을 붙이는 것보다 자리 하나를
   * 견주는 편이 확실합니다 — 그리는 것이 없는 통은 저절로 잡히지 않습니다.
   */
  private updateHandHover(): void {
    if (!this.modals.has(this.handList) || this.handRows.length === 0) return
    if (this.runInfoTab !== 'hands') return

    const local = this.handList.view.toLocal(this.world.toGlobal(this.pointerAt))
    const width = this.handList.size.width
    let found = -1
    if (local.x >= 12 && local.x <= width - 12) {
      found = this.handRows.findIndex(
        row => local.y >= row.y - 4 && local.y < row.y + row.height - 6)
    }
    if (found === this.handHovered) return
    this.handHovered = found

    const band = this.handBand
    if (band) {
      band.clear()
      const row = this.handRows[found]
      if (row) {
        band.roundRect(12, row.y - 4, width - 24, row.height - 2, 6)
          .fill({ color: 0x4a6ea8, alpha: 0.42 })
      }
    }

    const preview = this.handPreview
    if (!preview) return
    const row = this.handRows[found]
    if (!row) {
      preview.visible = false
      return
    }
    this.showHandShape(preview, row.hand, row.seen, width,
      row.y + row.height - 4, this.handList.size.height)
  }

  /**
   * 그 족보가 어떤 모양인가.
   *
   * **카드 다섯 장으로 보여 줍니다.** 족보에 드는 카드는 밝고 들지 않는 카드는 물러납니다 —
   * 「투 페어」에서 다섯째 장이 세지 않는다는 것이 그 그림에 있어야 합니다.
   *
   * 가리킨 줄 바로 아래에, **판의 너비를 꽉 채워** 놓입니다. 판 아래로 넘치면 줄 위로
   * 올라갑니다.
   */
  private showHandShape(into: Container, hand: PokerHandKind, seen: boolean,
                        width: number, below: number, panelHeight: number): void {
    into.removeChildren().forEach(child => child.destroy())
    into.visible = true

    const cardW = 52
    const cardH = 73
    const gap = 8
    const boxW = width - 24
    const shape = seen ? HAND_SHAPE[hand] : undefined
    const boxH = shape ? cardH + 24 : 46

    const board = new Graphics()
    board.roundRect(0, 0, boxW, boxH, 10).fill({ color: 0x0c1320, alpha: 0.98 })
    board.roundRect(0.5, 0.5, boxW - 1, boxH - 1, 10)
      .stroke({ color: 0x6f7f9a, width: 1.5 })
    into.addChild(board)

    if (!shape) {
      const veiled = new Text({
        text: t('ui.hand.never_played'),
        style: { fontSize: 12, fill: COLOR.inkDim },
      })
      veiled.anchor.set(0.5, 0.5)
      veiled.position.set(boxW / 2, boxH / 2)
      into.addChild(veiled)
    } else {
      const span = shape.length * cardW + (shape.length - 1) * gap
      const startX = (boxW - span) / 2
      shape.forEach((spot, index) => {
        const card: CardInstance = {
          uid: -1 - index, baseCardId: '', rank: spot.rank, suit: spot.suit,
          enhancement: EnhancementKind.None, seal: SealKind.None, edition: EditionKind.Base,
          bonusChips: 0, debuffed: false, faceDown: false,
        }
        const mini = this.miniCard(card, spot.counts, cardW, cardH)
        mini.position.set(startX + index * (cardW + gap), 12)
        into.addChild(mini)
      })
    }

    // 판 아래로 넘치면 줄 위로 올라갑니다.
    const under = below + 6
    const floor = panelHeight - FOOTER_BAR - 8
    const y = under + boxH > floor ? below - boxH - 40 : under
    into.position.set(12, y)
  }

  /**
   * 블라인드 셋을 한 자리에.
   *
   * **원작의 화면입니다** — 스몰·빅·보스가 나란히 서고, 지금 차례인 것 하나만 앞으로
   * 나옵니다. 이미 넘긴 것은 표시가 붙고, 아직 오지 않은 것은 물러나 있습니다.
   *
   * 건너뛰기가 뜻을 가지려면 **다음에 무엇이 오는지가 보여야 합니다.** 보스의 효과가
   * 그중에서도 가장 중요하고, 그래서 보스 칸에는 무엇을 하는 보스인지가 적힙니다.
   */
  private drawBlindPick(): void {
    this.blindPick.removeChildren().forEach(child => child.destroy())
    delete this.spots.pick
    delete this.spots.skip
    const state = this.state
    this.blindPick.visible = state.phase === 'blind-select' && this.presented
    if (!this.blindPick.visible) return

    // 블라인드가 바뀌면 처음부터 다시 들어옵니다.
    if (this.blindShown !== state.blind) {
      this.blindShown = state.blind
      this.blindEnter = 0
      // **보스 차례가 되면 그것이 들립니다.** 판 셋 중 붉은 것 하나가 앞으로 나오는 것을
      // 눈으로만 알리면, 안테의 마지막이라는 것이 지나가 버립니다.
      if (state.blind === BlindKind.Boss) this.audio.play('boss_reveal')
    }

    const order = [BlindKind.Small, BlindKind.Big, BlindKind.Boss]
    const cardW = 226
    const gap = 20
    const cardH = 322
    // **아래에 붙입니다.** 조커 줄과 판 사이가 비면 화면이 위로 쏠리고, 판이 서는 자리는
    // 카드를 내는 자리와 같아야 눈이 옮겨 다니지 않습니다.
    const bottom = 754
    const startX = BOARD_X - ((order.length - 1) * (cardW + gap)) / 2 - cardW / 2

    order.forEach((blind, index) => {
      const row = this.data.tables.blind.getByBlindOrThrow(blind)
      const boss = blind === BlindKind.Boss
      const bossRow = boss ? this.data.tables.bossBlind.findByBossId(state.bossId) : undefined
      const now = blind === state.blind
      const done = blind < state.blind
      const tint = boss ? 0x8e3a5c : blind === BlindKind.Big ? 0x8a6a2e : 0x2f6a52

      // 건너뛸 수 있으면 태그 딱지가 들어갑니다. 보스는 건너뛸 수 없고, 이미 지난 것도
      // 건너뛸 것이 없습니다.
      const skippable = row.skippable && blind !== BlindKind.Boss && !done
      // **건너뛰면 무엇을 받는가.** 스몰과 빅이 나란히 서므로 둘 다 적혀 있어야 지금 것을
      // 건너뛸지 다음 것을 건너뛸지를 견줄 수 있습니다.
      const offer = skippable ? tagFor(state, blind) : undefined
      const tag = offer ? this.tagPlate(offer, cardW - 36) : undefined

      // 밑단에 쌓이는 것들의 높이. 아래에서 위로 쌓습니다.
      const stack: number[] = now
        ? [...(skippable ? [36, tag?.height ?? 0] : []), 44]
        : [20, ...(tag ? [tag.height] : [])]
      const stackH = stack.reduce((sum, one) => sum + one + 8, 0)

      const group = new Container()
      // 지금 차례인 것만 앞으로 나옵니다. **아랫변을 맞춥니다** — 위로 자라면 줄이
      // 들쭉날쭉해 보입니다. 밑단에 쌓인 만큼은 반드시 자랍니다.
      const height = Math.max(cardH + (now ? 26 : 0), 222 + stackH + 12)

      // 왼쪽부터 차례로 아래에서 올라옵니다. 셋이 같이 나타나면 셋이 한 덩어리로 보입니다.
      const enter = Math.max(0, Math.min(1, (this.blindEnter - index * 0.16) / 0.44))
      const eased = 1 - Math.pow(1 - enter, 3)
      group.position.set(startX + index * (cardW + gap),
        bottom - height + (1 - eased) * 70)
      group.alpha = (now ? 1 : done ? 0.5 : 0.72) * eased

      const plate = new Graphics()
      const radius = 14
      plate.roundRect(0, 0, cardW, height, radius)
        .fill({ color: now ? 0x18202e : 0x141a24, alpha: 0.97 })

      // 머리 띠. 어느 블라인드인지가 색으로 먼저 읽힙니다.
      //
      // **길 하나로 그립니다.** 둥근 사각형에 네모를 겹쳐 아랫단을 메우면 그 겹친 자리가
      // 두 번 칠해지고, 반투명일 때 그 띠가 그대로 보입니다.
      //
      // 그리고 **테두리보다 먼저입니다.** 나중에 그리면 띠의 모서리가 테두리 바깥으로
      // 넘칩니다 — 테두리는 반 칸 안쪽에 있어서 두 모서리의 호가 어긋납니다.
      const band = 46
      plate.moveTo(0, band)
        .lineTo(0, radius)
        .quadraticCurveTo(0, 0, radius, 0)
        .lineTo(cardW - radius, 0)
        .quadraticCurveTo(cardW, 0, cardW, radius)
        .lineTo(cardW, band)
        .closePath()
        .fill({ color: tint, alpha: now ? 0.95 : 0.6 })

      plate.roundRect(0.5, 0.5, cardW - 1, height - 1, radius)
        .stroke({ color: now ? tint : COLOR.panelEdge, width: now ? 3 : 1.5 })
      group.addChild(plate)

      const label = (text: string, size: number, fill: number, weight = '700') =>
        new Text({ text, style: { fontSize: size, fill, fontWeight: weight as never } })

      const name = label(bossRow
        ? nameOf(this.data, 'boss', state.bossId, bossRow.name)
        : tf('ui.blind.named', { name: blindName(blind) }), 17, COLOR.ink, '800')
      name.anchor.set(0.5, 0.5)
      name.position.set(cardW / 2, 23)
      group.addChild(name)

      // **보스에는 인장이 붙습니다.** 스물여덟이 이름 하나로만 갈리면 어느 것이 나왔는지가
      // 판마다 남지 않습니다. 이름 왼쪽이고, 이름은 그만큼 오른쪽으로 비켜섭니다.
      // **이름은 언제나 가운데입니다.** 인장이 붙는 보스만 이름을 오른쪽으로 비켜세웠고,
      // 그러면 셋이 나란히 섰을 때 보스의 이름만 다른 자리에 있습니다 — 인장은 띠의 왼쪽
      // 끝에 얹히는 것이지 이름과 한 줄로 서는 것이 아닙니다.
      const seal = this.blindFace(blind, 40)
      seal.position.set(30, 23)
      group.addChild(seal)

      // **세 자리마다 쉼표를 찍습니다.** 요구 점수는 안테가 오르면 네 자리 다섯 자리가
      // 되고, 쉼표가 없으면 30000 과 300000 을 한눈에 가릴 수 없습니다.
      const need = label(
        targetOf(this.data, state, blind).toLocaleString('en-US'), 34, COLOR.chips, '800')
      need.anchor.set(0.5, 0)
      need.position.set(cardW / 2, 72)
      group.addChild(need)

      const needCaption = label(t('ui.label.target'), 11, COLOR.inkDim)
      needCaption.anchor.set(0.5, 0)
      needCaption.position.set(cardW / 2, 114)
      group.addChild(needCaption)

      const reward = label(tf('ui.blind.reward',
        { n: rewardOf(this.data, this.state, row.blind) }), 13, COLOR.money, '800')
      reward.anchor.set(0.5, 0)
      reward.position.set(cardW / 2, 138)
      group.addChild(reward)

      // 보스의 효과. **건너뛸지를 정하는 것이 대부분 이 한 줄입니다.**
      const note = bossRow
        ? describe(this.data, this.data.bossEffects.get(state.bossId) ?? []).join(NEWLINE)
        : t('ui.note.no_rules')
      // **수와 이름은 다른 색입니다.** 「패에서 2장을 버립니다」에서 판단을 가르는 것은
      // 그 2 입니다.
      // **접습니다.** 접는 폭을 주지 않으면 한 줄로 뻗어 카드 밖으로 나갑니다 — 보스의
      // 효과는 「패에서 무늬가 같은 카드를 2장 버립니다」 처럼 깁니다.
      //
      // 그리고 **`richBlock` 으로 쌓습니다.** 줄마다 따로 그려 17픽셀씩 내리면, 접혀서 두
      // 줄이 된 것이 다음 줄 위에 겹칩니다.
      const noteWidth = cardW - 36
      const noteText = richBlock(note.split(NEWLINE), {
        base: { fontSize: 12, fill: boss ? 0xffb4c8 : COLOR.inkDim },
        number: COLOR.accentNumber,
        term: COLOR.accentTerm,
      }, 17, noteWidth, 'center')
      noteText.position.set((cardW - noteWidth) / 2, 172)
      group.addChild(noteText)

      // 아래에서 위로 쌓습니다. **아랫변이 맞아야 셋이 한 줄로 보입니다.**
      let at = height - 12
      const place = (node: Container, h: number): void => {
        at -= h
        node.position.set(18, at)
        at -= 8
        group.addChild(node)
      }

      if (done) {
        const mark = label(t('ui.label.cleared'), 14, COLOR.good, '800')
        mark.anchor.set(0.5, 0)
        mark.position.set(cardW / 2, height - 40)
        group.addChild(mark)
      } else if (!now) {
        const mark = label(t('ui.label.next_up'), 13, COLOR.inkDim, '700')
        mark.anchor.set(0.5, 0)
        mark.position.set(cardW / 2, height - 32)
        group.addChild(mark)
        at = height - 40
        if (tag) place(tag.node, tag.height)
      } else {
        if (skippable) {
          const skip = new Button(t('ui.button.skip'), cardW - 36, 36, 0x4a5568,
            () => {
              this.audio.play('blind_skip')
              this.act({ t: 'skip_blind' })
            })
          place(skip, 36)
          this.spots.skip = { x: group.x + cardW / 2, y: group.y + skip.y + 18 }
          // **무엇을 받는지가 그 버튼 위에 적혀 있습니다.** 적혀 있지 않으면 건너뛸지를
          // 찍게 됩니다 — 태그 하나가 조커 하나이거나 바우처 하나입니다.
          if (tag) place(tag.node, tag.height)
        }
        const pick = new Button(t('ui.button.select_blind'), cardW - 36, 44, 0x2f6fb5,
          () => this.act({ t: 'select_blind' }))
        place(pick, 44)
        this.spots.pick = { x: group.x + cardW / 2, y: group.y + pick.y + 22 }
      }

      this.blindPick.addChild(group)
    })
  }

  /**
   * 덱에 남은 카드.
   *
   * **덱을 그대로 펼칩니다.** 숫자로 세어 놓으면 「스페이드가 4장」은 읽히지만 그것이 어느
   * 4장인지는 읽히지 않고, 강화가 붙은 카드가 아직 남았는지는 아예 보이지 않습니다.
   *
   * 무늬마다 한 줄이고, 카드는 **옆으로 겹쳐** 놓입니다 — 겹치면 한 장을 크게 그리고도
   * 13장이 한 줄에 들어가고, 겹친 쪽이 손에 쥔 부챗살과 같은 모습입니다. 아직 뽑지 않은
   * 것만 밝게 두어 남은 것이 무엇인지가 한눈에 갈립니다.
   *
   * 카드를 누르면 그 한 장의 설명이 뜹니다. 강화와 인장과 에디션은 얼굴의 색과 점 하나로만
   * 구분되므로, **누르면 글로 읽을 수 있어야 합니다.**
   */
  /**
   * 태그 딱지 하나.
   *
   * **이름과 하는 일이 함께 적혀 있어야 합니다.** 이름만 있으면 「저글 태그」 가 무엇인지
   * 모르는 채로 건너뛸지를 정하게 됩니다.
   */
  private tagPlate(tagId: string, width: number): { node: Container; height: number } {
    const lines = describe(this.data, this.data.tagEffects.get(tagId) ?? [])

    // **그림이 읽힐 만큼은 되어야 합니다.** 22픽셀에서는 색깔 있는 점 하나이고, 그러면
    // 태그마다 그림이 다르다는 것 자체가 보이지 않습니다.
    const FACE = 40
    const textLeft = 12 + FACE

    // **글을 먼저 만들고 딱지의 높이를 그것에 맞춥니다.** 못박으면 두 줄인 태그에서 아랫줄이
    // 딱지 밖으로 나갑니다 — 어느 태그의 설명이 긴지는 데이터가 정합니다.
    //
    // **접는 폭은 실제로 놓일 폭입니다.** 넓게 잡아 높이를 재고 나서 좁게 다시 접었고,
    // 좁으면 줄이 늘어나므로 잰 높이보다 커집니다 — 딱지 밖으로 나가던 것이 그것입니다.
    //
    // 그리고 **낱말 사이를 찾지 못하면 글자에서 끊습니다.** 일본어와 중국어는 띄어쓰기가
    // 없어서, 낱말 경계만 찾는 접기로는 한 줄이 그대로 뻗습니다.
    const note = new Text({
      text: lines.join(' · '),
      style: {
        fontSize: 10, fill: COLOR.inkDim,
        wordWrap: true, wordWrapWidth: width - textLeft - 8, breakWords: true,
        lineHeight: 12,
      },
    })
    const height = Math.max(FACE + 12, 20 + note.height + 8)

    const node = new Container()
    const plate = new Graphics()
    plate.roundRect(0, 0, width, height, 8).fill({ color: 0x1c2436, alpha: 0.95 })
    plate.roundRect(0.5, 0.5, width - 1, height - 1, 8)
      .stroke({ color: COLOR.accentTerm, width: 1.5, alpha: 0.7 })
    node.addChild(plate)

    const face = this.tagFace(tagId, FACE)
    face.position.set(6 + FACE / 2, height / 2)
    node.addChild(face)

    const name = new Text({
      text: nameOf(this.data, 'tag', tagId, tagId),
      style: { fontSize: 12, fill: COLOR.ink, fontWeight: '800' },
    })
    name.position.set(textLeft, 6)
    node.addChild(name)

    note.position.set(textLeft, 22)
    node.addChild(note)

    node.eventMode = 'static'
    node.hitArea = new Rectangle(0, 0, width, height)
    this.tipOn(node, () => {
      const spot = this.overlay.toLocal(node.toGlobal({ x: width / 2, y: height }))
      this.tooltip.show(nameOf(this.data, 'tag', tagId, tagId), t('ui.kind.tag'), 0, lines,
        spot.x, spot.y, SIZE)
    })
    return { node, height }
  }

  /**
   * 태그의 얼굴.
   *
   * 그림이 있으면 그림, 없으면 문양입니다 — **그림이 오기 전에도 종류가 갈려 보여야
   * 합니다.**
   */
  private tagFace(tagId: string, size: number): Container {
    const face = new Container()
    const texture = artFor('tag', tagId)
    if (texture) {
      // **동그라미로 오려 냅니다.** 그림마다 바탕의 여백이 조금씩 달라서 그대로 넣으면
      // 어떤 것은 네모 액자에 든 칩으로 보입니다 — 칩만 남기면 그 차이가 없어집니다.
      const grown = size * 1.16
      const sprite = new Sprite(texture)
      sprite.width = grown
      sprite.height = grown
      sprite.position.set(-grown / 2, -grown / 2)

      const round = new Graphics()
      round.circle(0, 0, size / 2).fill(0xffffff)
      sprite.mask = round

      face.addChild(round, sprite)
      return face
    }

    // 문양 하나와 그 태그의 색. 색은 이름에서 나오므로 태그마다 다릅니다.
    const hue = hashOf(tagId) % 360
    const art = new Graphics()
    art.circle(0, 0, size / 2).fill({ color: hsl(hue, 0.5, 0.32) })
    art.circle(0, 0, size / 2).stroke({ color: hsl(hue, 0.6, 0.6), width: 1.5 })
    drawGlyph(art, glyphFor(tagId), 0, 0, size * 0.3, {
      fill: hsl(hue, 0.7, 0.78), line: hsl(hue, 0.4, 0.22), weight: 1.4,
    })
    face.addChild(art)
    return face
  }

  /**
   * 보스의 인장.
   *
   * **보스마다 다른 표시가 있어야 합니다.** 이름과 효과만 적혀 있으면 스물여덟이 한 갈래로
   * 보이고, 어느 것이 나왔는지가 판마다 남지 않습니다 — 원작에서도 보스는 저마다 다른
   * 표시를 답니다.
   *
   * 그림이 있으면 그림, 없으면 문양입니다 — 문양은 식별자에서 나오므로 보스마다 다릅니다.
   */
  /**
   * 블라인드의 표시.
   *
   * **셋 다 답니다.** 보스에만 인장이 붙어 있었고, 그러면 나란히 선 셋 중 하나만 표시를
   * 가진 것이 되어 그 셋이 같은 갈래로 보이지 않습니다 — 스몰과 빅은 같은 모양의 딱지이고
   * 크기와 색으로 갈립니다. 어느 라운드인지가 이름을 읽지 않아도 표시로 먼저 읽힙니다.
   */
  private blindFace(blind: BlindKind, size: number): Container {
    if (blind === BlindKind.Boss) return this.bossFace(this.state.bossId, size)

    const big = blind === BlindKind.Big
    const tint = big ? 0xa279e0 : 0x5d92d6
    const face = new Container()
    const art = new Graphics()

    // 딱지 하나. **가운데의 원이 크기로 갈립니다** — 빅이 스몰보다 큽니다.
    art.circle(0, 0, size / 2).fill({ color: mix(tint, 0x000000, 0.55) })
    art.circle(0, 0, size / 2).stroke({ color: tint, width: 2 })
    art.circle(0, 0, size * (big ? 0.28 : 0.19)).fill({ color: tint, alpha: 0.9 })

    // 가장자리의 눈금 여덟. 딱지가 그냥 동그라미가 아니라 표식으로 보입니다.
    for (let i = 0; i < 8; i++) {
      const angle = (i / 8) * Math.PI * 2
      const from = size * 0.37
      const to = size * 0.45
      art.moveTo(Math.cos(angle) * from, Math.sin(angle) * from)
        .lineTo(Math.cos(angle) * to, Math.sin(angle) * to)
        .stroke({ color: tint, width: 1.4, alpha: 0.85 })
    }

    face.addChild(art)
    return face
  }

  private bossFace(bossId: string, size: number): Container {
    const face = new Container()
    const texture = artFor('boss', bossId)
    if (texture) {
      const grown = size * 1.16
      const sprite = new Sprite(texture)
      sprite.width = grown
      sprite.height = grown
      sprite.position.set(-grown / 2, -grown / 2)

      const round = new Graphics()
      round.circle(0, 0, size / 2).fill(0xffffff)
      sprite.mask = round

      face.addChild(round, sprite)
      return face
    }

    // 붉은 돌 하나에 새긴 표시. **색은 식별자에서 나오므로 보스마다 다릅니다.**
    const hue = 320 + (hashOf(bossId) % 60)
    const art = new Graphics()
    art.circle(0, 0, size / 2).fill({ color: hsl(hue % 360, 0.42, 0.20) })
    art.circle(0, 0, size / 2).stroke({ color: hsl(hue % 360, 0.55, 0.52), width: 2 })
    // 테두리의 눈금. 태그의 칩과 갈리는 것이 이것입니다.
    for (let i = 0; i < 12; i++) {
      const angle = (i / 12) * Math.PI * 2
      const inner = size / 2 - 4
      const outer = size / 2 - 1
      art.moveTo(Math.cos(angle) * inner, Math.sin(angle) * inner)
        .lineTo(Math.cos(angle) * outer, Math.sin(angle) * outer)
        .stroke({ color: hsl(hue % 360, 0.5, 0.62), width: 1.2, alpha: 0.8 })
    }
    drawGlyph(art, glyphFor(bossId), 0, 0, size * 0.28, {
      fill: hsl(hue % 360, 0.7, 0.8), line: hsl(hue % 360, 0.4, 0.14), weight: 1.6,
    })
    face.addChild(art)
    return face
  }

  /**
   * 들고 있는 태그.
   *
   * **상점에 들어갈 때까지 들고 있는 것입니다.** 「적용 중」 목록의 한 줄로만 두면 그것이
   * 지금 들고 있는 물건이라는 것이 읽히지 않습니다 — 조커와 소모품 줄 옆에 딱지로 섭니다.
   */
  /**
   * 들고 있는 태그.
   *
   * **이제 블라인드 딱지 안에 섭니다**(`tagChips`). 화면 오른쪽 위에 따로 두었는데, 그쪽
   * 끝은 덱과 소모품 칸이 이미 쓰고 있어서 태그가 그 둘 사이에 낀 셋째 줄처럼 보였고,
   * 무엇에 딸린 것인지도 끊겼습니다 — 태그는 다음 상점까지 들고 있는 것이므로 지금
   * 무엇과 붙고 있는지를 적은 그 딱지가 그 자리입니다.
   */
  private syncTags(): void {
    this.tagLayer.visible = false
  }

  private drawDeckView(): void {
    const layer = this.deckView.view
    layer.removeChildren().forEach(child => child.destroy())

    const state = this.state
    const suits = [...this.data.tables.suit.records].sort((a, b) => a.sortOrder - b.sortOrder)
    const alive = new Set(state.drawPile)
    const held = new Set(this.shown.hand)

    const rows = suits.map(suitRow => ({
      suit: suitRow,
      cards: state.deck
        .filter(card => card.suit === suitRow.suit)
        .sort((a, b) => a.rank - b.rank),
    }))
    const widest = Math.max(1, ...rows.map(row => row.cards.length))

    const cardW = 56
    const cardH = 79
    // 겹치는 폭. **얼굴의 왼쪽 절반이 보이면 랭크와 무늬가 읽힙니다.**
    const step = 30
    const left = 84
    const rowH = cardH + 16
    const width = left + (widest - 1) * step + cardW + 26
    const gridTop = TITLE_BAR + 62
    const height = gridTop + rows.length * rowH + 16 + FOOTER_BAR

    layer.addChild(panelFrame(width, height, t('ui.button.deck_view'), () => this.toggleDeckView()))

    const label = (text: string, size: number, fill: number, weight = '700') =>
      new Text({ text, style: { fontSize: size, fill, fontWeight: weight as never } })

    const total = label(`${state.drawPile.length} / ${state.deck.length}`, 15, COLOR.chips, '800')
    total.anchor.set(0.5, 0)
    total.position.set(width / 2, TITLE_BAR + 12)
    const legend = label(
      t('ui.deck_view.note'),
      11, COLOR.inkDim, '600')
    legend.anchor.set(0.5, 0)
    legend.position.set(width / 2, TITLE_BAR + 34)
    layer.addChild(total, legend)

    rows.forEach((row, line) => {
      const y = gridTop + line * rowH
      const leftIn = row.cards.filter(card => alive.has(card.uid)).length

      // **어두운 판 위이므로 검정이 아니라 잉크색으로 그립니다.** 세트가 검정으로 정한
      // 무늬는 이 판에서 보이지 않습니다.
      const dark = suitInk(row.suit.suit) === COLOR.black
      const mark = label(SUIT_PIP[row.suit.suit] ?? row.suit.letter, 26,
        dark ? COLOR.ink : suitInk(row.suit.suit), '800')
      mark.anchor.set(0.5, 0)
      mark.position.set(34, y + 16)
      const count = label(`${leftIn}/${row.cards.length}`, 11, COLOR.inkDim, '700')
      count.anchor.set(0.5, 0)
      count.position.set(34, y + 50)
      layer.addChild(mark, count)

      row.cards.forEach((card, index) => {
        const mini = this.miniCard(card, alive.has(card.uid), cardW, cardH)
        mini.position.set(left + index * step, y)
        // **오른쪽이 위입니다.** 손패를 부챗살로 펴는 것과 같은 순서라 눈이 헷갈리지 않습니다.
        mini.zIndex = index
        mini.eventMode = 'static'
        // 겹쳐 놓았으므로 **보이는 만큼만** 잡습니다. 카드 전체를 잡으면 뒤의 카드가
        // 앞의 카드에 가려 눌리지 않습니다.
        mini.hitArea = new Rectangle(0, 0,
          index === row.cards.length - 1 ? cardW : step, cardH)
        mini.cursor = 'pointer'
        mini.on('pointertap', event => {
          event.stopPropagation()
          if (this.ate()) return
          this.showCardTip(card, alive.has(card.uid), held.has(card.uid), mini)
        })
        layer.addChild(mini)
      })
    })

    layer.sortableChildren = true

    const remaining = state.deck.filter(card => alive.has(card.uid))
    const faces = remaining.filter(
      card => this.data.tables.rank.findByRank(card.rank)?.isFace).length
    const aces = remaining.filter(card => card.rank === RankKind.Ace).length
    const enhanced = remaining.filter(card => card.enhancement !== EnhancementKind.None).length
    const sealed = remaining.filter(card => card.seal !== SealKind.None).length

    const foot = label(
      tf('ui.deck_view.counts', { faces, aces, enhanced, sealed }),
      12, COLOR.inkDim)
    foot.anchor.set(0.5, 1)
    foot.position.set(width / 2, height - FOOTER_BAR - 10)
    layer.addChild(foot)

    this.deckView.size.width = width
    this.deckView.size.height = height
  }

  /**
   * 덱 판에 놓이는 카드 한 장.
   *
   * **손패의 카드와 같은 그림입니다.** 작다고 다른 얼굴을 쓰면 판을 보고 손패를 찾을 때
   * 한 번 더 옮겨 읽어야 합니다.
   */
  private miniCard(card: CardInstance, alive: boolean, w: number, h: number): Container {
    const node = new Container()
    const paint = MINI_TINT[card.enhancement] ?? cardPaper()

    // **나간 카드도 불투명합니다.** 반투명하면 뒤의 카드가 비쳐 겹친 자리가 지저분해지고,
    // 겹쳐 놓은 줄에서는 그 자리가 카드마다 다릅니다 — 어둡게만 두면 깔끔합니다.
    const body = new Graphics()
    body.roundRect(0, 0, w, h, 5).fill(alive ? cardPaper() : 0x39414f)
    node.addChild(body)

    const ink = alive ? suitInk(card.suit) : 0x5d6879
    const dir = cardArtDir()
    const texture = dir === undefined
      ? undefined : artFor(dir, cardArtId(card.suit, card.rank))
    if (texture) {
      const picture = new Sprite(texture)
      picture.width = w
      picture.height = h
      picture.tint = alive ? paint : 0x4c5566
      node.addChild(picture)
    }

    // **모서리의 랭크는 그림 위에도 적힙니다.** 정본 한 벌만 그림에 랭크가 들어 있고,
    // 우리가 굽는 세트는 그림 카드 12컷뿐입니다 — 여기서 빼면 이 판에서 J·Q·K 를 서로
    // 구별할 수 없습니다.
    if (texture === undefined || drawsIndex()) {
      const face = new Graphics()
      if (texture === undefined) drawFace(face, card.suit, card.rank, w, h, ink)
      const rank = new Text({
        text: MINI_RANK[card.rank] ?? '?',
        style: { fontSize: 11, fill: ink, fontWeight: '800' },
      })
      rank.position.set(3, 1)
      node.addChild(face, rank)
    }

    // **테두리는 그림 위에 그립니다.** 그림이 카드를 덮으므로 종이에 그으면 가려집니다.
    const edge = new Graphics()
    edge.roundRect(0.5, 0.5, w - 1, h - 1, 5)
      .stroke({ color: alive ? COLOR.cardEdge : 0x2a3140, width: 1 })
    node.addChild(edge)

    if (card.seal !== SealKind.None) {
      const seal = new Graphics()
      seal.circle(w - 9, 9, 4.5)
        .fill({ color: MINI_SEAL[card.seal] ?? COLOR.ink, alpha: alive ? 1 : 0.4 })
      node.addChild(seal)
    }
    if (card.edition !== EditionKind.Base) {
      const spark = new Graphics()
      spark.roundRect(3, h - 8, w - 6, 4, 2)
        .fill({ color: COLOR.mult, alpha: alive ? 0.9 : 0.3 })
      node.addChild(spark)
    }

    return node
  }

  /**
   * 덱 판에서 카드 한 장을 눌렀을 때.
   *
   * **지금 어디에 있는가가 첫 줄입니다** — 덱에 남았는지, 손에 있는지, 이미 나갔는지가
   * 이 판을 여는 이유이기 때문입니다.
   */
  private showCardTip(card: CardInstance, alive: boolean, inHand: boolean,
                      at: Container): void {
    const rank = this.data.tables.rank.findByRank(card.rank)
    const name = `${MINI_RANK[card.rank] ?? '?'} ${SUIT_PIP[card.suit] ?? ''}`

    const lines: string[] = [
      alive ? t('ui.deck.left') : inHand ? t('ui.deck.in_hand') : t('ui.deck.gone'),
      tf('ui.card.chips', { n: (rank?.chips ?? 0) + card.bonusChips })
        + (card.bonusChips > 0 ? tf('ui.card.chips_split', { base: rank?.chips ?? 0, bonus: card.bonusChips }) : ''),
    ]
    if (card.enhancement !== EnhancementKind.None) {
      lines.push(tf('ui.card.enhancement', { name: this.enhancementName(card.enhancement) }))
    }
    if (card.seal !== SealKind.None) lines.push(tf('ui.card.seal', { name: this.sealName(card.seal) }))
    if (card.edition !== EditionKind.Base) {
      lines.push(tf('ui.card.edition', { name: this.editionName(card.edition) }))
    }
    if (card.debuffed) lines.push(t('ui.note.disabled_this_round'))

    const spot = this.world.toLocal(at.getGlobalPosition())
    this.tooltip.show(name, alive ? t('ui.kind.deck') : inHand ? t('ui.kind.hand') : t('ui.kind.gone'), alive ? 3 : 1,
      lines, spot.x + 30, spot.y + 46, { width: SIZE.width, height: SIZE.height })
  }

  /**
   * 표시 이름 셋.
   *
   * **표의 `display` 를 거쳐 글 표로 갑니다** — 이름을 화면에 손으로 적으면 지역화가 그
   * 자리를 지나칩니다.
   */
  private enhancementName(kind: EnhancementKind): string {
    return this.localized(this.data.tables.enhancement.findByEnhancement(kind)?.display)
      ?? EnhancementKind[kind]
  }

  private sealName(kind: SealKind): string {
    return this.localized(this.data.tables.seal.findBySeal(kind)?.display) ?? SealKind[kind]
  }

  private editionName(kind: EditionKind): string {
    return this.localized(this.data.tables.edition.findByEdition(kind)?.display)
      ?? EditionKind[kind]
  }

  /**
   * 규칙 하나의 이름.
   *
   * **글 표에서 옵니다.** `RuleKind` 의 이름은 `AllCardsScore` 같은 식별자이고, 그것이
   * 화면에 그대로 뜨면 무엇이 바뀐 것인지 읽을 수 없습니다.
   */
  private ruleName(rule: string): string {
    return text(this.data, `rule.${snake(rule)}.name`)
  }

  /** 글 표에 있으면 그 말, 없으면 적힌 그대로. */
  private localized(key: string | undefined): string | undefined {
    if (key === undefined || key === '') return undefined
    return text(this.data, key)
  }

  /**
   * 끝났을 때 덮는 판.
   *
   * **지고 나서 아무것도 없는 것이 가장 나쁩니다.** 어디까지 갔는지 보여주고 다시 시작할
   * 자리를 둡니다.
   */
  private drawGameOver(): void {
    const done = this.state.phase === 'lost' || this.state.phase === 'won'
    if (!done) {
      this.gameOver.removeChildren().forEach(child => child.destroy())
      this.gameOver.visible = false
      this.gameOverShown = false
      return
    }

    // **연출이 끝나기 전에는 띄우지 않습니다.** 마지막 카드를 낸 결과를 보기도 전에 판이
    // 덮이면 무엇 때문에 끝난 것인지 알 수 없습니다. `tick` 이 조건을 보고 부릅니다.
    if (this.gameOverShown) return
    this.gameOverShown = true
    this.gameOverPop = 1

    const won = this.state.phase === 'won'
    this.gameOver.removeChildren().forEach(child => child.destroy())
    this.gameOver.visible = true

    // 판 하나가 뜨는 것과 같은 정도로 덮습니다. 끝난 판이라고 더 짙게 덮으면, 끝난
    // 화면만 다른 규칙으로 그려집니다.
    const veil = new Graphics()
    veil.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000)
      .fill({ color: 0x070a10, alpha: 0.66 })
    this.gameOver.addChild(veil)

    const board = new Container()
    const width = 480
    // **랭크 런이면 그만큼 큽니다.** 순위 두 줄이 들어갈 자리를 미리 두지 않으면 그 줄이
    // 시드 줄과 겹칩니다 — 판정은 나중에 오지만 자리는 지금 정해야 합니다.
    const ranked = this.hub.isRanked(this.state.seed)
    const height = 352 + (ranked ? 58 : 0)

    const plate = new Graphics()
    plate.roundRect(-width / 2, -height / 2, width, height, 16)
      .fill(won ? 0x1d2c22 : 0x2c1a22)
    plate.roundRect(-width / 2 + 0.5, -height / 2 + 0.5, width - 1, height - 1, 16)
      .stroke({ color: won ? COLOR.good : COLOR.bad, width: 2.5 })
    board.addChild(plate)

    const title = new Text({
      text: won ? t('ui.label.won') : t('ui.label.lost'),
      style: {
        fontSize: 52, fill: won ? COLOR.good : COLOR.bad, fontWeight: '800',
        stroke: { color: 0x0a0f18, width: 6 },
      },
    })
    title.anchor.set(0.5)
    title.position.set(0, -height / 2 + 62)

    const lead = new Text({
      text: this.endLine(won),
      style: {
        fontSize: 15, fill: COLOR.ink, fontWeight: '700',
        wordWrap: true, wordWrapWidth: width - 60, breakWords: true, align: 'center',
      },
    })
    lead.anchor.set(0.5, 0)
    lead.position.set(0, -height / 2 + 100)

    const lines = [
      tf('ui.stat.ante', { at: this.state.ante, all: this.data.run.winAnte }),
      tf('ui.stat.hands_played', { n: this.state.handsPlayedThisRun }),
      tf('ui.stat.jokers', { n: this.state.jokers.length }),
      tf('ui.stat.seed', { seed: this.state.seed }),
    ]
    const body = new Text({
      text: lines.join('\n'),
      style: { fontSize: 14, fill: 0xd2dcea, lineHeight: 24, align: 'center' },
    })
    body.anchor.set(0.5, 0)
    body.position.set(0, -height / 2 + 150)

    // **둘 다 페이지를 다시 읽지 않습니다.** 판을 접는 것은 화면이 하는 일이고, 다시
    // 읽으면 데이터·글꼴·그림을 처음부터 읽어 로딩 씬이 한 번 더 보입니다.
    const again = new Button(t('ui.button.restart'), 168, 52, won ? 0x2f7a52 : 0xa63f3f,
      () => this.restartRun())
    again.position.set(-176, height / 2 - 76)
    const home = new Button(t('ui.button.to_title'), 168, 52, 0x4a5568,
      () => this.enterTitle())
    home.position.set(8, height / 2 - 76)

    board.addChild(title, lead, body, again, home)
    board.position.set(POPUP_X, SIZE.height / 2)
    this.gameOver.addChild(board)
    this.gameOverBoard = board

    // **순위는 판정이 온 뒤에 붙습니다.** 랭크 런이 아니면 붙는 것이 없고, 그때의 판은
    // 지금과 한 줄도 다르지 않습니다.
    if (ranked) {
      this.rankNode = new Container()
      this.rankNode.position.set(0, height / 2 - 145)
      board.addChild(this.rankNode)
      void this.judgeRun()
    }
    this.gameOver.zIndex = 10_000

    // 럼블. **판이 그냥 나타나면 아무 무게가 없습니다.** 다만 무게는 색수차와 배경이
    // 지고, 흔들림은 거들기만 합니다 — 판이 뜨는 그 순간에 화면까지 크게 움직이면
    // 판을 읽으려는 눈이 먼저 흔들립니다.
    this.audio.play(won ? 'run_win' : 'run_lose')
    this.jolt(won ? 8 : 6, won ? 3.4 : 2.6, 1)
    this.flashScreen(won ? COLOR.money : COLOR.bad, won ? 0.5 : 0.34)
    if (won) this.particles.burst(POPUP_X, SIZE.height / 2, 90, COLOR.money, 2.6)
  }

  /**
   * 끝난 런을 올리고 그 결과를 판에 적습니다.
   *
   * **랭크 런이 아니면 아무것도 하지 않습니다.**
   */
  private async judgeRun(): Promise<void> {
    if (!this.hub.isRanked(this.state.seed)) return

    this.rankLine = { text: t('ui.lb.end.judging'), tone: COLOR.inkDim }
    this.drawRankLine()

    const line = await this.hub.finishRun(this.state, this.actions, this.metrics)
    this.rankLine = line
    this.rankRoll = line?.from ?? line?.to ?? 0
    this.drawRankLine()
    if (!line) return

    // **순위가 오른 것은 연출이 있어야 무게가 있습니다.** 그냥 적혀 있으면 아무 일도
    // 아닙니다.
    if (line.moved !== undefined && line.moved > 0) {
      this.audio.play('blind_clear')
      this.jolt(4, 2.2, 1)
      if (line.moved >= 25) {
        this.particles.burst(POPUP_X, SIZE.height / 2, 40, COLOR.money, 2.2)
      }
    }
    if (line.tier !== undefined) {
      this.audio.play('run_win')
      this.flashScreen(COLOR.money, 0.3)
      this.toasts.push(t('ui.lb.title'), tf('ui.lb.end.tierUp', { tier: line.tier }),
                       COLOR.money, 3.4)
    }
  }

  /**
   * 순위 숫자를 새 자리까지 굴립니다.
   *
   * **내려가는 동안 소리가 한 음씩 오릅니다.** 득점 연출의 카운터와 같은 규칙이고, 그
   * 규칙이 같아야 이 판의 것으로 읽힙니다.
   */
  private rollRank(seconds: number): void {
    const line = this.rankLine
    if (!line || line.to === undefined || line.from === undefined) return
    if (Math.round(this.rankRoll) === line.to) return

    const span = Math.max(1, Math.abs(line.from - line.to))
    const before = Math.round(this.rankRoll)
    // **폭이 넓어도 곧 끝납니다.** 자리마다 같은 속도로 굴리면 100자리가 오른 판이
    // 한참 동안 숫자만 굴립니다.
    this.rankRoll += (line.to - this.rankRoll) * Math.min(1, seconds * 5)
    if (Math.abs(this.rankRoll - line.to) < 0.6) this.rankRoll = line.to

    const now = Math.round(this.rankRoll)
    if (now !== before) {
      const step = 1 - Math.abs(now - line.to) / span
      this.audio.play('coin_land', Math.floor(step * 12))
      this.drawRankLine()
    }
  }

  /**
   * 순위 한 줄을 그립니다.
   *
   * **숫자가 굴러 내려갑니다.** 예전 순위에서 새 순위로 내려가는 동안 그 값을 글에
   * 끼워 넣으므로, 그 사이에는 매 프레임 다시 그립니다.
   */
  private drawRankLine(): void {
    const node = this.rankNode
    if (!node) return
    node.removeChildren().forEach(child => child.destroy({ children: true }))

    const line = this.rankLine
    if (!line) return

    const rolling = line.to !== undefined && Math.round(this.rankRoll) !== line.to
    const shown = rolling ? line.text.replace(RANK_MARK, '#' + String(Math.round(this.rankRoll))) : line.text

    const text = new Text({
      text: shown,
      style: {
        fontSize: 14, fill: line.tone, fontWeight: '700',
        wordWrap: true, wordWrapWidth: 420, align: 'center',
      },
    })
    text.anchor.set(0.5, 0)
    node.addChild(text)

    // 나머지 보드는 작은 글로. **하나씩 다 연출하면 끝난 판이 30초가 됩니다.**
    const others = this.hub.otherRanks()
    if (others !== '' && line.moved !== undefined) {
      const small = new Text({
        text: others,
        style: {
          fontSize: 11, fill: 0x7d8ca0, wordWrap: true, wordWrapWidth: 420,
          align: 'center',
        },
      })
      small.anchor.set(0.5, 0)
      small.position.set(0, text.height + 4)
      node.addChild(small)
    }
  }

  /** 왜 끝났는가. **숫자가 있어야 다음 판에 무엇을 다르게 할지 압니다.** */
  private endLine(won: boolean): string {
    if (won) return tf('ui.over.won', { n: this.data.run.winAnte })
    const short = Number(this.state.target) - Number(this.state.score)
    const where = tf('ui.over.where', { ante: this.state.ante, blind: blindName(this.state.blind) })
    return short > 0
      ? tf('ui.over.short', { where, n: short.toLocaleString('en-US') })
      : tf('ui.over.stopped', { where })
  }

  /** 럼블. 크기가 넘쳤다가 잦아들고, 그동안 판이 조금씩 떱니다. */
  private advanceGameOver(seconds: number): void {
    const board = this.gameOverBoard
    if (!board || this.gameOverPop <= 0) return

    this.gameOverPop = Math.max(0, this.gameOverPop - seconds / 0.62)
    const t = 1 - this.gameOverPop
    // 0.45 에서 1.16 을 지나 1 로.
    const scale = t < 0.42
      ? 0.45 + (1.16 - 0.45) * (t / 0.42)
      : 1.16 - 0.16 * ((t - 0.42) / 0.58)
    const shiver = this.gameOverPop * this.gameOverPop * 12

    board.scale.set(scale)
    board.position.set(
      POPUP_X + (Math.random() - 0.5) * shiver,
      SIZE.height / 2 + (Math.random() - 0.5) * shiver)
    board.rotation = (Math.random() - 0.5) * shiver * 0.0022

    if (this.gameOverPop <= 0) {
      board.scale.set(1)
      board.position.set(POPUP_X, SIZE.height / 2)
      board.rotation = 0
    }
  }

  private syncBadge(): void {
    const state = this.state

    // **태그는 연출과 상관없이 지금 것입니다.** 딱지 전체는 연출이 끝난 뒤에 바꾸지만,
    // 태그는 그 연출 안에서 들어오므로 함께 묶으면 딱지가 한 번씩 뒤처집니다 — 첫 스킵의
    // 태그가 보이지 않고 다음 스킵에서야 그 앞의 것이 뜨던 것이 그것입니다.
    this.badge.setTags(this.tagChips())

    // 연출이 도는 중에는 앞 국면의 딱지를 그대로 둡니다.
    if (!this.presented) return

    if (state.phase === 'shop') {
      this.badge.set(t('ui.guide.shop.head'), 0, 0,
        t('ui.shop.note'), false)
      return
    }

    const boss = state.blind === BlindKind.Boss
    const bossRow = boss ? this.data.tables.bossBlind.findByBossId(state.bossId) : undefined

    const note = bossRow
      ? describe(this.data, this.data.bossEffects.get(state.bossId) ?? []).join(' · ')
      : ''

    this.badge.set(
      bossRow
        ? nameOf(this.data, 'boss', state.bossId, bossRow.name)
        : tf('ui.blind.named', { name: blindName(state.blind) }),
      Number(state.target), rewardOf(this.data, state, state.blind), note, boss,
      state.blind === BlindKind.Big,
      // **판이 도는 내내 보이는 자리입니다.** 고르는 판은 한 번 지나가지만 이 딱지는
      // 남습니다 — 어느 보스와 붙고 있는지가 여기 있어야 합니다.
      this.blindFace(state.blind, 34),
      // 들고 있는 태그. **딱지 안 아래에 가운데로 섭니다** — 화면 구석에 따로 두었더니
      // 무엇에 딸린 것인지가 끊겼고, 조커 줄과 덱 사이에 낀 셋째 줄처럼 보였습니다.
      this.tagChips())
  }

  /**
   * 들고 있는 태그의 칩들.
   *
   * **셋까지입니다.** 그보다 많이 들고 있는 일은 드물고, 넷째부터는 딱지가 그만큼 자라서
   * 그 아래의 점수 칸을 밀어냅니다.
   */
  private tagChips(): Container[] {
    if (this.scene !== 'run') return []
    // 머리띠 안에 앉으므로 띠보다 작아야 합니다. 띠가 32이고 그 안에 26입니다.
    const size = 26

    // 안테가 바뀌면 쓴 것의 줄을 비웁니다.
    if (this.tagSpentAnte !== this.state.ante) {
      this.tagSpentAnte = this.state.ante
      this.tagSpent = []
    }

    // **받은 순서 그대로입니다.** 쓴 것이 먼저이고 들고 있는 것이 뒤입니다 — 새로 받은
    // 것이 바깥쪽에 서야 방금 무엇이 생겼는지가 자리로도 읽힙니다.
    const held = this.state.tagsPending
    const spent = this.tagSpent.filter(one => !held.includes(one))
    return [...spent, ...held].slice(-4).map(tagId => {
      const used = !held.includes(tagId)
      const lines = describe(this.data, this.data.tagEffects.get(tagId) ?? [])
      const cell = new Container()
      // **피벗은 늘 가운데입니다.** 발동할 때만 옮기면 그 순간에 자리가 한 번 바뀌고,
      // 세우는 쪽이 그것을 되돌려도 두 프레임에 걸쳐 흔들립니다 — 처음부터 가운데면
      // 부풀리는 것이 자리를 건드리지 않습니다.
      cell.pivot.set(size / 2, size / 2)

      // **그림이 이미 칩입니다.** 그 뒤에 또 네모 딱지를 깔면 칩이 액자에 든 것으로
      // 보입니다 — 그림이 없을 때만 딱지를 깝니다.
      const texture = artFor('tag', tagId)
      if (!texture) {
        const plate = new Graphics()
        plate.roundRect(0, 0, size, size, 8).fill({ color: 0x1c2436, alpha: 0.95 })
        plate.roundRect(0.5, 0.5, size - 1, size - 1, 8)
          .stroke({ color: COLOR.accentTerm, width: 1.5, alpha: 0.7 })
        cell.addChild(plate)
      }
      const face = this.tagFace(tagId, texture ? size : size - 10)
      face.position.set(size / 2, size / 2)
      cell.addChild(face)

      // **쓴 것은 흐리게 남습니다.** 지우면 그 자리에서 쓰이는 태그가 화면에 한 프레임도
      // 서지 못하고, 같은 밝기로 두면 아직 들고 있는 것과 갈리지 않습니다.
      if (used) cell.alpha = 0.42

      // **다만 발동하는 그 순간에는 켜집니다.** 그 태그가 한 일이 그 순간이고, 흐려지는
      // 것은 그 뒤에 남는 상태입니다.
      const fire = this.tagFire.get(tagId)
      if (fire !== undefined && fire >= 0) {
        const wave = Math.sin(fire * Math.PI)
        cell.alpha = 0.42 + wave * 0.58
        cell.scale.set(1 + wave * 0.3)
        const lit = new ArriveFilter()
        lit.at(this.clock)
        lit.flash = wave * 0.9
        lit.warp = wave * 0.35
        cell.filters = [lit]
      }

      // 새로 선 칩 하나만 한 번 하얗게 번쩍이며 나옵니다.
      //
      // **셰이더를 걸지 않습니다.** `ArriveFilter` 는 카드 한 장의 크기에 맞춰 여백을 잡아
      // 두었고, 26픽셀짜리 칩에서는 그 여백이 차지하는 비율이 딴판이라 그림이 왼쪽 위로
      // 밀립니다 — 「안착했다가 한 번 튄다」가 그것이었습니다. 작은 것에 필요한 것은
      // 왜곡이 아니라 밝아짐 하나입니다.
      //
      // 켜지는 것도 꺼지는 것도 사인 한 마디입니다. **꼭대기가 0.8입니다** — 1이면 그
      // 순간 칩이 통째로 하얘져서 무엇이 생겼는지가 도리어 안 보입니다.
      const life = tagId === this.tagFlashId ? this.tagFlashLife : 1
      if (life < 1) cell.addChild(glare(size, Math.sin(life * Math.PI) * 0.8))

      cell.eventMode = 'static'
      cell.hitArea = new Rectangle(0, 0, size, size)
      this.tipOn(cell, () => {
        this.tooltip.show(nameOf(this.data, 'tag', tagId, tagId), t('ui.kind.tag'), 0, lines,
          LEFT + cell.x + size / 2, 34 + 38, SIZE)
      })
      return cell
    })
  }

  /**
   * 국면이 배경의 색을 정합니다.
   *
   * **어디에 있는지가 배경만 보고도 읽혀야 합니다.** 스몰은 초록, 빅은 호박, 보스는 붉고,
   * 상점은 푸르고, 끝났으면 색이 빠집니다.
   */
  /**
   * 어느 곡이 흐르는가.
   *
   * **화면마다 하나입니다.** 타이틀과 판과 상점은 하는 일이 다르므로 분위기도 달라야 하고,
   * 넘어갈 때는 겹쳐서 넘어가므로 끊긴 자리가 들리지 않습니다.
   */
  private syncMusic(): void {
    if (this.scene !== 'run') {
      this.audio.music.play('title')
      return
    }
    // 끝난 판에서는 조용합니다. **끝난 것 위로 음악이 계속 흐르면 끝난 것이 아닙니다.**
    const phase = this.state.phase
    if (phase === 'lost' || phase === 'won') {
      this.audio.music.play(undefined)
      return
    }
    this.audio.music.play(phase === 'shop' ? 'shop' : 'round')
  }

  private syncMood(): void {
    const state = this.state

    // **타이틀에서는 배경 자체가 어둡습니다.** 글을 읽히게 하려고 반투명 사각형을 얹으면
    // 그 겹의 변이 그대로 가로선으로 보입니다 — 어둡게 할 것은 배경이므로 배경을 어둡게
    // 합니다.
    if (this.scene !== 'run') {
      this.background.setMood([0.012, 0.030, 0.020], [0.10, 0.34, 0.20])
      return
    }

    // 배경도 연출이 끝난 뒤에 갑니다. 득점 중에 색이 바뀌면 무엇이 끝난 것인지 흐려집니다.
    if (!this.presented) return

    if (state.phase === 'lost') {
      this.background.setMood([0.05, 0.05, 0.058], [0.55, 0.5, 0.55])
      return
    }
    if (state.phase === 'won') {
      this.background.setMood([0.075, 0.062, 0.026], [1, 0.82, 0.34])
      return
    }
    if (state.phase === 'shop') {
      this.background.setMood([0.032, 0.062, 0.072], [0.32, 0.86, 0.82])
      return
    }

    switch (state.blind) {
      case BlindKind.Boss:
        this.background.setMood([0.082, 0.024, 0.04], [1, 0.26, 0.33])
        break
      case BlindKind.Big:
        this.background.setMood([0.062, 0.042, 0.082], [0.72, 0.42, 0.98])
        break
      default:
        this.background.setMood([0.042, 0.052, 0.086], [0.30, 0.52, 0.98])
        break
    }
  }

  private syncButtons(): void {
    const state = this.state
    const inRound = state.phase === 'round'

    this.playButton.visible = inRound
    this.discardButton.visible = inRound
    this.clearButton.visible = inRound
    this.clearButton.enabled = inRound && this.selected.size > 0
    this.playButton.enabled = inRound && this.selected.size > 0 && state.handsLeft > 0
    this.discardButton.enabled = inRound && this.selected.size > 0 && state.discardsLeft > 0

    // **가운데 버튼이 없습니다.** 블라인드 선택은 판마다 자기 버튼을 가지고, 상점은 판의
    // 밑단에 자기 버튼을 가집니다 — 어느 쪽이든 누를 것이 그 판 안에 있습니다.
    this.primaryButton.visible = false
    this.skipButton.visible = false
    this.sortRankButton.visible = inRound
    this.sortSuitButton.visible = inRound
    // **끝난 판에서는 걷는 것이 아니라 끕니다.** 없애 버리면 왼쪽 판의 밑단이 통째로 비어
    // 판이 그리다 만 것으로 보입니다 — 자리는 그대로 두고 눌리지 않게만 합니다. 그 자리에
    // 무엇이 있었는지가 남고, 지금 누를 것이 아니라는 것도 함께 읽힙니다.
    const playing = state.phase !== 'lost' && state.phase !== 'won'
    this.infoButton.enabled = playing
    this.menuButton.enabled = playing
    // 남은 카드는 판이 도는 동안만 뜻이 있습니다. 덱을 눌러 엽니다.
    if (this.state.phase !== 'round') this.modals.close(this.deckView)
    // 리롤도 상점 판의 밑단에 있습니다.
    this.rerollButton.visible = false
  }

  private syncCards(): void {
    // **화면이 주장하는 패입니다.** 다음 패는 득점 연출이 끝난 뒤에 깔립니다.
    //
    // **끝난 판에서는 아무것도 깔지 않습니다.** 코어는 진 판의 손패를 비우지 않으므로,
    // 걷어 낸 뒤에 다시 그리면 그 카드들이 도로 손에 섭니다.
    const over = this.state.phase === 'lost' || this.state.phase === 'won'
    const wanted = new Set(over ? [] : this.shown.hand)

    for (const [uid, view] of this.cards) {
      if (!wanted.has(uid)) {
        view.destroy()
        this.cards.delete(uid)
      }
    }

    const hand = this.shown.hand
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    const spacing = Math.min(SIZE.cardWidth + 12, 720 / Math.max(1, hand.length))
    const startX = BOARD_X - ((hand.length - 1) * spacing) / 2
    this.handSpots = { startX, spacing }

    hand.forEach((card, index) => {
      let view = this.cards.get(card.uid)
      const fresh = view === undefined

      if (!view) {
        view = new CardView(card, this.editionLook(card.edition))
        view.eventMode = 'static'
        view.cursor = 'pointer'
        // **누르기와 끌기가 한 손가락에 얹힙니다.** 뗄 때까지 움직이지 않았으면 고른
        // 것이고, 움직였으면 자리를 옮긴 것입니다 — `pointertap` 은 이 둘을 갈라 주지
        // 않아서 끌고 나서도 골라 버립니다.
        view.on('pointerdown', event =>
          this.beginDrag('hand', card.uid, view as CardView, event))
        this.cards.set(card.uid, view)
        this.board.addChild(view)
        // 덱에서 날아옵니다. **곧바로 자리에 있으면 뽑았다는 느낌이 없습니다.**
        view.placeNow(DECK_X, DECK_Y)
        this.audio.play('card_draw')
      } else {
        view.set(card, this.editionLook(card.edition))
      }

      const chosen = this.selected.has(card.uid)
      view.selected = chosen
      // 고른 것이 하나도 없으면 아무것도 물러나지 않습니다 — 고르기 전에 화면이 어두워지면
      // 무엇이 잘못된 것처럼 보입니다.
      // 도움을 받는 카드는 물러나지 않습니다 — 어두워진 카드를 권할 수는 없습니다.
      const hint = !chosen && this.hinted.has(card.uid)
      view.hint = hint
      view.setPick(chosen ? 1 : this.selected.size === 0 || hint ? 0 : -1, PICK_TINT)

      // **한 줄로 폅니다.** 가운데를 높이고 양끝을 기울여 부채꼴로 폈는데, 여덟 장이
      // 늘어서면 그 곡선이 카드마다 다른 높이와 기울기가 되어 줄이 고르지 않게 보입니다 —
      // 손패는 늘어놓은 것이지 쥐고 있는 것이 아닙니다.
      const spotX = startX + index * spacing
      const spotY = HAND_Y
      const tilt = 0
      // 끌고 있는 카드는 손가락이 자리를 정합니다. 여기서 다시 놓으면 커서에서 떨어집니다.
      if (this.drag?.kind === 'hand' && this.drag.uid === card.uid && this.drag.moved) return
      // 갓 뽑힌 카드는 **절도 있게** 자리에 붙고, 나머지는 부드럽게 자리를 옮깁니다.
      if (fresh) view.deal(spotX, spotY, tilt)
      else view.place(spotX, spotY, tilt)
    })
  }

  private syncJokers(): void {
    const wanted = new Set(this.state.jokers.map(joker => joker.uid))

    for (const [uid, view] of this.jokers) {
      if (wanted.has(uid)) continue
      // **곧바로 지우지 않습니다.** 타서 사라지는 것이 보여야 무엇이 없어진 것인지
      // 눈이 따라갑니다. 다 타면 `tick` 이 치웁니다.
      view.ignite()
      this.audio.play('joker_burn')
      this.jokers.delete(uid)
      this.burning.push(view)
    }

    this.state.jokers.forEach((joker, index) => {
      const row = this.data.tables.joker.findByJokerId(joker.jokerId)
      const look = {
        name: nameOf(this.data, 'joker', joker.jokerId, joker.jokerId),
        rarity: row?.rarity ?? 1,
        lines: describe(this.data, this.data.jokerEffects.get(joker.jokerId) ?? []),
        edition: this.editionLook(joker.edition),
      }

      let view = this.jokers.get(joker.uid)
      if (!view) {
        view = new JokerView(joker, look)
        view.eventMode = 'static'
        view.cursor = 'pointer'
        view.on('pointerdown', event =>
          this.beginDrag('joker', joker.uid, view as JokerView, event))
        this.jokers.set(joker.uid, view)
        this.board.addChild(view)
        // 위에서 내려옵니다 — **산 것이라면 산 자리에서 옵니다.** 그리는 자리도 함께
        // 옮깁니다: 용수철만 옮기면 한 프레임 동안 화면 왼쪽 위에 서 있습니다.
        const home = JOKER_X + index * (SIZE.jokerWidth + 12)
        const bought = this.arriveFrom !== undefined
        const from = this.arriveFrom ?? { x: home, y: JOKER_Y - 160 }
        this.arriveFrom = undefined
        view.motion.snap(from.x, from.y)
        view.position.set(from.x, from.y)
        // **산 것만 울렁입니다.** 판이 시작될 때 딸려 오는 조커까지 울렁이면 그것이
        // 「샀다」의 표시가 되지 못합니다.
        if (bought) view.buying()
      } else {
        view.set(joker, look)
      }

      if (this.drag?.kind === 'joker' && this.drag.uid === joker.uid && this.drag.moved) return
      const lifted = this.held?.kind === 'joker' && this.held.uid === joker.uid ? 12 : 0
      view.place(JOKER_X + index * (SIZE.jokerWidth + 12), JOKER_Y - lifted)
    })

    this.syncHeldBar()
  }

  /**
   * 끌기를 시작합니다.
   *
   * 아직 끄는 것인지 누르는 것인지 모릅니다 — 손가락이 몇 px 움직이고 나서야 갈립니다.
   */
  private beginDrag(kind: 'hand' | 'joker', uid: number, view: Container,
                    event?: FederatedPointerEvent): void {
    if (this.player.busy || this.modals.busy) return

    // **누른 그 자리에서 시작합니다.** 마우스는 누르기 전에 움직이므로 마지막으로 지나간
    // 자리가 곧 누른 자리이지만, **손가락은 누르는 그 순간에 처음 나타납니다** — 그때의
    // 자리를 쓰지 않으면 지난 자리와의 차이만큼 카드가 튀고, 그 튐이 「끌었다」로 읽혀
    // 손을 떼도 고르는 것이 되지 않습니다.
    if (event) this.pointerAt = this.world.toLocal(event.global)

    this.drag = {
      kind, uid, moved: false,
      startX: this.pointerAt.x, startY: this.pointerAt.y,
      grabX: this.pointerAt.x - view.x,
      // **손가락은 가만히 있어도 흔들립니다.** 마우스와 같은 문턱을 두면 누르려던 것이
      // 끄는 것으로 읽힙니다.
      slack: event?.pointerType === 'touch' ? 18 : 6,
    }

    // **손가락으로 설명을 보는 길입니다.** 마우스는 올리면 뜨지만 손가락에는 그것이
    // 없으므로, 누른 채로 기다리면 뜹니다 — 그러면 그 누름은 고르는 것이 아닙니다.
    if (!event) return
    if (kind === 'joker') {
      const view = this.jokers.get(uid)
      if (view) this.armPress(event, () => this.showTooltip(view))
      return
    }
    const card = this.state.deck.find(one => one.uid === uid)
    // 손에 있는 것이므로 「덱에 남았다」가 아니라 「손에 있다」입니다.
    if (card) this.armPress(event, () => this.showCardTip(card, false, true, view))
  }

  /**
   * 끄는 동안.
   *
   * **자리를 바로바로 바꿉니다** — 손을 뗀 뒤에 한 번에 정리하면 어디에 놓이는 것인지
   * 모르는 채로 끌게 됩니다.
   */
  private advanceDrag(): void {
    const drag = this.drag
    if (!drag) return

    if (!drag.moved) {
      const far = Math.abs(this.pointerAt.x - drag.startX) > drag.slack
        || Math.abs(this.pointerAt.y - drag.startY) > drag.slack
      if (!far) return
      drag.moved = true
      this.audio.play(drag.kind === 'hand' ? 'card_select' : 'joker_move', -4)
    }

    const x = this.pointerAt.x - drag.grabX
    const order = drag.kind === 'hand'
      ? this.state.hand
      : this.state.jokers.map(joker => joker.uid)
    const current = order.indexOf(drag.uid)
    if (current < 0) return

    const spacing = drag.kind === 'hand'
      ? this.handSpots.spacing : SIZE.jokerWidth + 12
    const startX = drag.kind === 'hand' ? this.handSpots.startX : JOKER_X
    const target = Math.max(0, Math.min(order.length - 1,
      Math.round((x - startX) / Math.max(1, spacing))))

    if (target !== current) {
      if (drag.kind === 'hand') {
        const next = this.state.hand.slice()
        next.splice(target, 0, ...next.splice(current, 1))
        this.state.hand = next
        // **화면이 그리는 것은 `shown.hand` 입니다.** 이것을 함께 바꾸지 않으면 끌어다
        // 놓아도 자리가 하나도 움직이지 않고 제자리로 돌아갑니다 — 정렬과 같습니다.
        const seen = new Set(this.shown.hand)
        this.shown.hand = next.filter(uid => seen.has(uid))
      } else {
        const next = this.state.jokers.slice()
        next.splice(target, 0, ...next.splice(current, 1))
        this.state.jokers = next
      }
      this.audio.play(drag.kind === 'hand' ? 'card_select' : 'joker_move', target * 2)
      this.refresh()
    }

    // 끌리는 것은 커서를 따라오고 조금 들립니다. **다른 것들 위에 있어야** 어디로 가는지
    // 보입니다.
    const view = drag.kind === 'hand'
      ? this.cards.get(drag.uid) : this.jokers.get(drag.uid)
    if (view) {
      this.board.setChildIndex(view, this.board.children.length - 1)
      view.place(x, (drag.kind === 'hand' ? HAND_Y : JOKER_Y) - 22, 0)
    }
  }

  /** 손을 뗍니다. 움직이지 않았으면 끈 것이 아니라 누른 것입니다. */
  private endDrag(): void {
    const drag = this.drag
    this.drag = undefined
    if (!drag) return

    if (!drag.moved) {
      // 꾸욱 눌러 설명을 본 것이면 고르지 않습니다.
      if (this.ate()) return
      if (drag.kind === 'hand') this.toggle(drag.uid)
      else this.pick('joker', drag.uid)
      return
    }
    this.audio.play(drag.kind === 'hand' ? 'card_place' : 'joker_move')
    this.refresh()

    // **겹치는 차례도 되돌립니다.** 끄는 동안 맨 위로 올렸으므로, 그대로 두면 놓은 카드가
    // 이웃을 계속 가려 부챗살이 한 장만 어긋나 보입니다.
    if (drag.kind === 'hand') {
      for (const uid of this.state.hand) {
        const view = this.cards.get(uid)
        if (view) this.board.addChild(view)
      }
    } else {
      for (const joker of this.state.jokers) {
        const view = this.jokers.get(joker.uid)
        if (view) this.board.addChild(view)
      }
    }
  }

  /** 조커나 소모품 하나를 고릅니다. 같은 것을 다시 누르면 놓습니다. */
  private pick(kind: 'joker' | 'consumable' | 'shop' | 'pack' | 'pack_slot', uid: number): void {
    if (this.player.busy) return
    this.held = this.held?.kind === kind && this.held.uid === uid
      ? undefined : { kind, uid }
    this.audio.play('card_select')
    this.refresh()
  }

  /**
   * 고른 것 밑의 버튼들.
   *
   * **고른 자리 바로 밑입니다.** 화면 구석에 두면 무엇에 대한 버튼인지가 끊깁니다.
   */
  private syncHeldBar(): void {
    this.heldBar.removeChildren().forEach(child => child.destroy())
    delete this.spots.held
    const held = this.held
    if (!held) return

    let anchor = 0
    // **버튼이 서는 높이가 갈립니다.** 조커와 소모품은 자기 줄 밑이고, 상점의 칸과 팩의
    // 카드는 화면 가운데에 있으므로 그 밑입니다 — 한 높이로 두면 무엇에 대한 버튼인지가
    // 끊깁니다.
    let baseline = JOKER_Y + SIZE.jokerHeight / 2 + 10
    const buttons: Button[] = []

    if (held.kind === 'shop') {
      const item = this.state.shop.cards[held.uid]
      const one = this.shopTiles.get(held.uid)
      if (!item || !one) {
        this.held = undefined
        return
      }
      anchor = one.mid
      // **값이 있던 그 줄입니다.** 고른 칸은 값을 감추고 그 자리를 단추가 대신하므로,
      // 자리를 따로 세지 않고 **값에게 묻습니다** — 따로 세면 딱지의 높이가 바뀔 때마다
      // 어긋나고, 실제로 어긋나 있었습니다.
      //
      // **쉬는 자리로 셉니다.** 고른 딱지는 들려 있고, 들린 만큼 단추도 따라 올라가면
      // 단추가 딱지 안으로 파고듭니다.
      baseline = one.baseY + one.price.y - 4
      const room = this.roomFor(item.kind)
      const swap = !room && this.canSwap(item)
      // 자리가 없으면 무엇과 바꿀지를 묻는 판이 뜹니다. 그 말은 단추에 적힙니다.
      buttons.push(new Button(
        // **값은 적지 않습니다.** 딱지에 이미 크게 적혀 있고, 그 바로 밑의 단추가 같은
        // 값을 한 번 더 적으면 그 둘 중 어느 것이 값인지 잠깐 헷갈립니다.
        swap ? t('ui.button.swap_take') : t('ui.button.buy'),
        swap ? 118 : 84, 32, room ? 0x2f7a52 : 0x7a5f2f, () => {
          this.held = undefined
          this.buyFrom(held.uid, item)
        }))
    } else if (held.kind === 'pack_slot') {
      const row = this.state.shop.packs[held.uid]
      const spot = this.packSlotTiles.get(held.uid)
      if (row === undefined || !spot) {
        this.held = undefined
        return
      }
      // **가운데를 딱지가 들고 있습니다.** 상점 카드의 158 을 쓰고 있어서 단추가 27px
      // 오른쪽으로 밀려 옆 팩의 값에 걸쳤습니다.
      anchor = spot.mid
      // 카드 딱지와 같은 규칙입니다 — 값이 있던 그 줄.
      baseline = spot.baseY + spot.price.y - 4
      buttons.push(new Button(t('ui.button.buy'), 84, 32, 0x2f7a52, () => {
        this.held = undefined
        this.openPackSlot(held.uid)
      }))
    } else if (held.kind === 'pack') {
      const open = this.state.pack
      const view = this.packViews.get(held.uid)
      if (!open || !view) {
        this.held = undefined
        return
      }
      // **부챗살 아래의 한 줄입니다.** 고른 그 카드 바로 밑에 세웠더니 옆 카드에 걸쳤고,
      // 고른 카드는 올라오므로 그 단추도 함께 올라와 자리가 카드마다 달랐습니다 — 어느
      // 카드를 고르든 단추는 같은 줄에 섭니다.
      //
      // **간격은 상점과 같은 4px 입니다.** 66px 였고, 그만큼 떨어지면 카드와 단추가 한
      // 덩이로 읽히지 않습니다 — 상점의 칸은 값이 있던 자리(카드 밑 4px)에 단추가 섭니다.
      anchor = view.face.node.x
      baseline = PACK_CARDS_Y + PACK_CARD_H / 2 + 4
      const room = this.roomFor(view.item.kind)
      const swap = !room && this.canSwap(view.item)
      buttons.push(new Button(
        t(swap ? 'ui.button.swap_take' : 'ui.button.take'), swap ? 118 : 92, 32,
        room ? 0x2f7a52 : 0x7a5f2f, () => {
          this.held = undefined
          this.takeFromPack(held.uid)
        }))
    } else if (held.kind === 'joker') {
      const index = this.state.jokers.findIndex(joker => joker.uid === held.uid)
      if (index < 0) {
        this.held = undefined
        return
      }
      anchor = JOKER_X + index * (SIZE.jokerWidth + 12)
      const price = sellValueOf(this.data, this.state, this.state.jokers[index])
      buttons.push(new Button(tf('ui.button.sell', { n: price }), 92, 30, 0x7a3f4a, () => {
        this.held = undefined
        this.audio.play('joker_sell')
        this.sellFrom = this.jokerSpot(index)
        this.act({ t: 'sell_joker', index })
      }))
    } else {
      const index = this.state.consumables.findIndex(item => item.uid === held.uid)
      if (index < 0) {
        this.held = undefined
        return
      }
      anchor = CONSUMABLE_X + index * (SIZE.jokerWidth + 12)
      buttons.push(new Button(t('ui.button.use'), 68, 30, 0x3f5f8a, () => {
        this.held = undefined
        // **쓴 것과 판 것은 없어지는 모습이 다릅니다.** 쓴 것은 판 가운데로 나와 번쩍이고,
        // 판 것은 제자리에서 탑니다 — 화면은 어느 쪽인지 모르므로 여기서 적어 둡니다.
        this.usedItem = held.uid
        this.act({ t: 'use_consumable', index, targets: this.orderedSelection() })
      }))
      buttons.push(new Button(tf('ui.button.sell', { n: this.data.economy.sellMin }), 92, 30, 0x7a3f4a, () => {
        this.held = undefined
        this.audio.play('joker_sell')
        this.sellFrom = this.itemSpot(index)
        this.act({ t: 'sell_consumable', index })
      }))
    }

    const gap = 8
    const span = buttons.reduce((sum, one) => sum + one.width, 0) + gap * (buttons.length - 1)
    let x = anchor - span / 2
    // **첫 단추의 자리를 알립니다.** 이제 사는 것도 집는 것도 두 번 눌러야 하므로, 도구가
    // 두 번째 누를 자리를 알아야 합니다 — 계산을 도구가 베껴 적으면 배치를 고칠 때
    // 한쪽만 고쳐지고 그 도구는 엉뚱한 곳을 눌러 놓고 아무 말도 하지 않습니다.
    this.spots.held = { x: x + (buttons[0]?.width ?? 0) / 2, y: baseline + 16 }
    for (const button of buttons) {
      button.position.set(x, baseline)
      x += button.width + gap
      this.heldBar.addChild(button)
    }
  }

  /**
   * 소모품이 들리는 것.
   *
   * **조커와 같은 용수철입니다** — `Motion` 의 `y` 와 같은 강성과 감쇠이므로, 나란히 선
   * 조커와 소모품이 같은 빠르기로 올라갑니다.
   */
  /**
   * 사서 오는 소모품이 울렁이다가 번쩍입니다.
   *
   * **조커의 `advance` 가 하는 일과 같습니다.** 다만 소모품 칸은 화면을 다시 그릴 때마다
   * 새로 만들어지므로 그 몫을 화면이 대신 듭니다.
   */
  /**
   * 방금 들어온 소모품이 그 자리에서 옵니다.
   *
   * **조커는 뷰가 용수철을 들고 있어서 날아옵니다.** 소모품 칸은 화면을 다시 그릴 때마다
   * 새로 만들어지므로 그럴 것이 없어서 제 칸에 툭 나타났습니다 — 오는 동안의 어긋남을
   * 화면이 들고 있다가 매 프레임 얹습니다.
   *
   * 상점에서 사는 것과 팩에서 집는 것 둘이 부릅니다. **자리만 다르고 나머지는 같습니다.**
   */
  private itemFlying(from: { x: number; y: number }): void {
    this.flyAsked++
    const last = this.state.consumables[this.state.consumables.length - 1]
    if (!last) {
      this.flyMissed++
      return
    }
    this.itemArrive = {
      uid: last.uid, warp: 1, glow: 0, filter: new ArriveFilter(), from, travel: 0,
    }
    this.syncConsumables()
  }

  /** 오는 중인 소모품의 지금 자리와 어디에서 오는 중인지. 없으면 `null`. */
  private flyPeek(): {
    x: number; y: number; fromX: number; fromY: number; travel: number
  } | null {
    const coming = this.itemArrive
    if (!coming) return null
    const one = this.consumableTiles.find(tile => tile.uid === coming.uid)
    if (!one) return null
    return {
      x: Math.round(one.tile.x), y: Math.round(one.tile.y),
      fromX: Math.round(coming.from.x), fromY: Math.round(coming.from.y),
      travel: Math.round(coming.travel * 100) / 100,
    }
  }

  private advanceItemArrive(seconds: number): void {
    const one = this.itemArrive
    if (!one) return

    one.warp = Math.max(0, one.warp - seconds * 1.6)
    one.glow = Math.max(0, one.glow - seconds * 2.2)
    one.travel = Math.min(1, one.travel + seconds / LAND_AT)
    one.filter.at(this.clock)
    one.filter.warp = one.warp
    one.filter.flash = one.glow * one.glow

    if (one.warp > 0 || one.glow > 0 || one.travel < 1) return
    // 다 썼으면 놓습니다. **판이 도는 내내 물결을 굽고 있을 이유가 없습니다.**
    this.itemArrive = undefined
    for (const tile of this.consumableTiles) {
      const first = tile.tile.children[0]
      if (first instanceof Container) this.faceOf(first).filters = []
    }
  }

  private advanceConsumableLift(seconds: number): void {
    for (const one of this.consumableTiles) {
      let spring = this.consumableLift.get(one.uid)
      if (!spring) {
        spring = new Spring()
        this.consumableLift.set(one.uid, spring)
      }
      spring.target = this.held?.kind === 'consumable' && this.held.uid === one.uid ? 12 : 0
      spring.advance(seconds)
      one.tile.y = one.baseY - spring.value

      // 사서 오는 중인 한 장은 산 자리에서 제 칸으로 미끄러집니다.
      const coming = this.itemArrive
      if (!coming || coming.uid !== one.uid) continue
      const left = 1 - coming.travel
      // 끝에서 느려집니다. **곧게 오면 자리에 「툭」 서고, 그것이 순간이동으로 보입니다.**
      const ease = left * left * left
      one.tile.x = one.baseX + (coming.from.x - one.baseX) * ease
      one.tile.y += (coming.from.y - one.baseY) * ease
    }
  }

  /**
   * 지금 걸려 있는 것들.
   *
   * 셋을 한 목록으로 봅니다 — 들고 있는 태그, 산 바우처, **기본값과 다른 규칙**. 마지막
   * 것이 요점입니다: 손패가 11장인 이유는 조커일 수도 덱일 수도 바우처일 수도 있고,
   * 그것들을 하나씩 눌러 보게 할 수는 없습니다.
   */
  private activeEntries(): { label: string; value: string; lines: string[] }[] {
    const out: { label: string; value: string; lines: string[] }[] = []

    for (const tag of this.state.tagsPending) {
      out.push({
        label: nameOf(this.data, 'tag', tag, tag),
        value: t('ui.kind.tag'),
        lines: describe(this.data, this.data.tagEffects.get(tag) ?? []),
      })
    }

    for (const id of this.state.vouchers) {
      out.push({
        label: nameOf(this.data, 'voucher', id, id),
        value: t('ui.kind.voucher'),
        lines: describe(this.data, this.data.voucherEffects.get(id) ?? []),
      })
    }

    const base = defaultRules(this.data) as unknown as Record<string, unknown>
    const now = this.state.rules as unknown as Record<string, unknown>
    for (const key of Object.keys(base)) {
      const was = base[key]
      const is = now[key]
      if (was === is) continue
      if (typeof is === 'boolean') {
        out.push({ label: this.ruleName(key), value: is ? t('ui.option.on') : t('ui.option.off'), lines: [] })
        continue
      }
      if (typeof was !== 'number' || typeof is !== 'number') continue
      const delta = is - was
      out.push({
        label: this.ruleName(key),
        value: `${ruleValue(key, is)}   (${delta > 0 ? '+' : ''}${ruleValue(key, delta)})`,
        lines: [],
      })
    }

    return out
  }

  /**
   * 왼쪽 패널의 「적용 중」.
   *
   * **자리가 좁습니다.** 다 넣으려 하면 글씨가 작아져 아무것도 안 읽히므로, 넷까지만 세우고
   * 나머지는 개수로 적습니다 — 누르면 판이 펼쳐집니다.
   */
  private syncActive(): void {
    this.activeLayer.removeChildren().forEach(child => child.destroy())
    const entries = this.activeEntries()
    if (entries.length === 0) return

    // **금액과 안테 칸 아래입니다.** 555는 그 칸의 밑변에서 2픽셀이라 글이 칸에 붙어
    // 있었습니다 — 붙어 있으면 그 칸에 딸린 설명으로 보이고, 이것은 그 칸과 상관없는
    // 다른 목록입니다.
    const top = 552
    const rowH = 26
    const shown = Math.min(entries.length, entries.length > 4 ? 3 : 4)

    const head = new Text({
      text: tf('ui.active.count', { n: entries.length }),
      style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '800' },
    })
    head.position.set(LEFT + 4, top)
    this.activeLayer.addChild(head)

    entries.slice(0, shown).forEach((entry, index) => {
      const y = top + 20 + index * rowH
      const line = new Container()
      line.position.set(LEFT, y)

      const plate = new Graphics()
      plate.roundRect(0, 0, PANEL_W, rowH - 4, 6).fill({ color: 0x0e1520, alpha: 0.85 })
      plate.roundRect(0.5, 0.5, PANEL_W - 1, rowH - 5, 6)
        .stroke({ color: COLOR.panelEdge, width: 1 })
      line.addChild(plate)

      const name = new Text({
        text: entry.label,
        style: { fontSize: 12, fill: COLOR.ink, fontWeight: '700' },
      })
      name.position.set(8, 4)
      line.addChild(name)

      const value = richLine(entry.value, {
        base: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
        number: COLOR.accentNumber, term: COLOR.accentTerm,
      })
      value.position.set(PANEL_W - 8 - value.width, 4)
      line.addChild(value)

      line.eventMode = 'static'
      line.cursor = 'pointer'
      line.hitArea = new Rectangle(0, 0, PANEL_W, rowH - 4)
      this.tipOn(line, () => {
        this.tooltip.show(entry.label, entry.value, 0, entry.lines,
          LEFT + PANEL_W / 2, y + rowH, SIZE)
      })
      line.on('pointertap', () => {
        if (this.ate()) return
        this.toggleActive()
      })
      this.activeLayer.addChild(line)
    })

    if (entries.length > shown) {
      const more = new Text({
        text: tf('ui.active.more', { n: entries.length - shown }),
        style: { fontSize: 11, fill: COLOR.inkDim, fontWeight: '700' },
      })
      more.position.set(LEFT + 4, top + 20 + shown * rowH + 4)
      more.eventMode = 'static'
      more.cursor = 'pointer'
      more.on('pointertap', () => {
        if (this.ate()) return
        this.toggleActive()
      })
      this.activeLayer.addChild(more)
    }
  }

  /** 「적용 중」 판을 열고 닫습니다. */
  private toggleActive(): void {
    if (this.modals.has(this.activePanel)) {
      this.modals.close(this.activePanel)
      return
    }
    this.drawActivePanel()
    this.modals.open(this.activePanel)
  }

  private drawActivePanel(): void {
    const layer = this.activePanel.view
    layer.removeChildren().forEach(child => child.destroy())

    const entries = this.activeEntries()
    const width = 460
    const top = TITLE_BAR + 18

    // **줄 높이가 저마다 다릅니다.** 설명이 접히면 그만큼 아래가 밀리므로, 자리를 먼저 잡고
    // 그 합으로 판의 높이를 정합니다.
    const rows = entries.map(entry => {
      const line = new Container()
      const name = new Text({
        text: entry.label,
        style: { fontSize: 14, fill: COLOR.ink, fontWeight: '800' },
      })
      name.position.set(20, 6)
      line.addChild(name)

      const value = richLine(entry.value, {
        base: { fontSize: 13, fill: COLOR.inkDim, fontWeight: '700' },
        number: COLOR.accentNumber, term: COLOR.accentTerm,
      })
      value.position.set(width - 20 - value.width, 7)
      line.addChild(value)

      let height = 30
      // 무엇을 하는 것인지는 그 줄 아래에. **이름만으로는 왜 걸렸는지 모릅니다.**
      if (entry.lines.length > 0) {
        // **값 칸을 피해 접습니다.** 오른쪽 끝에 값이 서 있으므로 거기까지 가면 겹칩니다.
        const note = richLine(entry.lines[0], {
          base: { fontSize: 11, fill: COLOR.inkDim },
          number: COLOR.accentNumber, term: COLOR.accentTerm,
        }, width - 130, 13)
        note.position.set(20, 22)
        line.addChild(note)
        height = 26 + note.height
      }
      return { line, height }
    })

    const body = rows.reduce((sum, row) => sum + row.height, 0)
    const height = top + Math.max(30, body) + 14 + FOOTER_BAR
    ;(this.activePanel.size as { width: number; height: number }).height = height

    layer.addChild(panelFrame(width, height, t('ui.active.head'), () => this.toggleActive()))

    if (entries.length === 0) {
      const empty = new Text({
        text: t('ui.active.empty'),
        style: { fontSize: 13, fill: COLOR.inkDim },
      })
      empty.anchor.set(0.5, 0)
      empty.position.set(width / 2, top + 6)
      layer.addChild(empty)
      return
    }

    let y = top
    for (const row of rows) {
      row.line.position.set(0, y)
      layer.addChild(row.line)
      y += row.height
    }
  }

  /**
   * 정산 판.
   *
   * 줄이 하나씩 쌓입니다 — 이벤트가 하나씩 오므로 그리는 것도 하나씩이고, 그 쌓이는 것이
   * 곧 「어디서 얼마가 들어왔는가」입니다.
   */
  private drawPayout(): void {
    const layer = this.payout.view
    layer.removeChildren().forEach(child => child.destroy())
    this.payoutNodes.length = 0

    const width = 380
    const rowH = 34
    const top = TITLE_BAR + 16
    // 줄들 · 가로선 · 단추. **밑단 띠는 이 판에 없습니다** — 닫는 것이 곧 받는 것이라
    // 아래에 따로 둘 것이 없고, 두면 단추가 그 띠에 걸칩니다.
    //
    // 줄 아래의 26은 「정산 중」 이 앉을 자리입니다. 줄이 다 서면 그 글이 사라지고 그만큼이
    // 여백으로 남습니다 — 글이 있고 없고에 판의 높이가 달라지면 판이 자라 보입니다.
    const body = Math.max(1, this.payoutRows.length) * rowH
    const height = top + body + 26 + FOOTER_BAR
    ;(this.payout.size as { width: number; height: number }).height = height

    const sum = this.payoutRows.reduce((total, row) => total + row.amount, 0)
    // **닫기 단추가 없습니다.** 받는 것이 이 판의 전부이고, 그것을 누르는 것이 닫는 것이라
    // 밑단 띠에 그 단추 하나만 섭니다.
    const take = new Button(tf('ui.payout.take', { n: sum }), 220, 40, 0x2f7a52, () => {
      // **누른 그 자리에서 차례를 지웁니다.** 닫히는 것을 기다리면 그 사이에 다시 섭니다.
      this.payoutWanted = false
      this.modals.close(this.payout)
    })
    // **제목을 답니다.** 한동안 비워 두었습니다 — 밑단의 「받는다」 가 이 판이 무엇인지를
    // 이미 말한다는 이유였습니다. 그런데 머리띠가 비어 있는 판은 이 판 하나뿐이라, 다른
    // 판과 한 벌로 보이지 않고 띠 자체가 그리다 만 것으로 보입니다. 「받는다」 는 누를
    // 것의 이름이고 「정산」 은 판의 이름이므로, 같은 말이 두 번 있는 것도 아닙니다.
    layer.addChild(panelFrame(width, height, t('ui.payout.title'), undefined, take))

    // **줄이 서기 전의 판이 휑했습니다.** 판은 먼저 열리고 줄은 하나씩 쌓이므로 그 사이가
    // 빈 상자입니다.
    //
    // 뼈대 줄을 깔아 둡니다 — 웹에서 글이 오기 전에 회색 막대를 깔아 두는 것과 같고,
    // **그 자리에 무엇이 올지까지 말해 줍니다.** 「정산 중」 은 줄 아래에 적습니다:
    // 머리띠는 제목의 자리이고, 무엇을 기다리는 중인가는 제목이 아닙니다.
    const head = new Text({
      text: t('ui.payout.counting'),
      style: { fontSize: 13, fill: COLOR.inkDim, fontWeight: '700', letterSpacing: 1 },
    })
    head.anchor.set(0.5, 0.5)
    head.position.set(width / 2, top + body + 11)
    const bones = new Graphics()
    layer.addChild(bones, head)
    this.payoutWait = {
      head, bones, width,
      rows: Math.max(1, this.payoutRows.length), top, rowH,
      begin: this.clock + PAYOUT_WAIT,
    }

    // **줄이 하나씩 쌓입니다.** 판이 열릴 때 줄은 이미 다 모여 있으므로, 쌓이는 것은
    // 그리는 쪽에서 만듭니다 — 한꺼번에 그려 놓으면 어디서 얼마가 들어왔는지를 훑어야
    // 합니다. 설 때마다 동전 소리가 하나 나고 음이 올라갑니다.
    this.payoutRows.forEach((row, index) => {
      const y = top + index * rowH
      const at = this.clock + PAYOUT_WAIT + index * PAYOUT_STEP

      const label = new Text({
        text: row.why,
        style: { fontSize: 14, fill: COLOR.ink, fontWeight: '700' },
      })
      label.position.set(24, y + 5)

      const amount = new Text({
        text: `${row.amount > 0 ? '+' : ''}$${row.amount}`,
        style: {
          fontSize: 17, fill: row.amount > 0 ? COLOR.money : COLOR.bad, fontWeight: '800',
          stroke: { color: 0x0a0f18, width: 3 },
        },
      })
      amount.anchor.set(1, 0)
      amount.position.set(width - 24, y + 2)

      layer.addChild(label, amount)
      for (const node of [label, amount]) {
        const one = { node: node as Container, at, from: node.y }
        this.payoutNodes.push(one)
        this.advanceOne(one)
      }
      this.chimes.push({ at, cue: 'coin_land', semitones: index * 3 })
    })
  }

  /**
   * 나머지를 모아 둔 판.
   *
   * **줄 하나에 하나씩입니다.** 자주 쓰지 않는 것들이므로 찾기 쉬운 것이 빠른 것보다
   * 낫습니다.
   */
  private openMenu(): void {
    const layer = this.menu.view
    layer.removeChildren().forEach(child => child.destroy())

    const width = 260
    // **옵션이 위입니다.** 판이 도는 동안 여는 것은 대개 소리나 속도를 고치려는 것이고,
    // 게임 방법은 첫 판에 한 번 보는 것입니다.
    const rows: { label: string; press: () => void }[] = [
      { label: t('ui.button.options'), press: () => this.openOptions() },
      { label: t('ui.button.guide'), press: () => this.modals.open(this.guide) },
      // **타이틀로는 맨 아래입니다.** 판을 접는 것이므로 옵션과 게임 방법과 같은 무게로
      // 가운데에 두면 잘못 누르는 일이 생깁니다.
      { label: t('ui.button.toTitle'), press: () => this.enterTitle() },
    ]
    // **밑단이 없습니다.** 머리의 `✕` 와 바깥 누르기와 `Esc` 로 닫히므로, 닫기를 또 두면
    // 같은 일을 하는 것이 판 하나에 둘입니다.
    const height = TITLE_BAR + MENU_PAD + rows.length * 46 + 8
    ;(this.menu.size as { width: number; height: number }).height = height

    layer.addChild(panelFrame(width, height, t('ui.button.menu'),
      () => this.modals.close(this.menu), undefined, false))

    rows.forEach((row, index) => {
      const button = new Button(row.label, width - 48, 38, 0x3a4658, () => {
        // **닫고 나서 엽니다.** 이 판 위에 또 판이 서면 뒤로 물러난 것이 보이고, 그것은
        // 메뉴가 아니라 판이 쌓인 것으로 보입니다.
        this.modals.close(this.menu)
        row.press()
      })
      button.position.set(24, TITLE_BAR + MENU_PAD + index * 46)
      layer.addChild(button)
    })

    this.modals.open(this.menu)
  }

  private showTooltip(view: JokerView): void {
    const rarityName = ['', t('ui.rarity.common'), t('ui.rarity.uncommon'), t('ui.rarity.rare'), t('ui.rarity.legendary')][view.look.rarity] ?? ''
    this.tooltip.show(view.look.name, rarityName, view.look.rarity, view.look.lines,
      view.x, view.y + SIZE.jokerHeight / 2, SIZE)
  }

  private syncConsumables(): void {
    const alive = new Set(this.state.consumables.map(item => item.uid))

    // **없어진 것은 곧바로 지우지 않습니다.** 판 밖으로 옮겨 태우고, 다 타면 그때 지웁니다.
    for (const one of this.consumableTiles) {
      if (alive.has(one.uid)) continue
      const used = this.usedItem === one.uid
      if (used) this.usedItem = undefined
      this.igniteItem(one.tile, used)
    }

    this.consumableLayer.removeChildren().forEach(child => child.destroy())
    this.consumableTiles.length = 0
    // 없어진 것의 높이는 버립니다.
    for (const uid of [...this.consumableLift.keys()]) {
      if (!alive.has(uid)) this.consumableLift.delete(uid)
    }

    this.state.consumables.forEach((item, index) => {
      const name = this.consumableName(item.kind, item.id)
      const lines = this.consumableLines(item.kind, item.id)

      // **조커와 같은 카드입니다.** 나란히 선 줄에서 하나만 다른 모양이면 갈래가 다른
      // 물건으로 보이고, 실제로는 둘 다 손에 든 카드입니다.
      const tile = new Container()
      tile.position.set(
        CONSUMABLE_X + index * (SIZE.jokerWidth + 12) - SIZE.jokerWidth / 2,
        JOKER_Y - SIZE.jokerHeight / 2)

      tile.addChild(this.faceCard({
        kind: (item.kind === 1 ? ShopItemKind.Tarot
          : item.kind === 2 ? ShopItemKind.Planet : ShopItemKind.Spectral) as ShopItemKind,
        id: item.id,
        cost: 0,
        edition: item.edition as never,
      } as ShopItem))
      tile.hitArea = new Rectangle(0, 0, SIZE.jokerWidth, SIZE.jokerHeight)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      // **누르면 고르는 것입니다.** 쓰는 것과 파는 것은 그 밑에 선 버튼이 합니다 —
      // 소모품 하나가 판을 바꾸므로, 실수로 눌러 써 버리면 되돌릴 수 없습니다.
      tile.on('pointertap', () => {
        if (this.ate()) return
        this.pick('consumable', item.uid)
      })
      this.consumableTiles.push({ uid: item.uid, tile, baseX: tile.x, baseY: tile.y })
      this.tipOn(tile, () => {
        this.tooltip.show(name, t('ui.kind.consumable'), 0, lines,
          tile.x + SIZE.jokerWidth / 2, tile.y + SIZE.jokerHeight, SIZE)
      })
      // 사서 오는 중인 한 장에만 겁니다. **얼굴에만입니다** — 그림자까지 걸면 카드 옆에
      // 빛나는 얼룩 하나가 따로 남습니다.
      if (this.itemArrive?.uid === item.uid) {
        const first = tile.children[0]
        if (first instanceof Container) this.faceOf(first).filters = [this.itemArrive.filter]
      }
      this.consumableLayer.addChild(tile)
    })
  }

  /** 한 장을 태웁니다. 자기 자리에서 그대로 타야 하므로 자리를 옮겨 담습니다. */
  private igniteItem(tile: Container, used: boolean): void {
    const spot = this.spotOf(tile)
    tile.removeFromParent()
    tile.position.set(spot.x, spot.y)
    tile.eventMode = 'none'

    // 얼굴만 골라 냅니다. 셰이더가 걸리는 자리입니다.
    const first = tile.children[0]
    const face = first instanceof Container ? this.faceOf(first) : tile

    const arrive = new ArriveFilter()
    const dissolve = new DissolveFilter()
    face.filters = [arrive]
    tile.filters = [dissolve]
    // **판 위의 버튼들보다 위입니다.** 판 가운데로 나오는 길에 그것들을 지나갑니다.
    tile.zIndex = 400
    this.overlay.addChild(tile)
    this.burningItems.push({
      tile, face, arrive, dissolve,
      from: { x: spot.x, y: spot.y },
      // **판 가운데로 갑니다.** 카드가 놓이는 자리이므로, 쓴 것이 무엇에 걸리는지가 그
      // 자리에서 보입니다 — 오른쪽 칸에서 그대로 타 없어지면 화면 구석의 일이 됩니다.
      // **판 것은 제자리에서 탑니다.** 나와서 번쩍이는 것은 「썼다」의 몸짓이고, 파는 것은
      // 그 자리에서 없애는 것입니다.
      to: used
        ? { x: BOARD_X - SIZE.jokerWidth / 2, y: PLAY_Y - SIZE.jokerHeight / 2 - 24 }
        : { x: spot.x, y: spot.y },
      life: used ? 0 : ITEM_HOLD,
      burn: 0, flashed: !used, grows: used,
    })

    if (used) this.audio.play('consumable_use')
  }

  /**
   * 쓴 것이 없어지는 네 마디.
   *
   * **울렁 → 이동 → 번쩍 → 타서 사라짐**입니다. 제자리에서 그냥 타면 무엇을 쓴 것인지가
   * 오른쪽 구석의 일로 남고, 그냥 없어지면 정말 쓰인 것인지 눈이 따라가지 못합니다 —
   * 사는 것이 「울렁 · 이동 · 안착」인 것과 짝이고, 다만 마지막이 안착이 아니라 사라짐입니다.
   */
  private advanceBurningItems(seconds: number): void {
    for (let i = this.burningItems.length - 1; i >= 0; i--) {
      const one = this.burningItems[i]
      one.life += seconds

      // 첫 마디. 제자리에서 울렁입니다.
      const warp = Math.max(0, 1 - one.life / ITEM_WARP)
      one.arrive.at(this.clock)
      one.arrive.warp = warp

      // 둘째 마디. 판 가운데로 갑니다.
      const travel = Math.max(0, Math.min(1,
        (one.life - ITEM_WARP) / (ITEM_ARRIVE - ITEM_WARP)))
      const eased = 1 - Math.pow(1 - travel, 3)
      one.tile.position.set(
        one.from.x + (one.to.x - one.from.x) * eased,
        one.from.y + (one.to.y - one.from.y) * eased)
      // 가는 동안 조금 커집니다. **쓰는 것은 그 판에서 가장 큰 한 수입니다.** 판 것은
      // 나오지 않으므로 커지지도 않습니다.
      if (one.grows) one.tile.scale.set(1 + 0.22 * eased)

      // 셋째 마디. 닿는 그 한 번만 번쩍입니다.
      if (travel >= 1 && !one.flashed) {
        one.flashed = true
        this.audio.play('card_slam')
        this.jolt(7, 1.4, 0.3)
        this.flashPanel(0x9b8fd0, 0.6)
      }
      const since = one.life - ITEM_ARRIVE
      const glow = one.flashed ? Math.max(0, 1 - since / ITEM_FLASH) : 0
      one.arrive.flash = glow * glow

      // 넷째 마디. 잠시 머물렀다가 탑니다.
      if (one.life < ITEM_HOLD) continue
      if (one.burn <= 0) this.audio.play('joker_burn')
      one.burn = Math.min(1, one.burn + seconds * 1.6)
      one.dissolve.burn = one.burn
      one.tile.y -= seconds * 26
      one.tile.rotation += seconds * 0.12
      if (one.burn < 1) continue
      this.burningItems.splice(i, 1)
      one.tile.destroy({ children: true })
    }
  }

  private consumableName(kind: number, id: string): string {
    const group = kind === 1 ? 'tarot' : kind === 2 ? 'planet' : 'spectral'
    return nameOf(this.data, group, id, id)
  }

  private consumableLines(kind: number, id: string): string[] {
    if (kind === 1) return describe(this.data, this.data.tarotEffects.get(id) ?? [])
    if (kind === 3) return describe(this.data, this.data.spectralEffects.get(id) ?? [])
    const planet = this.data.tables.planet.findByPlanetId(id)
    return planet ? [tf('ui.hand.level_up', { name: this.handName(planet.hand) })] : []
  }

  /**
   * 상점.
   *
   * **판 하나입니다.** 물건이 화면 여기저기에 흩어져 있으면 무엇이 한 벌인지 · 무엇을 먼저
   * 보아야 하는지가 읽히지 않습니다. 다른 판들과 같은 머리와 밑단을 쓰고, 안쪽은 줄 셋으로
   * 나뉩니다 — 살 것 · 뜯을 것 · 런 내내 남을 것.
   *
   * **닫히지 않습니다.** 닫으면 갈 곳이 없으므로 밑단에는 닫기 대신 리롤과 다음 블라인드가
   * 놓입니다.
   */
  private syncShop(): void {
    // **지우는 그 자리에서 함께 비웁니다.** 딱지를 들고 있는 표가 둘 있는데, 그리는 쪽에서
    // 비우면 상점이 서지 않는 프레임에는 그 그리는 쪽에 닿지 않습니다 — 지워진 딱지가
    // 표에 남고, 매 프레임 그것의 자리를 만지는 곳이 그 자리에서 터집니다. 예외는 조용히
    // 삼켜지므로 화면은 멀쩡하고 그 뒤가 통째로 죽습니다.
    this.shopLayer.removeChildren().forEach(child => child.destroy())
    this.shopTiles.clear()
    this.packSlotTiles.clear()
    // **정산이 끝난 뒤에 섭니다.** 돈이 들어오는 것을 보는 동안 상점이 이미 뒤에 서 있으면
    // 그 판이 무엇을 막고 있는 것으로 보이고, 순서가 뒤집힙니다.
    // **차례는 새로 설 때만 다시 셉니다.** 하나 사면 다시 그리는데, 그때마다 처음부터
    // 세우면 산 다음에 남은 것들이 또 한 번 나타납니다.
    this.shopOpening = false

    // **동전이 나는 동안에도 서 있습니다.** 하나 샀다고 판이 통째로 사라지면 무엇을 샀는지
    // 보다 판이 없어진 것이 먼저 보입니다. **카드가 걷히는 동안에는 서지 않습니다** — 낸
    // 카드가 아직 물러나는 중인데 판이 그 위에 서면 그 둘이 겹칩니다.
    // **정산을 기다리는 동안에도 서지 않습니다.** 카드가 걷힌 그 프레임에 상점이 먼저
    // 그려지고 정산은 그다음 프레임에 열리므로, 판이 떠 있는지만 보면 그 사이에 상점이
    // 한 번 번쩍입니다.
    this.shopLayer.visible = this.state.phase === 'shop' && this.shopReady
      && !this.payoutWanted && !this.modals.has(this.payout)
    // **국면으로 봅니다.** 눈에 보이는지로 보면 연출이 한 박자 도는 동안 — 조커를 살 때
    // 동전이 날아가는 그 동안 — 상점이 잠깐 물러났다가 처음부터 다시 섭니다.
    if (this.state.phase !== 'shop') this.shopStanding = false
    if (!this.shopLayer.visible) return
    if (!this.shopStanding) {
      this.shopStanding = true
      this.shopOpening = true
      this.shopRevealAt = this.clock
    }
    this.beginReveal(this.shopLayer, this.shopRevealAt, this.shopOpening)

    const state = this.state
    // **왼쪽 패널을 비껴야 합니다.** 화면 한가운데에 서므로, 이보다 넓으면 판돈과 금액이
    // 적힌 칸을 덮습니다 — 왼쪽 판은 `x` 292 에서 끝나고, 그것을 비껴야 합니다.
    const width = 660

    // **자리를 세어 가며 쌓습니다.** 높이를 못박으면 물건이 하나 늘거나 줄 때마다 아래가
    // 넘치거나 비고, 그 둘은 눈에 곧바로 보입니다.
    // **조커 줄 아래에서 시작합니다.** 그 줄 위로 올라오면 가지고 있는 것이 판에 가리고,
    // 산 것이 줄에 꽂히는 것도 판 뒤에서 일어납니다. 그래서 칸의 높이가 빠듯합니다.
    const ITEM_H = SIZE.jokerHeight + 36
    const PACK_H = SIZE.jokerHeight + 30
    const VOUCHER_H = 62
    const HEAD = 22
    const GAP = 12

    // **다 산 칸은 없어집니다.** 이름만 남겨 두면 그만큼이 빈자리로 남고, 그 빈자리는
    // 무엇이 있었는지도 무엇을 더 살 수 있는지도 알려 주지 않습니다.
    const rows: { title: string; body: number; draw: (top: number, h: number) => void }[] = []
    if (state.shop.cards.length > 0) {
      rows.push({
        title: t('ui.shop.wares'), body: ITEM_H,
        draw: (top, h) => this.drawShopItems(x, top, width, h),
      })
    }
    if (state.shop.packs.length > 0) {
      rows.push({
        title: t('ui.kind.pack'), body: PACK_H,
        draw: (top, h) => this.drawPackRow(x, top, width, h),
      })
    }
    // 바우처는 이미 산 것도 한 줄로 남깁니다 — **그것은 빈자리가 아니라 적힌 사실입니다.**
    if (state.shop.voucher || state.shop.voucherBought) {
      rows.push({
        title: t('ui.kind.voucher'), body: state.shop.voucher ? VOUCHER_H : 30,
        draw: (top, h) => this.drawVoucher(x, top, width, h),
      })
    }
    // 하나도 남지 않았으면 그 사실을 한 줄로 적습니다. 빈 판을 보여 주지 않습니다.
    const empty = rows.length === 0

    const body = rows.reduce((sum, row) => sum + HEAD + row.body, 0)
      + GAP * Math.max(0, rows.length - 1)
    const height = TITLE_BAR + 10 + (empty ? 64 : body) + 8 + FOOTER_BAR

    const x = POPUP_X - width / 2
    const y = SHOP_TOP

    const foot = new Container()
    const reroll = new Button(tf('ui.shop.reroll_cost', { n: rerollCost(this.data, state, state.shop) }),
      150, 40, 0x3f5f8f, () => this.reroll())
    reroll.enabled = state.money >= rerollCost(this.data, state, state.shop)
    const leave = new Button(t('ui.button.next_blind'), 190, 40, 0x2f6fb5, () => this.primary())
    leave.position.set(166, 0)
    foot.addChild(reroll, leave)

    const frame = panelFrame(width, height, t('ui.guide.shop.head'), undefined, foot)
    frame.position.set(x, y)
    this.shopLayer.addChild(frame)

    const money = new Text({
      text: `$${this.shown.money}`,
      style: { fontSize: 19, fill: COLOR.money, fontWeight: '800' },
    })
    money.anchor.set(1, 0.5)
    money.position.set(x + width - 26, y + TITLE_BAR / 2)
    this.shopLayer.addChild(money)

    if (empty) {
      const note = new Text({
        text: t('ui.shop.sold_out'),
        style: { fontSize: 14, fill: COLOR.inkDim },
      })
      note.anchor.set(0.5, 0.5)
      note.position.set(x + width / 2, y + TITLE_BAR + 10 + 32)
      this.reveal(note)
      return
    }

    let at = y + TITLE_BAR + 10
    for (const row of rows) {
      this.shopSection(x, at, width, row.title)
      row.draw(at + HEAD, row.body)
      at += HEAD + row.body + GAP
    }
  }

  /**
   * 이제부터 세우는 것은 이 판의 것입니다.
   *
   * **소리는 새로 설 때만 냅니다.** 다시 그릴 때마다 내면 하나 살 때 남은 것들의 소리가
   * 한꺼번에 다시 납니다.
   */
  private beginReveal(layer: Container, base: number, sound: boolean): void {
    this.revealing = { layer, base, slot: 0, sound }
  }

  /**
   * 하나를 세웁니다.
   *
   * 한 번에 세우는 것들은 한 차례를 나눠 씁니다 — 칸 이름의 선과 글자가 그렇습니다.
   * 판이 선 지 오래되었으면 계산 결과가 이미 1이므로 그 자리에 그대로 섭니다.
   */
  private reveal(...nodes: Container[]): void {
    const at = this.revealing.base + this.revealing.slot * REVEAL_STEP
    this.revealing.slot += 1
    if (this.revealing.sound) this.chimes.push({ at, cue: 'card_place', semitones: 0 })

    for (const node of nodes) {
      this.revealing.layer.addChild(node)
      const one = { node, at, from: node.y }
      this.reveals.push(one)
      this.advanceOne(one)
    }
  }

  /** 하나가 지금 어디까지 섰는가. */
  private advanceOne(one: { node: Container; at: number; from: number }): void {
    const step = Math.max(0, Math.min(1, (this.clock - one.at) / REVEAL_SPAN))
    const eased = 1 - (1 - step) * (1 - step) * (1 - step)
    one.node.alpha = eased
    one.node.y = one.from + (1 - eased) * REVEAL_RISE
  }

  private advanceReveals(): void {
    for (const one of this.reveals) {
      if (one.node.alpha < 1) this.advanceOne(one)
    }
  }

  private shopSection(x: number, y: number, width: number, title: string): void {
    const label = new Text({
      text: title,
      style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '800', letterSpacing: 1 },
    })
    label.position.set(x + 26, y)

    const rule = new Graphics()
    rule.rect(x + 26 + label.width + 12, y + 8, width - 64 - label.width - 12, 1)
      .fill({ color: COLOR.panelEdge, alpha: 0.8 })

    this.reveal(rule, label)
  }

  /**
   * 살 것들.
   *
   * **줄에 서는 것은 카드입니다.** 아이콘을 얹은 딱지로 두면 살 때와 산 뒤의 모습이 달라
   * 같은 물건으로 보이지 않습니다 — 상점에 선 그 카드가 그대로 조커 줄에 섭니다.
   */
  private drawShopItems(left: number, top: number, width: number, tileH: number): void {
    const slots = this.state.shop.cards
    const tileW = 158
    const gap = 14
    // **칸이 늘어나도 줄은 판 안에 있어야 합니다.** 기본은 2칸이지만 상점 칸을
    // 늘리는 조커가 있으면 3칸도 5칸도 됩니다 — 그대로 다 그리면 오른으로 벗어납니다.
    const full = slots.length * tileW + Math.max(0, slots.length - 1) * gap
    const room = width - 52
    const fit = full > room ? room / full : 1
    const span = full * fit
    const startX = left + (width - span) / 2

    slots.forEach((item, slot) => {
      const name = shopLabel(item.kind, item.id, this.data)
      const lines = this.shopLines(item)
      const rarity = item.kind === ShopItemKind.Joker
        ? this.data.tables.joker.findByJokerId(item.id)?.rarity ?? 1 : 0
      const afford = this.shown.money >= item.cost
      const room = this.roomFor(item.kind)

      const tile = new Container()
      tile.position.set(startX + slot * (tileW + gap) * fit, top)
      tile.scale.set(fit)

      const card = this.itemCard(item)
      card.position.set((tileW - SIZE.jokerWidth) / 2, 0)
      tile.addChild(card)

      const price = new Text({
        text: `$${item.cost}`,
        style: {
          fontSize: 20, fontWeight: '800',
          fill: afford ? COLOR.money : 0x7a6a45,
          stroke: { color: 0x0a0f18, width: 4 },
        },
      })
      price.anchor.set(0.5, 0)
      price.position.set(tileW / 2, SIZE.jokerHeight + 8)
      tile.addChild(price)

      // **자리가 없으면 그것이 값보다 먼저 읽혀야 합니다.** 눌러 보고 아무 일도 없는 것이
      // 가장 나쁩니다.
      if (!room) {
        const full = new Text({
          text: t('ui.swap.hint'),
          style: { fontSize: 10, fill: 0xffb4c8, fontWeight: '800' },
        })
        full.anchor.set(0.5, 0)
        full.position.set(tileW / 2, SIZE.jokerHeight + 34)
        tile.addChild(full)
      }

      tile.alpha = afford ? 1 : 0.55
      tile.eventMode = 'static'
      tile.hitArea = new Rectangle(0, 0, tileW, tileH)
      tile.cursor = afford ? 'pointer' : 'default'
      // **가운데를 딱지가 알고 있습니다.** 단추를 세우는 쪽이 너버를 다시 셀면
      // 배율이 붙을 때 어긋납니다 — 팝의 158 이 그러했습니다.
      this.shopTiles.set(slot, { tile, baseY: tile.y, price,
                                 mid: tile.x + tileW * fit / 2 })
      // **누르면 고르기만 합니다.** 사는 것은 그 밑에 서는 단추가 합니다.
      tile.on('pointertap', () => {
        if (this.ate()) return
        // **밝힐 금액은 지금 금액이 아니라 보이는 금액입니다.** 동전이 날아가는 동안에
        // 그려진 딱지는 `afford` 가 거짓으로 굳어 있고, 다시 그려질 이유가 없으면 돈이
        // 다 온 뒤에도 눌러지지 않습니다 — 누를 때 다시 봅니다.
        if (!this.canPay(item.cost)) return
        this.pick('shop', slot)
      })
      this.tipOn(tile, () => {
        // 상점의 것은 **가격도 칩으로** 섭니다 — 사는 자리이므로 얼마인지가 이름 옆에
        // 있어야 합니다.
        this.tooltip.show(name, kindName(item.kind), rarity, lines,
          tile.x + tileW / 2, tile.y + tileH, SIZE, item.cost)
      })
      this.reveal(tile)
    })
  }

  /**
   * 상점에 선 물건 하나의 카드.
   *
   * 조커는 **줄에 서는 그 카드 그대로**입니다 — 같은 클래스를 씁니다. 소모품과 플레잉
   * 카드는 같은 크기와 모양의 카드로 그립니다.
   */
  private itemCard(item: ShopItem): Container {
    if (item.kind === ShopItemKind.Joker) {
      const row = this.data.tables.joker.findByJokerId(item.id)
      const view = new JokerView({
        uid: -1, jokerId: item.id, edition: item.edition as never,
        sticker: 0 as never, counters: newCounters(), age: 0, disabled: false,
      }, {
        name: row?.name ?? item.id,
        rarity: row?.rarity ?? 1,
        lines: describe(this.data, this.data.jokerEffects.get(item.id) ?? []),
        edition: this.editionLook(item.edition as EditionKind),
      })
      // 상점의 카드는 흔들리지 않습니다. 줄에 선 것과 달리 고를 것이지 도는 것이 아닙니다.
      view.pivot.set(0, 0)
      view.position.set(0, 0)
      return view
    }

    return this.faceCard(item)
  }

  /**
   * 카드에서 얼굴만. **그림자는 뺍니다.**
   *
   * `faceCard` 가 만든 것은 그림자 하나와 얼굴 하나입니다. 셰이더를 통째로 걸면 그림자에도
   * 걸려, 카드 옆에 빛나는 얼룩 하나가 따로 남습니다.
   */
  private faceOf(node: Container): Container {
    return (node.children[1] as Container | undefined) ?? node
  }

  /**
   * 소모품과 플레잉 카드의 얼굴.
   *
   * 조커와 **같은 크기, 같은 모서리, 같은 이름 띠**입니다 — 상점에 여러 갈래가 서므로
   * 모양이 어긋나면 줄이 흐트러져 보입니다.
   */
  private faceCard(item: ShopItem): Container {
    const w = SIZE.jokerWidth
    const h = SIZE.jokerHeight
    const node = new Container()

    // **그림자와 얼굴을 가릅니다.** 셰이더는 얼굴에만 걸립니다 — 통째로 걸면 그림자도
    // 함께 번쩍여서, 카드 옆에 빛나는 얼룩 하나가 따로 남습니다. `joker-view.ts` 와 같은
    // 이유이고, `faceOf` 가 이 둘째 아이를 찾아 씁니다.
    const shade = new Graphics()
    shade.roundRect(3, 5, w, h, 9).fill({ color: 0x000000, alpha: 0.4 })
    const paper = new Container()
    node.addChild(shade, paper)

    const plate = new Graphics()
    plate.roundRect(0, 0, w, h, 9).fill(0x141b26)
    paper.addChild(plate)

    /**
     * 그림을 카드 모양으로 자르는 것.
     *
     * **쓸 때 만듭니다.** 미리 만들어 붙여 두면 그림이 아직 오지 않은 동안 이 흰 사각형이
     * 그대로 카드 얼굴로 그려집니다 — 카드가 하얗게 나오던 것이 그것입니다.
     */
    const cutout = (): Graphics => {
      const clip = new Graphics()
      clip.roundRect(0, 0, w, h, 9).fill(0xffffff)
      paper.addChild(clip)
      return clip
    }

    if (item.kind === ShopItemKind.PlayingCard) {
      const row = this.data.tables.baseDeckCard.findByCardId(item.id)
      if (row) {
        const setDir = cardArtDir()
        const texture = setDir === undefined
          ? undefined : artFor(setDir, cardArtId(row.suit, row.rank))
        if (texture) {
          const picture = new Sprite(texture)
          picture.width = w
          picture.height = h
          paper.addChild(picture)
        } else {
          const face = new Graphics()
          face.roundRect(0, 0, w, h, 9).fill(COLOR.cardFace)
          drawFace(face, row.suit, row.rank, w, h, suitInk(row.suit))
          paper.addChild(face)
        }
      }
    } else {
      const kind: ArtKind | undefined = item.kind === ShopItemKind.Tarot ? 'tarot'
        : item.kind === ShopItemKind.Planet ? 'planet'
          : item.kind === ShopItemKind.Spectral ? 'spectral' : undefined
      const texture = kind ? artFor(kind, item.id) : undefined
      if (texture) {
        const sprite = new Sprite(texture)
        const scale = Math.max(w / texture.width, h / texture.height)
        sprite.width = texture.width * scale
        sprite.height = texture.height * scale
        sprite.position.set((w - sprite.width) / 2, (h - sprite.height) / 2)
        sprite.mask = cutout()
        paper.addChild(sprite)
      }
    }

    const tint = item.kind === ShopItemKind.PlayingCard ? COLOR.cardEdge : 0x9b8fd0
    const band = new Graphics()
    band.roundRect(0, h - 26, w, 26, 9).fill({ color: 0x0b1018, alpha: 0.88 })
    band.rect(0, h - 26, w, 17).fill({ color: 0x0b1018, alpha: 0.88 })
    band.rect(0, h - 26, w, 1.5).fill({ color: tint, alpha: 0.9 })
    paper.addChild(band)

    const label = new Text({
      text: shopLabel(item.kind, item.id, this.data),
      style: {
        fontSize: 11, fill: COLOR.ink, fontWeight: '800', align: 'center',
        wordWrap: true, wordWrapWidth: w - 8, breakWords: true, lineHeight: 12,
      },
    })
    label.anchor.set(0.5, 0.5)
    label.position.set(w / 2, h - 13)
    paper.addChild(label)

    const frame = new Graphics()
    frame.roundRect(1.25, 1.25, w - 2.5, h - 2.5, 8).stroke({ color: tint, width: 2.5 })
    paper.addChild(frame)

    return node
  }

  /**
   * 상점 딱지 하나가 선 자리. 산 것이 여기에서 날아갑니다.
   *
   * **한 곳에서만 셉니다.** 같은 계산이 세 군데에 적혀 있었고, 그중 둘은 액션 뒤에 있어
   * 이미 없어진 딱지에게 물었습니다.
   */
  private shopSpot(slot: number): { x: number; y: number } {
    const one = this.shopTiles.get(slot)
    const tile = one?.tile
    // **없으면 상점 한가운데입니다.** 딱지는 상점을 다시 그릴 때마다 새로 만들어지므로,
    // 붙들고 있던 것이 이미 지워졌을 수 있습니다 — 그때 그 딱지에게 자리를 물으면 예외가
    // 나고, 누르는 자리의 예외는 조용히 삼켜져 그 뒤가 통째로 죽습니다.
    if (!tile) return { x: BOARD_X, y: SHOP_CARD_Y }
    return { x: tile.x + (158 - SIZE.jokerWidth) / 2, y: tile.y + 22 }
  }

  /** 그 갈래를 받을 자리가 있는가. */
  /**
   * 이 값을 낼 수 있는가.
   *
   * **코어와 같은 판정입니다.** 빚 한도까지는 낼 수 있으므로 금액이 값보다 적어도 살 수
   * 있고, `shown.money` 는 동전이 날아가는 동안의 값이라 이 판정에 쓰지 않습니다.
   */
  private canPay(cost: number): boolean {
    return this.state.money - cost >= this.state.rules.debtLimit
  }

  private roomFor(kind: ShopItemKind): boolean {
    const state = this.state
    if (kind === ShopItemKind.Joker) return state.jokers.length < state.rules.jokerSlots
    if (kind === ShopItemKind.Tarot || kind === ShopItemKind.Planet
      || kind === ShopItemKind.Spectral) {
      return state.consumables.length < state.rules.consumableSlots
    }
    return true
  }

  /**
   * 상점의 물건 하나를 삽니다.
   *
   * **자리가 없으면 무엇과 바꿀지를 묻습니다.** 그냥 눌리지 않게 두면 왜 안 되는지 알 수
   * 없고, 말없이 파는 것은 되돌릴 수 없는 일을 묻지 않고 하는 것입니다.
   */
  /**
   * 상점의 물건 하나를 삽니다.
   *
   * **딱지를 받지 않습니다.** 받아 두면 그것을 붙든 채로 상점이 다시 그려지고, 다시
   * 그려지는 것은 딱지를 통째로 없애는 것입니다 — 자리는 부르는 그때 칸 번호로 찾습니다.
   */
  private buyFrom(slot: number, item: ShopItem): void {
    if (!this.canPay(item.cost)) return
    if (!this.roomFor(item.kind)) {
      this.tooltip.hide()
      this.askSwap(item, held => {
        // **자리를 먼저 적어 둡니다.** 아래와 같은 이유입니다 — `act` 가 상점을 다시 그리며
        // 이 딱지를 없애므로, 그 뒤에 딱지에게 자리를 물으면 없는 것에게 묻는 것입니다.
        const from = this.shopSpot(slot)
        this.audio.play('joker_buy')
        this.sellFrom = item.kind === ShopItemKind.Joker
          ? this.jokerSpot(held) : this.itemSpot(held)
        this.act({ t: 'swap', slot, index: held })
        if (isConsumable(item.kind)) this.itemFlying(from)
      })
      return
    }
    // **산 것이 그 자리에서 튀어 오릅니다.** 값을 치른 자리가 밝아지고, 그 자리에서
    // 조커가 날아가 줄에 꽂힙니다 — 조각 몇 개만으로는 눌린 것인지 산 것인지 모릅니다.
    this.tooltip.hide()
    // 조커를 사는 것과 소모품을 사는 것은 소리가 갈립니다.
    this.audio.play(item.kind === ShopItemKind.Joker ? 'joker_buy' : 'shop_buy')
    // **조각을 터뜨리지 않습니다.** 조각은 산 물건 뒤에서 흩어질 뿐이라 무엇을 산 것인지가
    // 남지 않습니다 — 산 그 물건이 울렁이며 날아가 자리에서 번쩍이는 것이 「샀다」입니다.
    this.flashPanel(COLOR.money, 0.35)

    // **산 자리를 액션보다 먼저 적어 둡니다.**
    //
    // `act` 는 상점을 다시 그리고, 다시 그리는 것은 딱지를 통째로 없애고 새로 만드는
    // 것입니다 — 그 뒤에 `tile.x` 를 읽으면 없어진 것에게 자리를 묻는 것이라 그 자리에서
    // 예외가 납니다. 예외는 누르는 자리에서 조용히 삼켜지므로 화면은 그대로 돌고, **산
    // 소모품만 오는 길 없이 제 칸에 툭 나타났습니다.** 조커는 이 값을 액션 앞에서 한 번만
    // 읽으므로 멀쩡했고, 팩에서 집는 것은 딱지가 없어지지 않으므로 멀쩡했습니다.
    const from = this.shopSpot(slot)
    this.arriveFrom = from

    this.act({ t: 'buy', slot })

    // 소모품은 산 자리에서 제 칸으로 미끄러집니다. **조커는 뷰가 용수철을 들고 있어 오는
    // 길이 있고, 소모품 칸은 매번 새로 만들어지므로 화면이 그 몫을 듭니다.**
    if (isConsumable(item.kind)) this.itemFlying(from)

    // 날아가 닿는 데까지가 한 박자입니다. 닿는 자리에서 이름과 소리가 납니다.
    this.later.push({ at: this.clock + LAND_AT, run: () => this.landed(item) })
  }

  /**
   * 산 것이 제자리에 닿았습니다.
   *
   * **닿은 자리에 이름이 뜹니다.** 어디로 들어간 것인지가 그 한 번으로 남습니다 —
   * 조커는 조커 줄로, 소모품은 소모품 칸으로 들어갑니다.
   */
  private landed(item: ShopItem): void {
    const joker = item.kind === ShopItemKind.Joker
    let spot: { x: number; y: number } | undefined

    if (joker) {
      const last = this.state.jokers[this.state.jokers.length - 1]
      const view = last ? this.jokers.get(last.uid) : undefined
      if (view) {
        // **발동이 아니라 도착입니다.** 흔들리면 아무 이유 없이 난리치는 것으로 보입니다.
        view.bounce(1.2)
        view.landing()
        spot = { x: view.x + SIZE.jokerWidth / 2, y: view.y + SIZE.jokerHeight / 2 }
      }
    } else {
      const last = this.consumableTiles[this.consumableTiles.length - 1]
      if (last) spot = this.spotOf(last.tile, SIZE.jokerWidth / 2, SIZE.jokerHeight / 2)
      if (this.itemArrive) {
        this.itemArrive.glow = 1
        this.itemArrive.warp = 0
      }
    }
    if (!spot) return

    this.audio.play('joker_add')
    // **여기서도 조각이 없습니다.** 자리에 닿은 것은 카드 전체가 한 번 번쩍이는 것으로
    // 알립니다 — 그것이 그 카드에 관한 일이라는 것이 조각보다 분명합니다.
    this.popAt({ x: spot.x, y: spot.y }, shopLabel(item.kind, item.id, this.data),
      COLOR.money, 0.5)
  }

  /**
   * 조커 칸과 소모품 칸의 가운데.
   *
   * **한 자리에서 셉니다.** 파는 자리에서 동전이 솟아야 하는데, 부르는 쪽마다 다시 세면
   * 줄의 자리를 고친 날에 한쪽만 고쳐집니다.
   */
  private jokerSpot(index: number): { x: number; y: number } {
    return { x: JOKER_X + index * (SIZE.jokerWidth + 12), y: JOKER_Y }
  }

  private itemSpot(index: number): { x: number; y: number } {
    return { x: CONSUMABLE_X + index * (SIZE.jokerWidth + 12), y: JOKER_Y }
  }

  /** 그 자리를 판 위의 자리로 옮깁니다. 왼쪽 판 안의 것들은 자기 판 기준입니다. */
  private spotOf(node: Container, dx = 0, dy = 0): { x: number; y: number } {
    return this.overlay.toLocal(node.toGlobal({ x: dx, y: dy }))
  }

  /**
   * 자리가 없습니다 — 무엇과 바꿀까요.
   *
   * **묻고 나서 팝니다.** 말없이 하나를 팔아 치우면 되돌릴 수 없는 일을 묻지 않고 한
   * 것이고, 그냥 눌리지 않게 두면 왜 안 되는지 알 수 없습니다.
   *
   * 파는 값이 줄마다 적혀 있습니다 — 그것이 무엇을 내놓을지를 정하는 값입니다.
   */
  private canSwap(item: ShopItem): boolean {
    return item.kind === ShopItemKind.Joker
      // `Eternal` 은 팔리지 않습니다. 그것만 들고 있으면 내놓을 것이 없습니다.
      ? this.state.jokers.some(held => held.sticker !== 1)
      : this.state.consumables.length > 0
  }

  private askSwap(item: ShopItem, commit: (held: number) => void): void {
    const joker = item.kind === ShopItemKind.Joker
    const rows = joker
      ? this.state.jokers.map((held, index) => {
        return {
          index,
          name: nameOf(this.data, 'joker', held.jokerId, held.jokerId),
          note: describe(this.data, this.data.jokerEffects.get(held.jokerId) ?? [])[0] ?? '',
          price: sellValueOf(this.data, this.state, held),
          // `Eternal` 은 팔리지 않습니다.
          locked: held.sticker === 1,
        }
      })
      : this.state.consumables.map((held, index) => ({
        index,
        name: this.consumableName(held.kind, held.id),
        note: this.consumableLines(held.kind, held.id)[0] ?? '',
        price: this.data.economy.sellMin,
        locked: false,
      }))

    const width = 460
    const top = TITLE_BAR + 70
    const GAP = 10

    // **글을 먼저 만들고 줄의 높이를 그것에 맞춥니다.** 높이를 못박으면 설명이 두 줄인
    // 조커에서 아랫줄이 딱지 밖으로 나갑니다 — 어느 조커의 설명이 긴지는 데이터가 정합니다.
    const built = rows.map(held => {
      const note = new Text({
        text: held.locked ? t('ui.note.cannot_sell') : held.note,
        style: {
          fontSize: 11, fill: held.locked ? 0xffb4c8 : COLOR.inkDim,
          wordWrap: true, wordWrapWidth: width - 200, breakWords: true, lineHeight: 14,
        },
      })
      return { held, note, height: Math.max(48, 26 + note.height + 8) }
    })

    const body = built.reduce((sum, one) => sum + one.height + GAP, 0)
    const height = top + body + 4 + FOOTER_BAR

    const panel: ModalPanel = {
      view: new Container(),
      size: { width, height },
    }
    const layer = panel.view
    layer.addChild(panelFrame(width, height, t('ui.swap.title'),
      () => this.modals.close(panel)))

    const lead = richLine(
      tf('ui.swap.lead', { name: shopLabel(item.kind, item.id, this.data) }), {
        base: { fontSize: 12, fill: COLOR.inkDim },
        number: COLOR.accentNumber,
        term: COLOR.accentTerm,
      }, width - 48)
    lead.position.set((width - lead.width) / 2, TITLE_BAR + 16)
    layer.addChild(lead)

    // **값이 들어온다는 것을 적어 둡니다.** 줄마다 `+$N` 이 적혀 있어도, 그것이 「이만큼
    // 받는다」인지 「이만큼 버린다」인지는 적혀 있지 않으면 알 수 없습니다.
    const paid = new Text({
      text: t('ui.swap.paid'),
      style: { fontSize: 11, fill: COLOR.money, fontWeight: '700' },
    })
    paid.anchor.set(0.5, 0)
    paid.position.set(width / 2, TITLE_BAR + 40)
    layer.addChild(paid)

    let at = top
    built.forEach(one => {
      const held = one.held
      const tile = new Panel(width - 48, one.height, held.locked ? 0x241c26 : 0x1b2331)
      tile.position.set(24, at)
      at += one.height + GAP

      const name = new Text({
        text: held.name,
        style: { fontSize: 14, fill: COLOR.ink, fontWeight: '800' },
      })
      name.position.set(14, 7)

      const note = one.note
      note.position.set(14, 26)

      const price = new Text({
        text: held.locked ? '—' : `+$${held.price}`,
        style: { fontSize: 15, fill: held.locked ? 0x7a6a45 : COLOR.money, fontWeight: '800' },
      })
      price.anchor.set(1, 0.5)
      price.position.set(width - 62, one.height / 2)

      tile.addChild(name, note, price)
      tile.alpha = held.locked ? 0.5 : 1
      if (!held.locked) {
        tile.eventMode = 'static'
        tile.cursor = 'pointer'
        tile.on('pointertap', () => {
          if (this.ate()) return
          this.modals.close(panel)
          commit(held.index)
          // 바꿔서 얻은 것도 얻은 것입니다. 닿는 자리에 같은 것이 납니다.
          this.later.push({ at: this.clock + LAND_AT, run: () => this.landed(item) })
        })
      }
      layer.addChild(tile)
    })

    this.modals.open(panel)
  }

  /**
   * 팩.
   *
   * **사는 것이 아니라 뜯는 것입니다** — 값을 내면 몇 장이 펼쳐지고 그중에서 고릅니다.
   * 그래서 카드가 아니라 **봉지**로 그립니다. 크기는 카드에 맞추되 위가 톱니로 뜯기게 되어
   * 있고, 그 톱니 하나가 「이건 여는 것이다」를 말합니다.
   */
  private drawPackRow(left: number, top: number, width: number, tileH: number): void {
    const packs = this.state.shop.packs
    const tileW = PACK_TILE_W
    const gap = 26
    // 상품 줄과 같은 셈입니다 — 팩 칸도 늘어날 수 있습니다.
    const full = packs.length * tileW + Math.max(0, packs.length - 1) * gap
    const room = width - 52
    const fit = full > room ? room / full : 1
    const span = full * fit
    const startX = left + (width - span) / 2

    packs.forEach((packId, slot) => {
      const row = this.data.tables.boosterPack.findByPackId(packId)
      if (!row) return

      const ink = packInk(row.kind)
      const afford = this.shown.money >= row.cost
      const h = SIZE.jokerHeight
      const tile = new Container()
      tile.position.set(startX + slot * (tileW + gap) * fit, top)
      tile.scale.set(fit)

      const bag = new Graphics()
      bag.roundRect(3, 5, tileW, h, 10).fill({ color: 0x000000, alpha: 0.4 })
      bag.roundRect(0, 0, tileW, h, 10).fill(shade(ink, 0.45))
      // 봉지의 몸통. 위쪽이 조금 밝아 빛을 받은 것으로 보입니다.
      bag.roundRect(0, 0, tileW, h * 0.55, 10).fill({ color: ink, alpha: 0.5 })

      // **뜯는 줄.** 톱니 하나가 봉지를 봉지로 만듭니다.
      const tearY = 26
      bag.rect(0, tearY - 7, tileW, 14).fill({ color: 0x0b1018, alpha: 0.35 })
      const teeth = 13
      for (let i = 0; i < teeth; i++) {
        const x = (tileW / teeth) * i
        bag.moveTo(x, tearY)
          .lineTo(x + tileW / teeth / 2, tearY - 4)
          .lineTo(x + tileW / teeth, tearY)
          .stroke({ color: shade(ink, 0.8), width: 1.4, alpha: 0.9 })
      }

      bag.roundRect(1.25, 1.25, tileW - 2.5, h - 2.5, 9)
        .stroke({ color: shade(ink, 0.9), width: 2.5 })
      tile.addChild(bag)

      const label = new Text({
        text: packName(row.kind, row.size),
        style: {
          fontSize: 12, fill: COLOR.ink, fontWeight: '800', align: 'center',
          wordWrap: true, wordWrapWidth: tileW - 14, breakWords: true, lineHeight: 15,
        },
      })
      label.anchor.set(0.5, 0.5)
      label.position.set(tileW / 2, h * 0.52)

      const note = richLine(tf('ui.pack.of', { cards: row.cards, picks: row.picks }), {
        base: { fontSize: 11, fill: 0xdbe4f0 },
        number: COLOR.accentNumber,
        term: COLOR.accentTerm,
      })
      note.position.set((tileW - note.width) / 2, h - 30)
      tile.addChild(label, note)

      const price = new Text({
        text: `$${row.cost}`,
        style: {
          fontSize: 20, fontWeight: '800',
          fill: afford ? COLOR.money : 0x7a6a45,
          stroke: { color: 0x0a0f18, width: 4 },
        },
      })
      price.anchor.set(0.5, 0)
      price.position.set(tileW / 2, h + 8)
      tile.addChild(price)

      tile.alpha = afford ? 1 : 0.55
      tile.eventMode = 'static'
      tile.hitArea = new Rectangle(0, 0, tileW, tileH)
      tile.cursor = afford ? 'pointer' : 'default'
      // **누르면 고르기만 합니다.** 뜯는 것은 그 밑에 서는 단추가 합니다 — 상점의
      // 카드와 같은 규칙이고, 뜯은 팩은 무르지 못합니다.
      this.packSlotTiles.set(slot, { tile, height: h, baseY: tile.y, price,
                                     mid: tile.x + tileW * fit / 2 })
      tile.on('pointertap', () => {
        if (this.ate()) return
        if (!this.canPay(row.cost)) return
        this.pick('pack_slot', slot)
      })
      this.tipOn(tile, () => {
        this.tooltip.show(packName(row.kind, row.size), t('ui.kind.pack'), 0,
          [packBlurb(row.kind), tf('ui.pack.spread', { cards: row.cards, picks: row.picks })],
          tile.x + tileW / 2, tile.y + tileH, SIZE)
      })
      this.reveal(tile)
    })
  }

  /**
   * 상점의 팩 하나를 뜯습니다.
   *
   * **누름과 갈라 두었습니다.** 뜯은 팩은 무르지 못하므로 한 번 더 눌러야 합니다.
   */
  private openPackSlot(slot: number): void {
    const spot = this.packSlotTiles.get(slot)
    if (this.state.shop.packs[slot] === undefined) return

    this.tooltip.hide()
    this.audio.play('pack_open')
    // **카드가 이 딱지에서 나옵니다.** 어느 것을 뜯었는지가 그 움직임에 남습니다. 딱지가
    // 이미 지워졌으면 상점 한가운데에서 나옵니다.
    const from = spot
      ? { x: spot.mid, y: spot.baseY + spot.height / 2 }
      : { x: BOARD_X, y: SHOP_CARD_Y }
    const row = this.data.tables.boosterPack.findByPackId(this.state.shop.packs[slot])
    this.particles.burst(from.x, from.y, 20, row ? packInk(row.kind) : COLOR.ink, 1.2)
    this.jolt(5, 3)
    this.packFrom = from
    this.act({ t: 'buy_pack', slot })
  }

  /** 바우처. 한 안테에 하나이고 런이 끝날 때까지 남습니다. */
  private drawVoucher(left: number, top: number, width: number, tileH: number): void {
    const id = this.state.shop.voucher
    if (!id) {
      const none = new Text({
        text: t('ui.shop.voucher_taken'),
        style: { fontSize: 11, fill: COLOR.inkDim },
      })
      none.anchor.set(0.5, 0.5)
      none.position.set(left + width / 2, top + tileH / 2)
      this.reveal(none)
      return
    }

    const row = this.data.tables.voucher.findByVoucherId(id)
    const lines = describe(this.data, this.data.voucherEffects.get(id) ?? [])
    const cost = this.data.economy.voucherCost
    const afford = this.shown.money >= cost

    const tileW = 460
    const tile = new Panel(tileW, tileH, 0x1d3149)
    tile.position.set(left + (width - tileW) / 2, top)

    const label = new Text({
      text: nameOf(this.data, 'voucher', id, row?.name ?? ''),
      style: { fontSize: 15, fill: COLOR.ink, fontWeight: '800' },
    })
    label.position.set(16, 11)

    const note = richLine(lines[0] ?? t('ui.note.rest_of_run'), {
      base: { fontSize: 11, fill: 0x9fc4e8 },
      number: COLOR.accentNumber,
      term: COLOR.accentTerm,
    }, width - 110, 14)
    note.position.set(16, 36)

    const price = new Text({
      text: `$${cost}`,
      style: {
        fontSize: 18, fill: afford ? COLOR.money : 0x7a6a45, fontWeight: '800',
      },
    })
    price.anchor.set(1, 0.5)
    price.position.set(tileW - 16, tileH / 2)

    tile.addChild(label, note, price)
    tile.alpha = afford ? 1 : 0.55
    tile.eventMode = 'static'
    tile.cursor = afford ? 'pointer' : 'default'
    tile.on('pointertap', () => {
      if (this.ate()) return
      if (!this.canPay(cost)) return
      // **바우처는 들어갈 칸이 없습니다.** 규칙으로 들어가므로, 산 자리에서 이름이 뜨는
      // 것이 그것을 얻었다는 유일한 표시입니다.
      this.audio.play('voucher_buy')
      this.particles.burst(tile.x + tileW / 2, tile.y + tileH / 2, 26, COLOR.money, 1.3, 1.2)
      this.flashPanel(COLOR.money, 0.35)
      this.popAt({ x: tile.x + tileW / 2, y: tile.y + tileH / 2 },
        nameOf(this.data, 'voucher', id, row?.name ?? ''), COLOR.money, 0.5)
      this.act({ t: 'buy_voucher' })
    })
    this.tipOn(tile, () => {
      this.tooltip.show(nameOf(this.data, 'voucher', id, row?.name ?? ''),
        t('ui.kind.voucher'), 0, lines,
        tile.x + tileW / 2, tile.y + tileH, SIZE)
    })
    this.reveal(tile)
  }

  /**
   * 뜯어 놓은 팩.
   *
   * **펼쳐 놓고 하나를 집습니다.** 딱지에 설명을 적어 나란히 세우면 읽고 나서 고르는 일이
   * 되고, 그것은 카드 게임이 아니라 목록입니다.
   *
   * **판을 매 프레임 다시 만들지 않습니다.** 다시 만들면 한 장을 집었을 때 남은 카드가
   * 새 자리에 순간이동합니다 — 카드는 미끄러져 가야 하고, 그러려면 그 카드가 같은 카드로
   * 남아 있어야 합니다. 그래서 뜯을 때 한 번 짓고, 그다음은 자리만 다시 정합니다.
   */
  private syncPack(): void {
    const open = this.state.pack
    // 어느 팩을 뜯었는가. 바뀌면 처음부터 다시 폅니다.
    const key = open ? open.packId + ':' + open.options.length : ''

    if (key !== this.packShown) {
      this.packShown = key
      if (open) this.buildPack()
    }
    if (open) this.layoutPack()
  }

  /** 판을 짓습니다. 뜯을 때 한 번입니다. */
  private buildPack(): void {
    this.packLayer.removeChildren().forEach(child => child.destroy())
    this.packViews.clear()
    this.packGone.length = 0

    const open = this.state.pack
    if (!open) return

    this.packLayer.sortableChildren = true
    this.packLayer.visible = true
    this.packEnter = 0

    // 팩 딱지에 떠 있던 설명을 걷습니다. 뜯은 판 뒤에 남으면 지저분합니다.
    this.tooltip.hide()

    const row = this.data.tables.boosterPack.findByPackId(open.packId)
    const ink = packInk(open.kind)

    // **뒤가 비쳐서는 안 됩니다.** 상점의 카드와 값이 흐릿하게 남으면 펼친 카드와 섞여
    // 어느 것을 고르는 것인지 흐려집니다.
    const veil = new Graphics()
    veil.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000).fill(0x070a10)
    veil.eventMode = 'static'
    veil.zIndex = -10
    this.packVeil = veil
    this.packLayer.addChild(veil)

    // **뜯은 것의 이름입니다.** 26픽셀에 자간 없이 두었더니 그 아래의 지시문과 굵기만
    // 다른 두 줄이 되어서, 무엇을 뜯었는지가 읽히지 않고 지나갔습니다 — 이름은 크게,
    // 자간을 벌려서, 그리고 지시문과 사이를 두어야 이름으로 읽힙니다.
    const title = new Text({
      text: row ? packName(row.kind, row.size) : t('ui.kind.pack'),
      style: {
        fontSize: 34, fill: ink, fontWeight: '800', letterSpacing: 2,
        stroke: { color: 0x070a10, width: 6 },
      },
    })
    title.anchor.set(0.5, 0)
    title.position.set(POPUP_X, PACK_TITLE_Y)

    // **지시문은 여기 한 곳입니다.** 화면 아래에도 같은 말을 두면 덮개 뒤에서 흐릿하게
    // 읽히고, 그것은 남은 글자로 보입니다.
    const note = new Text({
      text: this.packLine(open.picksLeft),
      style: { fontSize: 14, fill: 0xdbe4f0, fontWeight: '700' },
    })
    note.anchor.set(0.5, 0)
    note.position.set(POPUP_X, PACK_TITLE_Y + 60)
    this.packNote = note

    const skip = new Button(t('ui.button.skip'), 160, 40, 0x4a5568,
      () => this.act({ t: 'skip_pack' }))
    // **설명 아래입니다.** 카드 바로 밑에 두면 마우스를 올릴 때 뜨는 설명이 그 위를 덮습니다.
    skip.position.set(POPUP_X - 80, PACK_CARDS_Y + PACK_CARD_H / 2 + 130)
    this.packSkip = skip

    this.packTitle = title

    this.packLayer.addChild(title, note, skip)

    // **카드는 팩에서 나옵니다.** 자기 자리에 그냥 나타나면 무엇이 뜯긴 것인지가 남지
    // 않습니다. 누른 그 딱지의 자리에서 하나씩 미끄러져 나옵니다.
    const from = this.packFrom ?? { x: POPUP_X, y: PACK_CARDS_Y }

    open.options.forEach((item, index) => {
      if (open.taken[index]) return

      const face = this.packCard(item, index)
      const node = face.node
      const motion = new Motion()
      motion.snap(from.x, from.y)
      motion.rotation.snap(0)
      node.position.set(from.x, from.y)
      node.alpha = 0

      this.packViews.set(index, {
        face, motion, index, item,
        // 황금비만큼씩 벌려 둡니다. 정수 배로 벌리면 장수가 짝수일 때 두 장씩 같은 자리가
        // 됩니다.
        sway: index * 2.399_96,
        glow: -1,
      })
      this.packLayer.addChild(node)

      // 하나씩 나옵니다. 나오는 순간에 소리가 하나.
      this.later.push({
        at: this.clock + 0.12 + index * 0.11,
        run: () => {
          node.alpha = 1
          // **나오는 그 한 장이 반짝입니다.** 소리만 나고 그림은 그냥 있으면 다섯 장이
          // 한꺼번에 놓인 것으로 보입니다 — 하나씩 나온다는 것은 하나씩 눈에 띈다는
          // 것이고, 눈에 띄게 하는 것은 그 순간의 빛입니다.
          const one = this.packViews.get(index)
          if (one) one.glow = 0
          // **뜯은 팩에서 나오는 것은 뒤집히는 소리입니다.** 손에 깔리는 것과 갈립니다.
          this.audio.play('card_flip', index * 2)
          this.particles.burst(from.x, from.y, 8, ink, 0.7, 0.6)
        },
      })
    })
  }

  /**
   * 자리를 다시 정합니다.
   *
   * **남은 것만 다시 가운데로 모읍니다** — 집어 간 자리를 비워 두면 부챗살에 이가 빠지고,
   * 순간이동하면 어느 카드가 어디로 갔는지가 남지 않습니다.
   */
  private layoutPack(): void {
    const open = this.state.pack
    if (!open) return

    if (this.packNote) this.packNote.text = this.packLine(open.picksLeft)
    if (this.packSkip) {
      const untouched = open.taken.every(one => !one)
      this.packSkip.text = untouched ? t('ui.button.skip') : t('ui.button.clear')
    }

    // 집어 간 것은 판에서 물러납니다.
    for (const [index, one] of [...this.packViews]) {
      if (!open.taken[index]) continue
      this.packViews.delete(index)
      this.packGone.push({ node: one.face.node, life: 0 })
    }

    const left = [...this.packViews.values()].sort((a, b) => a.index - b.index)
    const spacing = PACK_CARD_W + 26
    const startX = POPUP_X - ((left.length - 1) * spacing) / 2

    left.forEach((one, slot) => {
      // **한 줄로 폅니다.** 부챗살이 보기에는 좋았는데, 집는 단추가 그 곡선 아래 어디에
      // 서든 어느 카드와는 붙고 어느 카드와는 벌어집니다 — 카드가 한 높이에 있어야 그
      // 아래의 한 줄이 셋 모두의 것이 됩니다.
      one.motion.to(startX + slot * spacing, PACK_CARDS_Y, 0)
    })
  }

  /**
   * 펼친 카드의 얼굴을 다시 그립니다.
   *
   * **판을 다시 짓지 않습니다.** 그림은 처음 물어볼 때부터 읽히기 시작하므로 팩을 뜯는
   * 순간에는 아직 없을 수 있고, 그때 판을 다시 지으면 부챗살이 처음부터 다시 펴집니다 —
   * 자리와 용수철은 그대로 두고 얼굴만 바꿉니다.
   */
  private repaintPack(): void {
    for (const one of this.packViews.values()) {
      // **띠는 두고 카드만 갈아 끼웁니다.** 통째로 비우면 띠까지 지워지고, 그러면 자리가
      // 없다는 표시가 그림이 들어오는 순간에 사라집니다.
      const fresh = this.itemCard(one.item)
      one.face.node.removeChild(one.face.card)
      one.face.card.destroy({ children: true })
      one.face.card = fresh
      one.face.node.addChildAt(fresh, 0)
    }
  }

  /** 몇 장을 더 고르는가. 건너뛸 수 있다는 것도 함께 적습니다. */
  private packLine(left: number): string {
    return tf('ui.pack.pick_from', { n: left }) + t('ui.hint.skip_if_unwanted')
  }

  /** 펼쳐 놓는 카드 한 장. */
  /**
   * 어느 자리가 찼는가.
   *
   * **「자리가 없습니다」로는 모자랍니다.** 조커 칸이 찬 것과 소모품 칸이 찬 것은 다음에
   * 할 일이 다릅니다 — 하나는 조커를 팔아야 하고 하나는 소모품을 써야 합니다.
   */
  private fullNote(kind: ShopItemKind): string {
    return t(kind === ShopItemKind.Joker ? 'ui.pack.jokers_full' : 'ui.pack.consumables_full')
  }

  /**
   * 팩에 펼쳐 놓는 카드 한 장.
   *
   * **팩의 색은 이제 여기서 쓰지 않습니다.** 집을 때 터지는 조각의 색이었는데, 집는 일이
   * `takeFromPack` 으로 옮겨 가면서 그 색도 그쪽에서 셉니다.
   */
  private packCard(item: ShopItem, index: number): PackFace {
    const name = shopLabel(item.kind, item.id, this.data)
    const lines = this.shopLines(item)
    const rarity = item.kind === ShopItemKind.Joker
      ? this.data.tables.joker.findByJokerId(item.id)?.rarity ?? 1 : 0

    const node = new Container()
    node.pivot.set(SIZE.jokerWidth / 2, SIZE.jokerHeight / 2)
    node.scale.set(PACK_SCALE)
    const card = this.itemCard(item)
    node.addChild(card)

    // **자리가 있는지가 카드에 보여야 합니다.** 눌러 보고서야 아는 것은 「고르는 것」이
    // 아니라 「찍는 것」입니다. 띠는 미리 만들어 두고 매 프레임 켜고 끕니다 — 팩이 열려
    // 있는 동안 소모품을 쓰거나 조커를 팔면 자리가 생기기 때문입니다.
    const w = SIZE.jokerWidth
    const mark = new Container()
    const plate = new Graphics()
    plate.roundRect(4, SIZE.jokerHeight / 2 - 11, w - 8, 22, 5)
      .fill({ color: 0x1b2331, alpha: 0.95 })
    const label = new Text({
      text: '', style: { fontSize: 9, fill: COLOR.ink, fontWeight: '800' },
    })
    label.anchor.set(0.5)
    label.position.set(w / 2, SIZE.jokerHeight / 2)
    mark.addChild(plate, label)
    mark.visible = false
    node.addChild(mark)

    node.eventMode = 'static'
    node.cursor = 'pointer'
    node.hitArea = new Rectangle(0, 0, SIZE.jokerWidth, SIZE.jokerHeight)
    // 카드가 들리는 것과 설명이 뜨는 것이 함께 있어서 `tipOn` 을 쓰지 않습니다 —
    // **손가락으로는 들리는 것만 먼저 일어나고, 설명은 꾸욱 눌러야 뜹니다.**
    const tip = (): void => {
      this.tooltip.show(name, kindName(item.kind), rarity, lines,
        node.x, node.y + PACK_CARD_H / 2 + 8, SIZE)
    }
    // **마우스를 올리는 것은 설명까지입니다.** 올리기만 해도 카드가 들리면 상점의 칸과
    // 몸짓이 갈립니다 — 상점은 눌러서 고른 것만 들리고, 팩도 그래야 어느 것을 집으려는
    // 중인지가 한 가지로 읽힙니다.
    node.on('pointerover', event => {
      if (event.pointerType === 'mouse') tip()
    })
    node.on('pointerdown', event => this.armPress(event, tip))
    node.on('pointerout', () => {
      if (!this.pressShown) this.tooltip.hide()
    })
    // **누르면 고르기만 합니다.** 집는 것은 그 밑에 서는 단추가 합니다 — 집는 것은
    // 되돌릴 수 없고, 팩은 열려 있는 동안 무엇을 집을지 견주어 보는 자리입니다.
    node.on('pointertap', () => {
      if (this.ate()) return
      this.tooltip.hide()
      this.pick('pack', index)
    })
    return { node, card, mark, plate, label }
  }


  /**
   * 팩에서 한 장을 집습니다.
   *
   * **누름과 갈라 두었습니다.** 카드를 누르는 것은 고르는 것이고, 집는 것은 그 밑에 선
   * 단추입니다 — 되돌릴 수 없는 일이 손이 미끄러진 한 번으로 일어나지 않습니다.
   */
  private takeFromPack(index: number): void {
    const open = this.state.pack
    const view = this.packViews.get(index)
    if (!open || !view) return

    const item = view.item
    const node = view.face.node
    const ink = packInk(open.kind)
    // 집은 카드도 산 것과 같이 제자리에서 옵니다.
    const from = { x: node.x - SIZE.jokerWidth / 2, y: node.y - SIZE.jokerHeight / 2 }

    // **자리가 없으면 무엇과 바꿀지를 묻습니다.** 코어는 자리가 없으면 아무것도 하지
    // 않는데, 화면이 그것을 모른 채 소리와 조각을 내고 있었습니다.
    if (!this.roomFor(item.kind)) {
      if (!this.canSwap(item)) {
        // 내놓을 것도 없습니다. **왜 안 되는지는 적혀야 합니다.**
        this.audio.play('joker_fizzle')
        // **줄로 알립니다.** 머리글은 팩의 제목과 같은 자리라 둘이 겹칩니다.
        this.toasts.push(t('ui.swap.title'), this.fullNote(item.kind), COLOR.bad, 3)
        return
      }
      this.askSwap(item, held => {
        this.audio.play('pack_pick')
        this.arriveFrom = from
        this.sellFrom = item.kind === ShopItemKind.Joker
          ? this.jokerSpot(held) : this.itemSpot(held)
        this.act({ t: 'swap_pack', index, held })
        if (isConsumable(item.kind)) this.itemFlying(from)
      })
      return
    }

    this.audio.play('pack_pick')
    this.particles.burst(node.x, node.y, 26, ink, 1.4, 1.2)
    this.particles.burst(node.x, node.y, 12, 0xffffff, 0.8, 0.7)
    this.arriveFrom = from
    this.jolt(4, 3)
    this.act({ t: 'pick_pack', index })
    // **소모품도 집은 자리에서 옵니다.** 조커는 `arriveFrom` 을 뷰가 받아 날아오는데,
    // 소모품은 화면이 그 몫을 들어야 합니다.
    if (isConsumable(item.kind)) this.itemFlying(from)
    this.later.push({ at: this.clock + LAND_AT, run: () => this.landed(item) })
  }

  /**
   * 자리가 없는 카드에 띠를 답니다.
   *
   * **매 프레임 다시 정합니다.** 팩이 열려 있는 동안에도 소모품을 쓰거나 조커를 팔 수 있고,
   * 그러면 방금까지 못 집던 것을 집을 수 있게 됩니다 — 그릴 때 한 번 정하면 그 띠가 실제와
   * 어긋납니다.
   */
  private markPackFace(face: PackFace, item: ShopItem): void {
    const room = this.roomFor(item.kind)
    face.card.alpha = room ? 1 : 0.62
    face.mark.visible = !room
    if (room) return

    const swap = this.canSwap(item)
    const want = t(swap ? 'ui.pack.swap_mark' : 'ui.pack.full_mark')
    if (face.label.text === want) return

    const w = SIZE.jokerWidth
    face.label.text = want
    face.label.style.fill = swap ? COLOR.ink : 0xffc4cc
    face.plate.clear()
    face.plate.roundRect(4, SIZE.jokerHeight / 2 - 11, w - 8, 22, 5)
      .fill({ color: swap ? 0x2f3a4e : 0x4a1f28, alpha: 0.95 })
    face.plate.roundRect(4.5, SIZE.jokerHeight / 2 - 10.5, w - 9, 21, 5)
      .stroke({ color: swap ? COLOR.accentTerm : COLOR.bad, width: 1.2, alpha: 0.9 })
  }

  /**
   * 나오는 한 장이 반짝이는 것.
   *
   * **팩에서 나오는 그 순간의 한 장에만 붙습니다.** 카드가 자기 자리로 미끄러지는 것은
   * 이미 있었고, 없던 것은 「지금 이 한 장이 나왔다」입니다 — 다섯 장이 0.11초 간격으로
   * 나오므로 그 사이를 채우는 것이 없으면 다섯 장이 한꺼번에 놓인 것으로 읽힙니다.
   *
   * **켜지는 것도 꺼지는 것도 사인 한 마디입니다.** 1 에서 시작해 잦아드는 쪽은 켜지는
   * 순간이 계단이 되고, 그것은 부드럽게 반짝이는 것이 아니라 한 번 터지는 것입니다.
   * 조커가 자리에 닿을 때의 번쩍임이 그쪽이고, 그것은 「닿았다」라서 그렇습니다.
   *
   * 필터는 반짝이는 동안에만 붙입니다 — 필터 하나가 곧 렌더 텍스처 하나이고, 다 반짝인
   * 카드가 그것을 계속 들고 있을 이유가 없습니다.
   */
  private advancePackGlow(one: PackView, seconds: number): void {
    if (one.glow < 0 || one.glow >= 1) return

    one.glow = Math.min(1, one.glow + seconds / 0.46)
    const wave = Math.sin(one.glow * Math.PI)

    if (one.glow >= 1) {
      one.face.node.filters = []
      one.arrive = undefined
      return
    }

    if (!one.arrive) {
      one.arrive = new ArriveFilter()
      one.face.node.filters = [one.arrive]
    }
    one.arrive.at(this.clock)
    // 빛은 물결 그대로, 울렁임은 그 절반보다 작게. **둘이 같은 세기면 반짝이는 것이
    // 아니라 카드가 녹습니다.**
    one.arrive.flash = wave * 0.85
    one.arrive.warp = wave * 0.3
  }

  /**
   * 펼친 팩이 도는 것.
   *
   * 덮개가 짙어지고 · 카드가 자리로 미끄러지고 · 마우스를 올린 한 장이 올라오고 · 집어 간
   * 것이 물러납니다. 다 닫혔으면 판을 걷습니다.
   */
  private advancePack(seconds: number): void {
    const open = this.state.pack !== null
    // **덮개는 서서히 짙어집니다.** 한 프레임에 덮이면 상점이 툭 꺼진 것으로 보입니다.
    this.packEnter += ((open ? 1 : 0) - this.packEnter) * Math.min(1, seconds * 11)
    // **판을 덮는 것보다 짙습니다.** 다른 덮개는 뒤가 무엇이었는지 남기려고 옅지만, 이
    // 덮개가 가리는 것은 상점이고 상점의 카드와 값이 흐릿하게 남으면 펼친 카드와 섞입니다.
    if (this.packVeil) this.packVeil.alpha = 0.86 * this.packEnter

    if (!this.packLayer.visible) return

    if (!open && this.packEnter < 0.03) {
      this.packLayer.removeChildren().forEach(child => child.destroy())
      this.packViews.clear()
      this.packGone.length = 0
      this.packVeil = undefined
      this.packNote = undefined
      this.packSkip = undefined
      this.packTitle = undefined
      this.packLayer.visible = false
      return
    }

    // 글과 버튼은 덮개와 함께 들고 납니다.
    if (this.packNote) this.packNote.alpha = this.packEnter
    if (this.packSkip) this.packSkip.alpha = this.packEnter
    if (this.packTitle) this.packTitle.alpha = this.packEnter

    for (const one of this.packViews.values()) {
      one.motion.advance(seconds)
      const node = one.face.node
      // 올라오는 것은 **고른 것 하나**입니다. 상점의 칸과 같은 규칙이고, 고른 카드는 손을
      // 떼어도 올라와 있어야 무엇을 집으려는 중인지가 남습니다.
      const up = this.held?.kind === 'pack' && this.held.uid === one.index

      // **펼쳐 놓은 카드는 가만히 있지 않습니다.** 자리에 닿은 뒤로 아무것도 움직이지
      // 않으면 고르는 화면이 그림 한 장이 됩니다. 살짝 갸웃거리고 아주 조금 떠 있습니다 —
      // 눈에 띄면 그것은 이미 큰 것이라, 각도는 1.6도이고 높이는 2픽셀입니다.
      //
      // **올린 한 장은 잦아듭니다.** 들여다보는 중인 카드가 계속 흔들리면 읽기 어렵고,
      // 멈추는 것 자체가 「이것을 보고 있다」가 됩니다.
      const alive = up ? 0.22 : 1
      const tilt = Math.sin(this.clock * 1.15 + one.sway) * 1.6 * alive
      const bob = Math.sin(this.clock * 0.83 + one.sway * 1.6) * 2 * alive

      one.motion.scale.target = up ? PACK_SCALE * 1.07 : PACK_SCALE
      node.position.set(one.motion.x.value, one.motion.y.value - (up ? 22 : 0) + bob)
      node.rotation = (one.motion.rotation.value + tilt) * (Math.PI / 180)
      node.scale.set(one.motion.scale.value)
      node.zIndex = up ? 10 : 0
      this.markPackFace(one.face, one.item)
      this.advancePackGlow(one, seconds)
      // 닫히는 동안에는 카드도 함께 물러납니다.
      if (!open) node.alpha = this.packEnter
    }

    for (let i = this.packGone.length - 1; i >= 0; i--) {
      const gone = this.packGone[i]
      gone.life += seconds / 0.18
      gone.node.alpha = Math.max(0, 1 - gone.life)
      gone.node.scale.set(PACK_SCALE * (1 + gone.life * 0.3))
      if (gone.life < 1) continue
      this.packGone.splice(i, 1)
      gone.node.destroy({ children: true })
    }
  }

  private shopLines(item: { kind: ShopItemKind; id: string }): string[] {
    switch (item.kind) {
      case ShopItemKind.Joker:
        return describe(this.data, this.data.jokerEffects.get(item.id) ?? [])
      case ShopItemKind.Tarot: return this.consumableLines(1, item.id)
      case ShopItemKind.Planet: return this.consumableLines(2, item.id)
      case ShopItemKind.Spectral: return this.consumableLines(3, item.id)
      default: return []
    }
  }
}

/**
 * 작은 것 위에 얹는 흰 빛.
 *
 * **셰이더 대신입니다.** 카드에 쓰는 `ArriveFilter` 는 카드 크기에 맞춰 여백을 잡아 두어서
 * 작은 것에 걸면 그림이 밀립니다 — 26픽셀짜리에 필요한 것은 왜곡이 아니라 밝아짐 하나이고,
 * 그것은 흰 원 하나를 얹는 것으로 됩니다.
 */
function glare(size: number, strength: number): Graphics {
  const lit = new Graphics()
  lit.circle(size / 2, size / 2, size / 2).fill({ color: 0xffffff, alpha: strength })
  lit.blendMode = 'add'
  return lit
}

/** 점 하나가 쉬는 자리의 네모 안에 있는가. */
function near(point: { x: number; y: number },
              motion: { x: { target: number }; y: { target: number } },
              width: number, height: number): boolean {
  return Math.abs(point.x - motion.x.target) <= width / 2
    && Math.abs(point.y - motion.y.target) <= height / 2
}

/** 돈이 왜 오갔는가. 표에 없는 갈래는 적지 않습니다. */
/** 바뀐 규칙의 이름. 표에 없는 것은 식별자를 그대로 적습니다. */
/**
 * 규칙 하나가 어떻게 바뀌었는가.
 *
 * **읽는 법이 셋입니다** — 켜고 끄는 것, 수를 세는 것, 만분율로 적힌 배수. 셋을 한 가지로
 * 적으면 확률 배수가 `10000 → 20000` 으로 뜹니다.
 *
 * 값을 가지지 않는 규칙도 있습니다 — 덱을 다시 뽑거나 그림 카드를 빼는 것들이고, 그때는
 * 이름 한 줄이 전부입니다.
 */
function ruleChange(event: { before: number | null; after: number | null;
                             flag: boolean; rule: string }): string {
  if (event.after === null) return t('ui.note.applies_all_run')
  if (event.flag) return event.after !== 0 ? t('ui.note.turned_on') : t('ui.note.turned_off')

  if (RULE_IS_SCALE.has(event.rule) || RULE_IS_MULTIPLIER.has(event.rule)) {
    const unit = RULE_IS_SCALE.has(event.rule) ? 10_000 : 1
    const to = (event.after / unit).toFixed(2)
    if (event.before === null || event.before === event.after) return `×${to}`
    return `×${(event.before / unit).toFixed(2)}  →  ×${to}`
  }

  // 할인은 만분율이고 **낮을수록 좋습니다.** 배수로 적으면 그 방향이 뒤집혀 읽힙니다.
  if (event.rule === 'ShopDiscount') {
    return tf('ui.rule.discount', { n: (event.after / 100).toFixed(0) })
  }

  if (event.before === null || event.before === event.after) return String(event.after)
  const delta = event.after - event.before
  return `${event.before}  →  ${event.after}   (${delta > 0 ? '+' : ''}${delta})`
}

/**
 * 규칙 값 하나를 읽을 수 있게.
 *
 * **단위는 규칙의 성질이고 읽는 법은 화면의 몫입니다** — `ruleChange` 와 같은 표를 봅니다.
 */
function ruleValue(rule: string, value: number): string {
  if (RULE_IS_SCALE.has(rule)) return `×${(value / 10_000).toFixed(2)}`
  if (RULE_IS_MULTIPLIER.has(rule)) return `×${value.toFixed(2)}`
  if (rule === 'shopDiscount') return `${(value / 100).toFixed(0)}%`
  return String(value)
}

/**
 * 만분율로 적힌 규칙들.
 *
 * 값의 단위는 규칙의 성질이지만 **읽는 법은 화면의 몫이라** 여기 있습니다 —
 * `moneyReason` · `valueText` 와 같은 자리입니다.
 */
const RULE_IS_SCALE = new Set([
  'BlindSizeScale', 'PlanetGivesMult',
])

/**
 * 백분율로 적힌 규칙들.
 *
 * 나머지 배수들은 만분율이 아니라 그냥 곱하는 수입니다 — `probabilityScale` 의 기본값이
 * 1이고 2가 되면 두 배라는 뜻입니다. 단위를 지레짐작하면 `1 → 2` 가 `×0.00` 으로 뜹니다.
 */
const RULE_IS_MULTIPLIER = new Set([
  'ShopWeightTarot', 'ShopWeightPlanet', 'ProbabilityScale', 'EditionWeightScale',
])

/**
 * 효과 하나가 낸 값을 글로.
 *
 * **연산마다 읽는 법이 다릅니다** — 곱은 `×`, 가산은 `+`, 돈은 `$` 입니다. 한 자리에서
 * 정하지 않으면 카드와 조커와 런이 각자 다르게 적게 됩니다.
 */
function valueText(op: string, chips: number, mult: number, money: number): string {
  if (op === 'MulMult') return `×${(mult / 10_000).toFixed(2)}`
  if (op === 'GrowSelf') return t('ui.note.grew')
  if (money !== 0) return `${money > 0 ? '+' : ''}$${money}`
  if (chips !== 0) return `+${chips}`
  if (mult !== 0) return `+${(mult / 10_000).toFixed(0)}`
  return t('ui.label.triggered')
}

/** `AllCardsScore` 를 `all_cards_score` 로. 글 표의 식별자가 그 모양입니다. */
function snake(name: string): string {
  return name.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase()
}

/** 색 하나를 셰이더가 받는 0..1 셋으로. */
function rgbOf(color: number): [number, number, number] {
  return [
    ((color >> 16) & 0xff) / 255,
    ((color >> 8) & 0xff) / 255,
    (color & 0xff) / 255,
  ]
}

function moneyReason(reason: string): string {
  switch (reason) {
    case 'blind': return t('ui.label.reward')
    case 'interest': return t('ui.money.interest')
    case 'hands_left': return t('ui.label.hands_left')
    case 'discards_left': return t('ui.label.discards_left')
    // **파는 것과 사는 것도 적습니다.** 이 둘이 비어 있어서 금액만 조용히 바뀌었고,
    // 바꿀 때는 들어온 것과 나간 것이 한 프레임 안에 섞여 「알아서 들어갔네」가 되었습니다.
    case 'sell': return t('ui.money.sell')
    case 'shop': return t('ui.money.spent')
    case 'rental': return t('ui.money.rental')
    default: return ''
  }
}

function blindName(blind: BlindKind): string {
  return blind === BlindKind.Small ? t('ui.blind.small') : blind === BlindKind.Big ? t('ui.blind.big') : t('ui.blind.boss')
}


/** 무늬 하나의 글자. 작은 카드 얼굴이 씁니다. */
const SUIT_PIP: Record<number, string> = {
  [SuitKind.Spade]: '♠',
  [SuitKind.Heart]: '♥',
  [SuitKind.Club]: '♣',
  [SuitKind.Diamond]: '♦',
}

/** 팩 이름. 표가 갈래와 크기만 정하므로 이름은 여기서 짓습니다. */
function packName(kind: PackKind, size: PackSize): string {
  const body = kind === PackKind.Arcana ? t('ui.pack.arcana')
    : kind === PackKind.Celestial ? t('ui.pack.celestial')
    : kind === PackKind.Spectral ? t('ui.kind.spectral')
    : kind === PackKind.Buffoon ? t('ui.enhancement.mult')
    : t('ui.pack.standard')
  const scale = size === PackSize.Jumbo ? t('ui.pack.size.jumbo') : size === PackSize.Mega ? t('ui.pack.size.mega') : ''
  return tf('ui.pack.named', { scale, body })
}

function packBlurb(kind: PackKind): string {
  switch (kind) {
    case PackKind.Arcana: return t('ui.pack.note.arcana')
    case PackKind.Celestial: return t('ui.pack.note.celestial')
    case PackKind.Spectral: return t('ui.pack.note.spectral')
    case PackKind.Buffoon: return t('ui.pack.note.buffoon')
    default: return t('ui.pack.note.standard')
  }
}

function packInk(kind: PackKind): number {
  switch (kind) {
    case PackKind.Arcana: return 0x4a3a6b
    case PackKind.Celestial: return 0x264a6b
    case PackKind.Spectral: return 0x3a2a52
    case PackKind.Buffoon: return 0x6b3a3a
    default: return 0x2f5c42
  }
}

function kindName(kind: ShopItemKind): string {
  switch (kind) {
    case ShopItemKind.Joker: return t('ui.guide.joker.head')
    case ShopItemKind.Tarot: return t('ui.kind.tarot')
    case ShopItemKind.Planet: return t('ui.kind.planet')
    case ShopItemKind.Spectral: return t('ui.kind.spectral')
    default: return t('ui.kind.card')
  }
}

function shopLabel(kind: ShopItemKind, id: string, data: Data): string {
  switch (kind) {
    case ShopItemKind.Joker: return nameOf(data, 'joker', id, id)
    case ShopItemKind.Tarot: return nameOf(data, 'tarot', id, id)
    case ShopItemKind.Planet: return nameOf(data, 'planet', id, id)
    case ShopItemKind.Spectral: return nameOf(data, 'spectral', id, id)

    // **플레잉 카드는 이름이 표에 없습니다.** 이름을 52개 적어 둘 것이 아니라 무늬와
    // 랭크로 짓는 것이고, 그 둘은 이미 말마다 번역되어 있습니다 — 적어 두지 않아서
    // 식별자 `D2` 가 그대로 딱지에 떴습니다.
    case ShopItemKind.PlayingCard: {
      const row = data.tables.baseDeckCard.findByCardId(id)
      if (!row) return id
      const suit = text(data, `phrase.suit.${SuitKind[row.suit]}`)
      return `${suit} ${MINI_RANK[row.rank] ?? row.rank}`
    }

    default: return id
  }
}
