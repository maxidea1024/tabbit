// 옵션.
//
// **여기 있는 것은 전부 실제로 무언가를 합니다.** 켜고 끄는 자리를 늘어놓고 아무것도 하지
// 않으면 그것은 옵션이 아니라 장식입니다.
//
// 갈래가 셋이라 탭입니다 — 한 줄로 늘어놓으면 무엇을 찾는지 알고 있어야 찾을 수 있고,
// 「소리가 크다」와 「연출이 느리다」는 서로 다른 자리에서 찾게 됩니다.

import type { PoolChoice } from '../core/pool'
import { Container, Graphics, Rectangle, Sprite, Text } from 'pixi.js'

import { detectLanguage, type Language, LANGUAGE_NAMES, LANGUAGES, t, tf } from '../core/strings'
import type { Data } from '../core/data'
import { artFor, onArtReady } from '../render/art'
import { setLookOf, setsOf, type SetLook } from '../render/card-set'
import { cardArtId, drawFace, drawSuit } from '../render/pips'
import { SuitKind } from '../generated/enums/suit-kind'
import { COLOR, SIZE } from '../render/theme'
import type { ToolSpot } from './layout'
import { FOOTER_BAR, panelFrame, TITLE_BAR, type ModalPanel } from './modal'
import { richLine, type RichStyle } from './rich'
import { Tooltip } from './tooltip'
import { Button } from './widgets'
import { randomSeed } from './title'

/** 이 판의 설명 줄에 붙는 강조. */
const RICH: RichStyle = {
  base: { fontSize: 11, fill: COLOR.inkDim },
  number: COLOR.accentNumber,
  term: COLOR.accentTerm,
}

/**
 * 화면과 소리와 연출에 관한 것들.
 *
 * **판단은 사람마다 다르고 그래서 여기 있습니다.** 흔들림이 거슬리는 사람과 흔들림이 없으면
 * 심심한 사람이 같은 값을 쓸 이유가 없습니다.
 */
export interface Options {
  /** 소리를 내는가. */
  sound: boolean
  /** 음량. 0 에서 100 입니다. */
  volume: number
  /**
   * 배경음을 내는가.
   *
   * **효과음과 따로입니다.** 효과음은 무엇이 일어났는지를 알리는 것이라 켜 두고, 배경음은
   * 취향이라 끄고 싶은 사람이 있습니다.
   */
  music: boolean
  /** 배경음의 음량. 효과음이 잘 들리는 크기와 배경음이 방해되지 않는 크기는 다릅니다. */
  musicVolume: number
  /** 연출의 배속. 1 · 2 · 4 중 하나입니다. */
  speed: number
  /** 화면이 흔들리는가. */
  shake: boolean
  /** 카드 뒤에서 파티클이 터지는가. */
  particles: boolean
  /** 큰 값에서 색이 갈라지는가. */
  chromatic: boolean
  /** 어느 카드를 고르면 좋은지 표시하는가. */
  hints: boolean
  /**
   * 화면의 글이 어느 말인가.
   *
   * **고른 적이 없으면 비어 있습니다.** 그때는 이 기계의 언어를 따릅니다 — 기본값을 한국어로
   * 박아 두면 독일에서 처음 여는 사람이 한국어를 보게 됩니다.
   */
  language: Language | ''
  /**
   * 다음 판을 어느 덱으로 시작하는가. `Deck.deck_id` 입니다.
   *
   * **표에 없는 값일 수 있습니다.** 손으로 고친 저장소나 예전 판의 값이 그러하므로, 쓰는
   * 쪽이 `validSetup` 으로 걸러 붉은 덱으로 되돌립니다 — 여기서 표를 볼 수는 없습니다.
   */
  deck: string
  /** 어느 스테이크로 시작하는가. `StakeKind` 의 이름입니다. */
  stake: string
  /**
   * 트럼프 52장을 어느 벌로 보는가. `CardSet.set_id` 입니다.
   *
   * **겉모습뿐입니다.** 규칙에 닿지 않으므로 도는 판에도 곧바로 적용되고, 리플레이와
   * 해시에는 들어가지 않습니다.
   */
  cardSet: string
  /**
   * 어느 조커 풀로 하는가.
   *
   * **기본이 `base` 입니다.** 켜진 채로 시작하면 원작을 기대한 사람이 모를 조커를
   * 만나게 되고, 굽어 둔 리플레이와도 어긋납니다.
   */
  pool: PoolChoice
}

export function defaultOptions(): Options {
  return {
    sound: true, volume: 60, music: true, musicVolume: 40, speed: 1,
    shake: true, particles: true, chromatic: true, hints: true,
    language: '', deck: 'red_deck', stake: 'White', cardSet: 'classic', pool: 'base',
  }
}

/**
 * 지금 쓸 언어.
 *
 * 고른 적이 있으면 그것이고, 없으면 이 기계의 언어입니다. **매번 다시 재는 이유**는 기계의
 * 언어가 바뀔 수 있기 때문입니다.
 */
export function chosen(options: Options): Language {
  if (options.language !== '') return options.language
  return detectLanguage(typeof navigator === 'undefined'
    ? [] : [...(navigator.languages ?? []), navigator.language ?? ''])
}

const KEY = 'clover.options'

