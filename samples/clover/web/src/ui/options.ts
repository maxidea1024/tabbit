// 옵션.
//
// **여기 있는 것은 전부 실제로 무언가를 합니다.** 켜고 끄는 자리를 늘어놓고 아무것도 하지
// 않으면 그것은 옵션이 아니라 장식입니다.
//
// 갈래가 셋이라 탭입니다 — 한 줄로 늘어놓으면 무엇을 찾는지 알고 있어야 찾을 수 있고,
// 「소리가 크다」와 「연출이 느리다」는 서로 다른 자리에서 찾게 됩니다.

import { Container, Graphics, Rectangle, Text } from 'pixi.js'

import { detectLanguage, type Language, LANGUAGE_NAMES, LANGUAGES, t, tf } from '../core/strings'
import { COLOR } from '../render/theme'
import { FOOTER_BAR, panelFrame, TITLE_BAR, type ModalPanel } from './modal'
import { richLine, type RichStyle } from './rich'
import { Button } from './widgets'

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
}

export function defaultOptions(): Options {
  return {
    sound: true, volume: 60, speed: 1,
    shake: true, particles: true, chromatic: true, hints: true,
    language: '',
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
      if (typeof value === typeof options[key]) (options[key] as unknown) = value
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

const WIDTH = 520
const HEIGHT = 348 + FOOTER_BAR
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
  current?: () => string
  pick?: (key: string) => void
}

interface Tab {
  name: string
  rows: Row[]
}

export class OptionsPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  private readonly body = new Container()
  private readonly tabRow = new Container()
  private tab = 0

  constructor(private readonly options: Options,
              private readonly onChange: () => void,
              private readonly onClose: () => void) {
    this.relabel()
  }

  /**
   * 글을 다시 읽습니다.
   *
   * **말을 바꾼 그 자리에서 바뀌어야 합니다.** 이 판에서 말을 고르므로, 다시 그리지 않으면
   * 고른 사람이 바뀌었는지를 이 화면에서 확인할 수 없습니다.
   */
  relabel(): void {
    this.view.removeChildren().forEach(child => child.destroy())
    this.view.addChild(panelFrame(WIDTH, HEIGHT, t('ui.button.options'), () => this.onClose()),
      this.tabRow, this.body)
    this.buildTabs()
    this.draw()
  }

  private tabs(): Tab[] {
    const options = this.options
    const flip = (key: 'sound' | 'shake' | 'particles' | 'chromatic' | 'hints') => () => {
      options[key] = !options[key]
    }
    const onOff = (key: 'sound' | 'shake' | 'particles' | 'chromatic' | 'hints') =>
      () => (options[key] ? t('ui.option.on') : t('ui.option.off'))

    return [
      // **언어는 「일반」 의 첫 줄입니다.** 이 화면에서 사람이 가장 먼저 찾는 것이고,
      // 「소리」나 「화면」 아래에 두면 그 둘을 다 열어 본 뒤에야 찾습니다.
      {
        name: t('ui.tab.general'),
        rows: [
          {
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
        ],
      },
      {
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
      {
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
    const names = this.tabs().map(tab => tab.name)
    const ruleY = TAB_Y + TAB_H + 10
    const pageL = 24
    const pageR = WIDTH - 24
    // 탭 줄은 본문과 같은 폭입니다. 그 안에서 고르게 나눕니다.
    const left = pageL
    const step = (pageR - pageL) / names.length
    const pageB = ruleY + (HEIGHT - FOOTER_BAR - ruleY - 16)
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
    })
  }

  private draw(): void {
    this.body.removeChildren().forEach(child => child.destroy())

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

      if (row.choices === undefined) {
        const value = new Button(row.read(), 128, 34, 0x3a4658, () => {
          row.next()
          this.onChange()
          this.draw()
        })
        value.position.set(WIDTH - 172, y)
        this.body.addChild(value)
        y += ROW
        continue
      }

      y += this.drawChoices(row, y + 50)
    }
  }

  /**
   * 고를 것들을 격자로 세웁니다.
   *
   * 세 칸씩 놓습니다. 한 줄에 여섯을 세우면 글씨가 작아지고, 한 줄에 하나면 아래로 길어져
   * 판이 그만큼 커집니다.
   */
  private drawChoices(row: Row, top: number): number {
    const choices = row.choices ?? []
    const now = row.current?.()
    const columns = 3
    const gap = 12
    const width = Math.floor((WIDTH - 88 - gap * (columns - 1)) / columns)
    const height = 36

    choices.forEach((choice, index) => {
      const column = index % columns
      const line = Math.floor(index / columns)
      const button = new Button(choice.label, width, height,
        choice.key === now ? 0x2f6f52 : 0x3a4658, () => {
          row.pick?.(choice.key)
          this.onChange()
          this.draw()
        })
      button.highlight = choice.key === now
      button.position.set(44 + column * (width + gap), top + line * (height + gap))
      this.body.addChild(button)
    })

    const lines = Math.ceil(choices.length / columns)
    return 50 + lines * (height + gap)
  }
}
