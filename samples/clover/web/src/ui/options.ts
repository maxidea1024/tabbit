// 옵션.
//
// **여기 있는 것은 전부 실제로 무언가를 합니다.** 켜고 끄는 자리를 늘어놓고 아무것도 하지
// 않으면 그것은 옵션이 아니라 장식입니다.
//
// 갈래가 셋이라 탭입니다 — 한 줄로 늘어놓으면 무엇을 찾는지 알고 있어야 찾을 수 있고,
// 「소리가 크다」와 「연출이 느리다」는 서로 다른 자리에서 찾게 됩니다.

import type { PoolChoice } from '../core/pool'
import { Container, Graphics, Rectangle, Text } from 'pixi.js'

import { detectLanguage, type Language, LANGUAGE_NAMES, LANGUAGES, t, tf } from '../core/strings'
import { COLOR } from '../render/theme'
import { FOOTER_BAR, panelFrame, TITLE_BAR, type ModalPanel } from './modal'
import { richLine, type RichStyle } from './rich'
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
    language: '', pool: 'base',
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
/**
 * 판의 가장 낮은 높이.
 *
 * **글이 길면 그만큼 자랍니다.** 못박아 두면 말을 바꿨을 때 — 독일어가 한국어보다 깁니다 —
 * 마지막 줄이 판 밖으로 나갑니다. 낮은 값을 두는 것은 탭을 옮길 때마다 판이 들썩이지 않게
 * 하기 위한 것입니다.
 */
const MIN_HEIGHT = 348 + FOOTER_BAR
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
  /** 글을 적는 줄. 지금은 시드 하나뿐입니다. */
  seed?: true
}

interface Tab {
  name: string
  rows: Row[]
}

export class OptionsPanel implements ModalPanel {
  readonly view = new Container()
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

  constructor(private readonly options: Options,
              private readonly onChange: () => void,
              private readonly onClose: () => void) {
    this.relabel()

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
   * 글을 다시 읽습니다.
   *
   * **말을 바꾼 그 자리에서 바뀌어야 합니다.** 이 판에서 말을 고르므로, 다시 그리지 않으면
   * 고른 사람이 바뀌었는지를 이 화면에서 확인할 수 없습니다.
   */
  relabel(): void {
    // **가장 긴 탭이 판의 높이를 정합니다.** 탭마다 다르게 하면 옮길 때마다 판이 들썩이고,
    // 못박아 두면 말을 바꿨을 때 마지막 줄이 판 밖으로 나갑니다.
    this.height = Math.max(MIN_HEIGHT, ...this.tabs().map(tab => this.measure(tab)))

    this.view.removeChildren().forEach(child => child.destroy())
    this.view.addChild(
      panelFrame(WIDTH, this.height, t('ui.button.options'), () => this.onClose()),
      this.tabRow, this.body)
    this.buildTabs()
    this.draw()
  }

  /** 그 탭이 쓰는 높이. **그리지 않고 재기만 합니다.** */
  private measure(tab: Tab): number {
    let y = TAB_Y + TAB_H + 34
    for (const row of tab.rows) {
      if (row.seed) {
        y += SEED_ROW
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
      {
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
    const names = this.tabs().map(tab => tab.name)
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

      if (row.seed) {
        // 설명 줄 아래입니다 — `y + 24` 에 설명이 서므로 그보다 내려야 겹치지 않습니다.
        y += this.drawSeed(y + 46)
        continue
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