/** 지난번에 정한 것. 저장소가 막힌 브라우저에서는 기본값입니다. */
export function loadOptions(): Options {
  const options = defaultOptions()
  try {
    const raw = localStorage.getItem(KEY)
    if (raw === null) return options
    const saved = JSON.parse(raw) as Partial<Options>
    for (const key of Object.keys(options) as (keyof Options)[]) {
      const value = saved[key]
      if (typeof value !== typeof options[key]) continue
      // **모르는 값은 받지 않습니다.** 저장된 말 코드가 목록에 없으면 그 칸을 찾는
      // 자리가 빈 값을 읽고, 게임이 부팅에서 멈춥니다 — 손으로 고친 저장소나 예전
      // 판의 값이 그러합니다.
      if (key === 'language' && value !== ''
          && !LANGUAGES.includes(value as Language)) continue
      if (key === 'pool' && value !== 'base' && value !== 'all') continue
      (options[key] as unknown) = value
    }
  } catch {
    // 저장소가 막혀 있으면 기본값으로 갑니다. 옵션 하나 때문에 화면이 서지 않습니다.
  }
  return options
}

export function saveOptions(options: Options): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(options))
  } catch {
    // 저장하지 못하는 것은 이번 판에만 적용된다는 뜻이지 오류가 아닙니다.
  }
}

/**
 * 판의 폭.
 *
 * **여섯 말을 세 칸씩 세우고 카드 여섯 벌을 세 칸씩 세우는 폭입니다.** 좁으면 칸마다의
 * 카드가 무엇인지 보이지 않고, 고르는 것이 겉모습이므로 그것이 보이지 않으면 뜻이 없어집니다.
 */
const WIDTH = 720
/**
 * 판의 가장 낮은 높이.
 *
 * **글이 길면 그만큼 자랍니다.** 못박아 두면 말을 바꿨을 때 — 독일어가 한국어보다 깁니다 —
 * 마지막 줄이 판 밖으로 나갑니다. 낮은 값을 두는 것은 탭을 옮길 때마다 판이 들썩이지 않게
 * 하기 위한 것입니다.
 */
const MIN_HEIGHT = 348 + FOOTER_BAR
/**
 * 판의 가장 높은 높이.
 *
 * **판이 내용만큼 자라게 두지 않습니다.** 화면이 800이므로 탭 하나가 길어지면 판이 화면
 * 밖으로 나가고, 그때 마지막 줄은 어디에도 없습니다 — 넘치는 만큼은 본문이 굴러갑니다.
 */
const MAX_HEIGHT = 620
/** 굴림 한 번에 움직이는 거리. */
const WHEEL_STEP = 48
/**
 * 탭 하나의 높이. **본문과 이어져 보여야 탭입니다.**
 *
 * 폭은 못박지 않고 탭 수로 나눕니다 — 못박아 두면 탭이 하나 늘었을 때 마지막 것이 판
 * 밖으로 나가고, 그것을 아무도 보지 않습니다.
 */
const TAB_H = 36
/** 탭 줄의 윗변. */
const TAB_Y = TITLE_BAR + 14
/** 본문의 바탕. 고른 탭이 같은 색이라 둘이 한 장으로 보입니다. */
const BODY = 0x1c2431
const EDGE = 0x55637a
/** 값을 고르는 줄 하나의 높이. */
const ROW = 52
/** 시드를 적을 수 있는 길이. 주소에 실려 나가므로 길게 둘 이유가 없습니다. */
const SEED_MAX = 28
/** 시드 줄의 높이. 칸과 「무작위」 가 나란히 섭니다. */
const SEED_ROW = 96
/** 고를 것들이 서는 격자. 세 칸씩입니다. */
const CHOICE_COLUMNS = 3
const CHOICE_H = 36
const CHOICE_GAP = 12
/**
 * 카드 넉 장을 세운 칸.
 *
 * **손패보다 작지만 무늬의 색이 읽히는 크기입니다.** 넉 장이 어긋나 겹쳐 서므로 칸의 폭은
 * 한 장의 폭에 걸음 셋을 더한 것입니다.
 */
const CARD_W = 54
const CARD_H = 76
const CARD_STEP = 37
/** 카드와 이름과 테두리를 합친 한 칸의 높이. */
const CARD_ROW_H = CARD_H + 34
/**
 * 겉모습을 고르는 격자의 열 수.
 *
 * **한 줄에 밀어넣지 않습니다.** 세트가 늘어나면 칸이 좁아지고, 좁아진 칸의 카드는 무엇을
 * 고르는지 보이지 않습니다 — 고르는 것이 겉모습이므로 그것이 보이지 않으면 이 줄의 뜻이
 * 없어집니다. 판의 높이는 가장 긴 탭이 정하므로 줄이 늘어나면 판이 자랍니다.
 */
const CARD_COLUMNS = 3
/** 미리보기의 모서리에 적히는 글자. `card-view.ts` 의 것과 같습니다. */
const RANK_TEXT: Record<number, string> = {
  4: '4', 7: '7', 13: 'K', 14: 'A',
}
/** 칸마다 세우는 넉 장. **무늬가 넷이므로 넷입니다** — 랭크는 그림이 있는 벌이 잘 보이게 섞습니다. */
const PREVIEW: { suit: SuitKind; rank: number }[] = [
  { suit: SuitKind.Spade, rank: 13 },
  { suit: SuitKind.Heart, rank: 7 },
  { suit: SuitKind.Club, rank: 4 },
  { suit: SuitKind.Diamond, rank: 14 },
]

interface Row {
  label: string
  /** 지금 값을 글로. */
  read: () => string
  /** 눌렀을 때. **다음 값으로 넘어갑니다** — 값이 둘이나 셋뿐이라 이것으로 충분합니다. */
  next: () => void
  note?: string
  /**
   * 고를 것이 여럿이면 목록으로 세웁니다.
   *
   * **넘기는 단추로는 여섯 개를 고를 수 없습니다.** 하나를 지나치면 다섯 번을 더 눌러야
   * 돌아오고, 무엇이 있는지도 다 눌러 봐야 압니다.
   */
  choices?: { key: string; label: string }[]
  /**
   * 이 줄을 도구가 짚을 이름.
   *
   * **글로 짚을 수 없습니다.** 줄의 이름은 말에 따라 달라지고, 자리는 탭과 글 길이에 따라
   * 달라집니다 — 검증 도구가 좌표를 못박고 있어서 줄이 하나 늘 때마다 빈자리를 눌러 놓고
   * 통과했습니다.
   */
  id?: string
  current?: () => string
  pick?: (key: string) => void
  /** 글을 적는 줄. 지금은 시드 하나뿐입니다. */
  seed?: true
  /**
   * 고르는 것이 겉모습일 때.
   *
   * **글자로는 고를 수 없습니다.** 무엇을 고르는가가 「그 카드가 어떻게 보이는가」이므로,
   * 이름만 적힌 단추 셋에서는 고르고 판을 열어 본 뒤에야 무엇을 골랐는지 알게 됩니다 —
   * 그래서 칸마다 그 벌의 카드 넉 장을 그립니다.
   */
  cards?: { key: string; label: string }[]
}

interface Tab {
  /**
   * 이 탭을 도구가 짚을 이름.
   *
   * **`name` 으로 짚을 수 없습니다.** 그것은 말에 따라 달라지므로, 도구가 그것으로 찾으면
   * 말을 바꾼 판에서는 찾지 못합니다.
   */
  id: string
  name: string
  rows: Row[]
}

export class OptionsPanel implements ModalPanel {
  readonly view = new Container()
  /**
   * 판의 머리띠와 밑단.
   *
   * **매번 새로 만드는 유일한 자식입니다.** 높이가 탭의 길이로 정해지고 그 길이는 말에
   * 따라 달라지므로 다시 그려야 하며, 나머지 자식들은 그대로 두고 이것만 지웁니다.
   */
  private frame?: Container
  /** 이름이 붙은 칸들. **검증 도구가 짚을 자리입니다.** */
  private readonly choiceNodes = new Map<string, ToolSpot>()
  /** 탭 줄의 칸들. **본문과 따로 셉니다** — 지워지는 때가 다릅니다. */
  private readonly tabNodes = new Map<string, ToolSpot>()

  /**
   * 검증 도구가 짚을 것들.
   *
   * **자리를 화면 좌표로 바꾸는 것은 이 판의 일이 아닙니다.** 판이 어디에 섰는지는 판을
   * 띄운 쪽이 알므로, 어느 컨테이너의 어디인지까지만 넘기고 셈은 그쪽에서 합니다.
   */
  get toolSpots(): [string, ToolSpot][] {
    return [...this.tabNodes, ...this.choiceNodes]
  }
  readonly size = { width: WIDTH, height: MIN_HEIGHT }

  /** 지금 판의 높이. 가장 긴 탭이 정합니다. */
  private get height(): number {
    return this.size.height
  }

  private set height(value: number) {
    ;(this.size as { width: number; height: number }).height = value
  }

  private readonly body = new Container()
  private readonly tabRow = new Container()
  /** 본문이 담기는 창. 이 밖으로 나간 줄은 잘립니다. */
  private readonly viewport = new Container()
  /** 창의 모양. `viewport` 의 마스크입니다. */
  private readonly clip = new Graphics()
  /** 오른쪽의 손잡이. 넘치는 만큼만 섭니다. */
  private readonly bar = new Graphics()
  /** 본문이 얼마나 굴러갔는가. 0 이 맨 위입니다. */
  private scroll = 0
  /** 이 탭의 본문이 창보다 얼마나 긴가. 0 이면 굴러갈 것이 없습니다. */
  private over = 0
  private tab = 0

  /**
   * 시드.
   *
   * **판 밖에서만 고칠 수 있습니다.** 판이 돌기 시작하면 그 판의 시드이고, 도는 중에
   * 바꾸면 지금 보고 있는 패와 적힌 시드가 어긋납니다.
   */
  private seedText = ''
  private seedEditable = false
  private editing = false
  private buffer = ''
  /** 시드가 정해졌을 때. 화면이 판을 다시 만듭니다. */
  onSeed?: (seed: string) => void

  constructor(data: Data, private readonly options: Options,
              private readonly onChange: () => void,
              private readonly onClose: () => void) {
    // **표를 먼저 읽습니다.** 탭을 세우는 것이 `relabel` 이고 카드 탭이 이 목록을 씁니다.
    for (const one of setsOf(data)) {
      this.sets.push(one)
      this.looks.set(one.setId, setLookOf(data, one.setId))
    }
    // **그림은 나중에 옵니다.** `artFor` 는 처음에 없다고 답하고 읽기를 시작하므로, 다시
    // 그리지 않으면 미리보기가 영원히 그린 얼굴로 남습니다 — 판에서 카드가 그렇게 그려지는
    // 것과 같은 규약이고, 판은 매 프레임 다시 그리지만 이 판은 그렇지 않습니다.
    for (const look of this.looks.values()) {
      if (look.artDir === undefined) continue
      for (const want of PREVIEW) artFor(look.artDir, cardArtId(want.suit, want.rank))
    }
    onArtReady(() => { if (this.view.parent) this.draw() })
    this.relabel()

    // **바퀴는 판 위에서만 받습니다.** 화면 전체에 걸면 판이 닫힌 뒤에도 받게 되고, 뒤의
    // 화면이 같은 바퀴로 움직입니다.
    this.view.eventMode = 'static'
    this.view.on('wheel', event => {
      if (this.over <= 0) return
      event.preventDefault()
      this.scroll -= Math.sign(event.deltaY) * WHEEL_STEP
      this.scroll = Math.max(-this.over, Math.min(0, this.scroll))
      this.body.y = this.scroll
      this.fitScroll(this.over + this.windowHeight)
    })

    // **적는 동안의 키는 이 판의 것입니다.** 뒤에 있는 화면이 같은 키를 받으면 `Esc` 로
    // 판이 닫히거나 연출이 건너뛰어집니다.
    window.addEventListener('keydown', event => {
      if (!this.editing) return
      event.preventDefault()
      event.stopImmediatePropagation()
      this.typed(event.key)
    })
    window.addEventListener('paste', event => {
      if (!this.editing) return
      event.preventDefault()
      for (const one of event.clipboardData?.getData('text') ?? '') this.typed(one)
    })
  }

  /** 지금 시드와, 그것을 고칠 수 있는지. 판을 열기 전에 화면이 정합니다. */
  setSeed(seed: string, editable: boolean): void {
    if (this.seedText === seed && this.seedEditable === editable) return
    this.seedText = seed
    this.seedEditable = editable
    this.editing = false
    this.buffer = ''
    this.draw()
  }

  private typed(key: string): void {
    if (key === 'Enter') {
      const next = this.buffer.trim()
      this.editing = false
      this.buffer = ''
      if (next !== '' && next !== this.seedText) {
        this.seedText = next
        this.onSeed?.(next)
      }
      this.draw()
      return
    }
    if (key === 'Escape') {
      this.editing = false
      this.buffer = ''
      this.draw()
      return
    }
    if (key === 'Backspace') {
      this.buffer = this.buffer.slice(0, -1)
      this.draw()
      return
    }
    // **글자와 숫자와 `-` `_` 만 받습니다.** 시드는 주소에 실려 나가므로 그 밖의 글자는
    // 옮겨 적히는 사이에 달라집니다.
    if (key.length !== 1 || !/[A-Za-z0-9\-_]/.test(key)) return
    if (this.buffer.length >= SEED_MAX) return
    this.buffer += key
    this.draw()
  }


  /**
   * 고른 것을 적용하고 다시 그립니다. **한 박자 뒤입니다.**
   *
   * `onChange` 가 말이 바뀌었으면 화면 전체를 다시 그리고, 그 안에 이 판도 들어 있습니다 —
   * 그러면 **지금 눌린 칸이 그 눌림을 처리하는 중에 지워집니다.** 그다음 차례를 기다리던
   * 것이 없어진 객체를 만나고, 화면이 거기서 멈춥니다.
   *
   * 말을 바꾸었을 때만 나는 일이지만, 미루는 데 드는 것이 없으므로 셋 다 미룹니다.
   */
  private applyLater(): void {
    queueMicrotask(() => {
      this.onChange()
      this.draw()
    })
  }

  /**
   * 글을 다시 읽습니다.
   *
   * **말을 바꾼 그 자리에서 바뀌어야 합니다.** 이 판에서 말을 고르므로, 다시 그리지 않으면
   * 고른 사람이 바뀌었는지를 이 화면에서 확인할 수 없습니다.
   */
  relabel(): void {
    // **가장 긴 탭이 판의 높이를 정합니다.** 탭마다 다르게 하면 옮길 때마다 판이 들썩이고,
    // 못박아 두면 말을 바꿨을 때 마지막 줄이 판 밖으로 나갑니다.
    this.height = Math.min(
      MAX_HEIGHT, Math.max(MIN_HEIGHT, ...this.tabs().map(tab => this.measure(tab))))

    // **제 것을 지우지 않습니다.** 판의 자식은 매번 새로 만드는 틀 하나와, 이 판이 처음부터
    // 끝까지 들고 있는 것 넷입니다 — 넷까지 함께 지우면 그 뒤로 이 판은 지워진 것들을
    // 다시 붙이고 그것들에 그리려 합니다. 지워진 컨테이너는 속이 비어 있어서 마스크를
    // 거는 그 자리에서 예외가 나고, **말을 바꾸는 첫 번째 순간에 화면이 통째로 멈췄습니다.**
    //
    // 처음 세울 때는 판이 비어 있어 아무것도 지워지지 않으므로, 이 결함은 두 번째 호출부터
    // 나타납니다 — 그리고 이 함수를 두 번 부르는 것은 말을 바꾸는 것뿐입니다.
    this.view.removeChildren()
    this.frame?.destroy({ children: true })
    this.frame = panelFrame(WIDTH, this.height, t('ui.button.options'), () => this.onClose())
    this.view.addChild(this.frame, this.tabRow, this.viewport, this.bar, this.tip)
    // **본문은 창 안에서 움직입니다.** 창을 판보다 작게 두고 그 밖으로 나간 줄은 자릅니다 —
    // 자르지 않으면 굴러간 줄이 머리띠와 밑단 위에 그려집니다.
    this.viewport.addChild(this.body)
    this.viewport.mask = this.clip
    this.viewport.addChild(this.clip)
    this.buildTabs()
    this.draw()
  }

  /** 그 탭이 쓰는 높이. **그리지 않고 재기만 합니다.** */
  /**
   * 고를 수 있는 세트들.
   *
   * **판을 세울 때 한 번 읽습니다.** 표는 판이 도는 동안 바뀌지 않고, 매번 정렬하면 탭을
   * 그릴 때마다 15줄을 다시 셉니다.
   */
  private readonly sets: { setId: string; name: string; credit?: string }[] = []
  /** 세트마다의 겉모습. 미리보기를 그릴 때마다 표를 다시 읽지 않습니다. */
  private readonly looks = new Map<string, SetLook>()
  /**
   * 그림의 출처가 뜨는 쪽지.
   *
   * **판 안의 쪽지와 같은 것입니다.** 옵션에만 따로 만들면 모습이 두 가지가 됩니다.
   */
  private readonly tip = new Tooltip()

  private setName(setId: string): string {
    return this.sets.find(one => one.setId === setId)?.name ?? setId
  }

  private measure(tab: Tab): number {
    let y = TAB_Y + TAB_H + 34
    for (const row of tab.rows) {
      if (row.seed) {
        y += SEED_ROW
        continue
      }
      if (row.cards !== undefined) {
        const lines = Math.ceil(row.cards.length / CARD_COLUMNS)
        y += 50 + lines * (CARD_ROW_H + CHOICE_GAP)
        continue
      }
      if (row.choices === undefined) {
        y += ROW
        continue
      }
      const lines = Math.ceil(row.choices.length / CHOICE_COLUMNS)
      y += 50 + lines * (CHOICE_H + CHOICE_GAP)
    }
    return y + 18 + FOOTER_BAR
  }

  private tabs(): Tab[] {
    const options = this.options
    const flip = (key: 'sound' | 'music' | 'shake' | 'particles' | 'chromatic' | 'hints') => () => {
      options[key] = !options[key]
    }
    const onOff = (key: 'sound' | 'music' | 'shake' | 'particles' | 'chromatic' | 'hints') =>
      () => (options[key] ? t('ui.option.on') : t('ui.option.off'))

    return [
      // **언어는 「일반」 의 첫 줄입니다.** 이 화면에서 사람이 가장 먼저 찾는 것이고,
      // 「소리」나 「화면」 아래에 두면 그 둘을 다 열어 본 뒤에야 찾습니다.
      {
        id: 'general',
        name: t('ui.tab.general'),
        rows: [
          {
            id: 'language',
            label: t('ui.option.language'),
            note: t('ui.option.note.language'),
            read: () => LANGUAGE_NAMES[chosen(options)],
            next: () => undefined,
            // **그 말로 적습니다** — 찾는 사람이 그 말의 사람입니다.
            choices: LANGUAGES.map(one => ({ key: one, label: LANGUAGE_NAMES[one] })),
            current: () => chosen(options),
            pick: (key: string) => { options.language = key as Language },
          },
        ],
      },
      {
        id: 'sound',
        name: t('ui.tab.sound'),
        rows: [
          { label: t('ui.tab.sound'), read: onOff('sound'), next: flip('sound') },
          {
            label: t('ui.option.volume'),
            read: () => `${options.volume}`,
            // 0 에서 100 까지 20씩. **끄는 자리는 위에 있습니다** — 음량 0 과 소리 꺼짐은
            // 다른 것이고, 둘을 한 줄에 두면 어느 쪽으로 껐는지 알 수 없습니다.
            next: () => { options.volume = options.volume >= 100 ? 20 : options.volume + 20 },
          },
          { label: t('ui.option.music'), read: onOff('music'), next: flip('music') },
          {
            label: t('ui.option.music_volume'),
            read: () => `${options.musicVolume}`,
            next: () => {
              options.musicVolume = options.musicVolume >= 100 ? 20 : options.musicVolume + 20
            },
          },
        ],
      },
      {
        id: 'video',
        name: t('ui.tab.video'),
        rows: [
          {
            label: t('ui.option.shake'), read: onOff('shake'), next: flip('shake'),
            note: t('ui.option.note.shake'),
          },
          {
            label: t('ui.option.particles'), read: onOff('particles'), next: flip('particles'),
            note: t('ui.option.note.particles'),
          },
          {
            label: t('ui.option.chromatic'), read: onOff('chromatic'), next: flip('chromatic'),
            note: t('ui.option.note.chromatic'),
          },
        ],
      },
      // **카드가 「화면」 과 따로입니다.** 화면의 나머지는 켜고 끄는 것이고 이것은 고르는
      // 것이며, 세트가 늘어나면 여기에 미리보기가 들어옵니다.
      {
        id: 'cards',
        name: t('ui.tab.cards'),
        rows: [
          {
            label: t('ui.option.cardSet'),
            note: t('ui.option.note.cardSet'),
            read: () => this.setName(options.cardSet),
            next: () => undefined,
            cards: this.sets.map(one => ({ key: one.setId, label: one.name })),
            current: () => options.cardSet,
            pick: (key: string) => { options.cardSet = key },
          },
        ],
      },
      {
        id: 'game',
        name: t('ui.tab.game'),
        rows: [
          {
            label: t('ui.option.speed'),
            read: () => tf('ui.option.speed_value', { n: options.speed }),
            next: () => { options.speed = options.speed >= 4 ? 1 : options.speed * 2 },
            note: t('ui.option.note.speed'),
          },
          {
            label: t('ui.option.hints'), read: onOff('hints'), next: flip('hints'),
            note: t('ui.option.note.hints'),
          },
        ],
      },
      {
        id: 'seed',
        name: t('ui.title.seed'),
        rows: [{
          label: t('ui.title.seed'),
          read: () => this.seedText,
          next: () => undefined,
          note: this.seedEditable ? t('ui.title.seed_note') : t('ui.seed.locked'),
          seed: true,
        }],
      },
    ]
  }

  /**
   * 탭 줄.
   *
   * **고른 탭은 본문과 이어집니다.** 버튼 셋을 나란히 두고 하나만 밝게 하면 그것은 탭이
   * 아니라 눌린 버튼이고, 아래의 내용이 그 버튼에 딸린 것으로 읽히지 않습니다. 그래서 고른
   * 탭은 아래 선을 지우고 본문과 같은 바탕을 씁니다.
   */
  /**
   * 탭 줄.
   *
   * **고른 탭과 본문은 도형 하나입니다.** 둘을 따로 그리면 맞닿는 자리마다 이음매가
   * 생깁니다 — 선의 굵기가 어긋나 보이고, 탭의 세로선이 본문의 윗변 아래로 삐져나옵니다.
   * 길 하나를 채우고 그 길 하나를 두르면 이음매가 있을 자리가 없습니다.
   *
   * 고르지 않은 탭은 **뒤에 먼저** 깔립니다. 아랫단이 본문의 바탕에 덮여 사라지므로, 그
   * 끝을 따로 다듬지 않아도 됩니다.
   */
  private buildTabs(): void {
    this.tabRow.removeChildren().forEach(child => child.destroy())
    this.tabNodes.clear()
    const all = this.tabs()
    const names = all.map(tab => tab.name)
    const ruleY = TAB_Y + TAB_H + 10
    const pageL = 24
    const pageR = WIDTH - 24
    // 탭 줄은 본문과 같은 폭입니다. 그 안에서 고르게 나눕니다.
    const left = pageL
    const step = (pageR - pageL) / names.length
    const pageB = ruleY + (this.height - FOOTER_BAR - ruleY - 16)
    // 탭 위의 모서리와 본문 아래의 모서리.
    const tr = 9
    const pr = 10
    const tabW = step - 4

    const g = new Graphics()

    // 1. 고르지 않은 탭. 한 단 내려가 있고, 아랫단은 곧 본문에 덮입니다.
    names.forEach((_name, index) => {
      if (index === this.tab) return
      const x = left + index * step + 2
      const top = TAB_Y + 6
      g.moveTo(x, ruleY + 4)
        .lineTo(x, top + tr)
        .quadraticCurveTo(x, top, x + tr, top)
        .lineTo(x + tabW - tr, top)
        .quadraticCurveTo(x + tabW, top, x + tabW, top + tr)
        .lineTo(x + tabW, ruleY + 4)
        .closePath()
        .fill(0x141a24)
      g.moveTo(x, ruleY + 4)
        .lineTo(x, top + tr)
        .quadraticCurveTo(x, top, x + tr, top)
        .lineTo(x + tabW - tr, top)
        .quadraticCurveTo(x + tabW, top, x + tabW, top + tr)
        .lineTo(x + tabW, ruleY + 4)
        .stroke({ color: 0x2b3646, width: 1.5 })
    })

    // 2. 고른 탭과 본문. **길 하나입니다.**
    const sx = left + this.tab * step + 2
    const merged = (target: Graphics) => {
      target.moveTo(pageL, ruleY)
        .lineTo(sx, ruleY)
        .lineTo(sx, TAB_Y + tr)
        .quadraticCurveTo(sx, TAB_Y, sx + tr, TAB_Y)
        .lineTo(sx + tabW - tr, TAB_Y)
        .quadraticCurveTo(sx + tabW, TAB_Y, sx + tabW, TAB_Y + tr)
        .lineTo(sx + tabW, ruleY)
        .lineTo(pageR, ruleY)
        .lineTo(pageR, pageB - pr)
        .quadraticCurveTo(pageR, pageB, pageR - pr, pageB)
        .lineTo(pageL + pr, pageB)
        .quadraticCurveTo(pageL, pageB, pageL, pageB - pr)
        .closePath()
    }
    merged(g)
    g.fill(BODY)
    merged(g)
    g.stroke({ color: EDGE, width: 1.5 })

    this.tabRow.addChild(g)

    names.forEach((name, index) => {
      const x = left + index * step + 2
      const chosen = index === this.tab
      const top = TAB_Y + (chosen ? 0 : 6)
      const label = new Text({
        text: name,
        style: {
          fontSize: 14, fill: chosen ? COLOR.ink : COLOR.inkDim,
          fontWeight: chosen ? '800' : '700',
        },
      })
      label.anchor.set(0.5, 0.5)
      label.position.set(x + tabW / 2, top + TAB_H / 2)

      const hit = new Container()
      hit.addChild(label)
      hit.eventMode = 'static'
      hit.hitArea = new Rectangle(x, top, tabW, ruleY - top)
      hit.cursor = 'pointer'
      hit.on('pointertap', () => {
        if (this.tab === index) return
        this.tab = index
        this.buildTabs()
        this.draw()
      })
      this.tabRow.addChild(hit)
      // 도구가 짚을 자리. **누르는 칸의 가운데입니다** — 고른 탭과 그렇지 않은 탭이 한 단
      // 어긋나 있으므로, 위쪽 끝이 아니라 가운데를 넘겨야 둘 다 맞습니다.
      const id = all[index]?.id
      if (id !== undefined) {
        this.tabNodes.set(`tab:${id}`,
                          { node: this.tabRow, cx: x + tabW / 2, cy: top + TAB_H / 2 })
      }
    })
  }

  /** 창의 위와 아래. 탭 줄 밑에서 밑단 위까지입니다. */
  private get windowTop(): number {
    return TAB_Y + TAB_H + 10
  }

  private get windowHeight(): number {
    return this.height - FOOTER_BAR - this.windowTop - 10
  }

  /**
   * 굴러갈 것이 얼마인지 재고 손잡이를 세웁니다.
   *
   * **넘치지 않으면 손잡이가 없습니다.** 늘 서 있으면 굴릴 것이 없는 탭에서도 굴릴 수 있는
   * 것으로 보입니다.
   */
  private fitScroll(content: number): void {
    this.over = Math.max(0, content - this.windowHeight)
    this.scroll = Math.max(-this.over, Math.min(0, this.scroll))
    this.body.y = this.scroll

    this.clip.clear()
    this.clip.rect(6, this.windowTop, WIDTH - 12, this.windowHeight).fill(0xffffff)

    this.bar.clear()
    if (this.over <= 0) return

    const track = this.windowHeight - 8
    const height = Math.max(34, track * (this.windowHeight / content))
    const at = this.over === 0 ? 0 : (-this.scroll / this.over) * (track - height)
    this.bar.roundRect(WIDTH - 16, this.windowTop + 4, 4, track, 2)
      .fill({ color: 0xffffff, alpha: 0.07 })
    this.bar.roundRect(WIDTH - 16, this.windowTop + 4 + at, 4, height, 2)
      .fill({ color: 0xffffff, alpha: 0.30 })
  }

  private draw(): void {
    this.body.removeChildren().forEach(child => child.destroy())
    // **지워진 칸을 표에 남기지 않습니다.** 도구가 그것을 짚으면 없는 것의 자리를 묻습니다.
    this.choiceNodes.clear()

    const rows = this.tabs()[this.tab]?.rows ?? []
    let y = TAB_Y + TAB_H + 34

    for (const row of rows) {
      const label = new Text({
        text: row.label,
        style: { fontSize: 15, fill: COLOR.ink, fontWeight: '700' },
      })
      label.position.set(44, y + 4)
      this.body.addChild(label)

      if (row.note !== undefined) {
        const note = richLine(row.note, RICH, WIDTH - 220, 14)
        note.position.set(44, y + 24)
        this.body.addChild(note)
      }

      if (row.seed) {
        // 설명 줄 아래입니다 — `y + 24` 에 설명이 서므로 그보다 내려야 겹치지 않습니다.
        y += this.drawSeed(y + 46)
        continue
      }

      if (row.cards !== undefined) {
        y += this.drawCardChoices(row, y + 50)
        continue
      }

      if (row.choices === undefined) {
        const value = new Button(row.read(), 128, 34, 0x3a4658, () => {
          row.next()
          this.applyLater()
        })
        value.position.set(WIDTH - 172, y)
        this.body.addChild(value)
        y += ROW
        continue
      }

      y += this.drawChoices(row, y + 50)
    }

    // **그린 뒤에 잽니다.** 줄의 높이가 글의 길이에 달려 있으므로 그리기 전에는 알 수
    // 없습니다 — `measure` 는 판의 높이를 정하는 어림이고 이것이 실제입니다.
    this.fitScroll(y + 18 - this.windowTop)
  }

  /**
   * 시드를 적는 줄.
   *
   * **판 밖에서만 고칠 수 있습니다.** 도는 중에는 지금 시드를 읽기만 합니다 — 바꾸면
   * 보고 있는 패와 적힌 시드가 어긋납니다.
   */
  private drawSeed(top: number): number {
    const width = WIDTH - 88
    const fieldW = width - 108
    const height = 38

    const plate = new Graphics()
    plate.roundRect(44, top, fieldW, height, 8)
      .fill({ color: 0x121a26, alpha: this.seedEditable ? 0.92 : 0.5 })
    plate.roundRect(44.5, top + 0.5, fieldW - 1, height - 1, 8)
      .stroke({
        color: this.editing ? COLOR.good : COLOR.panelEdge,
        width: 1.5, alpha: this.seedEditable ? 0.9 : 0.4,
      })
    if (this.seedEditable) {
      plate.eventMode = 'static'
      plate.cursor = 'text'
      plate.on('pointertap', event => {
        event.stopPropagation()
        if (this.editing) return
        this.editing = true
        this.buffer = this.seedText
        this.draw()
      })
    }
    this.body.addChild(plate)
    // 적는 칸의 자리를 도구에 알립니다. **판의 높이가 말에 따라 달라지므로** 도구가
    // 좌표를 못박으면 다른 말에서는 빈자리를 누릅니다.
    this.choiceNodes.set('field:seed',
                         { node: this.body, cx: 44 + fieldW / 2, cy: top + height / 2 })

    const value = new Text({
      text: this.editing ? this.buffer : this.seedText,
      style: {
        fontSize: 15, fill: this.seedEditable ? COLOR.ink : COLOR.inkDim,
        fontWeight: '800', letterSpacing: 1,
      },
    })
    value.anchor.set(0, 0.5)
    value.position.set(58, top + height / 2)
    value.eventMode = 'none'
    this.body.addChild(value)

    if (this.editing) {
      const caret = new Graphics()
      caret.rect(value.x + value.width + 2, top + 9, 2, height - 18)
        .fill({ color: COLOR.ink, alpha: 0.9 })
      caret.eventMode = 'none'
      this.body.addChild(caret)
    }

    if (this.seedEditable) {
      const dice = new Button(t('ui.button.random'), 96, height, 0x3a4658, () => {
        this.editing = false
        this.buffer = ''
        this.seedText = randomSeed()
        this.onSeed?.(this.seedText)
        this.draw()
      })
      dice.position.set(44 + fieldW + 12, top)
      this.body.addChild(dice)
    }

    return SEED_ROW
  }

  /**
   * 고를 것들을 격자로 세웁니다.
   *
   * 세 칸씩 놓습니다. 한 줄에 여섯을 세우면 글씨가 작아지고, 한 줄에 하나면 아래로 길어져
   * 판이 그만큼 커집니다.
   */
  /**
   * 겉모습을 고르는 줄.
   *
   * 칸마다 그 벌의 카드 넉 장을 그립니다. **그 벌의 색으로 그립니다** — 지금 고른 벌의
   * 색으로 그리면 셋이 같아 보이고, 그러면 글자 단추와 다를 것이 없습니다.
   */
  private drawCardChoices(row: Row, top: number): number {
    const cards = row.cards ?? []
    const now = row.current?.()
    const gap = CHOICE_GAP
    const columns = CARD_COLUMNS
    const width = Math.floor((WIDTH - 88 - gap * (columns - 1)) / columns)
    const lines = Math.ceil(cards.length / columns)

    cards.forEach((one, index) => {
      const look = this.looks.get(one.key)
      const here = one.key === now
      const cell = new Container()
      cell.position.set(44 + (index % columns) * (width + gap),
                        top + Math.floor(index / columns) * (CARD_ROW_H + gap))

      const board = new Graphics()
      board.roundRect(0, 0, width, CARD_ROW_H, 8)
        .fill({ color: here ? 0x1d3a26 : 0x252b36 })
        .stroke({ color: here ? COLOR.good : 0x3a4658, width: here ? 3 : 2 })
      cell.addChild(board)

      // 넉 장이 어긋나 겹쳐 섭니다. 왼쪽 위가 첫 장입니다.
      const fan = CARD_W + CARD_STEP * (PREVIEW.length - 1)
      PREVIEW.forEach((want, at) => {
        const card = this.previewCard(look, want.suit, want.rank)
        card.position.set((width - fan) / 2 + at * CARD_STEP, 8)
        cell.addChild(card)
      })

      const name = new Text({
        text: one.label,
        style: {
          fontSize: 12, fill: here ? COLOR.ink : COLOR.inkDim, fontWeight: '800',
          wordWrap: true, wordWrapWidth: width - 10, align: 'center', breakWords: true,
          lineHeight: 13,
        },
      })
      name.anchor.set(0.5, 0)
      name.position.set(width / 2, CARD_H + 14)
      cell.addChild(name)

      cell.eventMode = 'static'
      cell.cursor = 'pointer'
      cell.on('pointertap', () => {
        row.pick?.(one.key)
        this.applyLater()
      })
      // **출처는 쪽지로 뜹니다.** 판에 한 줄로 적어 두면 자작 세트에서는 빈 줄이 되고,
      // 정본 하나에만 있는 글이 줄 하나를 늘 차지합니다.
      const credit = this.sets.find(two => two.setId === one.key)?.credit
      if (credit !== undefined) {
        cell.on('pointerover', () => this.tip.show(
          one.label, '', 0, [credit],
          cell.x + width / 2, cell.y + CARD_ROW_H, this.size))
        cell.on('pointerout', () => this.tip.hide())
      }
      this.body.addChild(cell)
    })

    return 50 + lines * (CARD_ROW_H + gap)
  }

  /**
   * 미리보기 카드 한 장.
   *
   * **그림이 있으면 그림이 얼굴입니다.** 없으면 그 벌의 색으로 문양을 그립니다 — 판에서
   * 카드를 그리는 것과 같은 순서이므로, 여기서 맞으면 판에서도 맞습니다.
   */
  private previewCard(look: SetLook | undefined, suit: SuitKind, rank: number): Container {
    const node = new Container()
    const ink = look?.ink[suit] ?? COLOR.black
    const paper = new Graphics()
    paper.roundRect(0, 0, CARD_W, CARD_H, 4).fill(look?.paper ?? COLOR.cardFace)
    paper.roundRect(0.5, 0.5, CARD_W - 1, CARD_H - 1, 4)
      .stroke({ color: COLOR.cardEdge, width: 1 })
    node.addChild(paper)

    const texture = look?.artDir === undefined
      ? undefined : artFor(look.artDir, cardArtId(suit, rank))
    const face = new Graphics()
    if (texture) {
      const picture = new Sprite(texture)
      picture.width = CARD_W
      picture.height = CARD_H
      node.addChild(picture)
    } else {
      drawFace(face, suit, rank, CARD_W, CARD_H, ink)
    }

    // **판에서 그리는 것과 같은 순서입니다.** 모서리를 그림 위에 그리는 벌은 여기서도
    // 그려야 하고, 그러지 않으면 미리보기가 실제와 다른 카드가 됩니다.
    if (texture === undefined || look?.artHasIndex === false) {
      const scale = CARD_H / SIZE.cardHeight
      const mark = new Text({
        text: RANK_TEXT[rank] ?? '?',
        style: { fontSize: Math.round(19 * scale), fill: ink, fontWeight: '800' },
      })
      mark.position.set(Math.round(8 * scale), Math.round(5 * scale))
      node.addChild(mark)
      drawSuit(face, suit, 14 * scale, 33 * scale, 12 * scale, ink)
    }
    node.addChild(face)
    return node
  }

  private drawChoices(row: Row, top: number): number {
    const choices = row.choices ?? []
    const now = row.current?.()
    const columns = CHOICE_COLUMNS
    const gap = CHOICE_GAP
    const width = Math.floor((WIDTH - 88 - gap * (columns - 1)) / columns)
    const height = CHOICE_H

    choices.forEach((choice, index) => {
      const column = index % columns
      const line = Math.floor(index / columns)
      const button = new Button(choice.label, width, height,
        choice.key === now ? 0x2f6f52 : 0x3a4658, () => {
          row.pick?.(choice.key)
          this.applyLater()
        })
      button.highlight = choice.key === now
      button.position.set(44 + column * (width + gap), top + line * (height + gap))
      this.body.addChild(button)
      // 이름이 붙은 줄은 도구가 짚을 수 있게 칸의 가운데를 남깁니다.
      if (row.id !== undefined) {
        this.choiceNodes.set(`${row.id}:${choice.key}`,
                             { node: button, cx: width / 2, cy: height / 2 })
      }
    })

    const lines = Math.ceil(choices.length / columns)
    return 50 + lines * (height + gap)
  }
}
