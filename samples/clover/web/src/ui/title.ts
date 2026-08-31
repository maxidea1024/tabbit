// 타이틀.
//
// **게임은 타이틀에서 시작합니다.** 열자마자 판이 깔려 있으면 무엇을 하는 화면인지 읽을
// 자리가 없고, 시드를 확인하거나 게임 방법을 먼저 볼 자리도 없습니다.
//
// **판이 아니라 화면입니다.** 큰 이름 하나가 가운데 위에 서고 버튼이 아래에 줄지어 섭니다 —
// 상자 안에 담으면 게임 위에 뜬 대화창으로 보이고, 그러면 뒤에 이미 무언가가 돌고 있다는
// 뜻이 됩니다.
//
// 규칙은 모릅니다. 시작을 누르면 화면이 알아서 판을 폅니다.

import { Container, Graphics, Text } from 'pixi.js'
import { t } from '../core/strings'

import { COLOR, SIZE } from '../render/theme'
import { Button } from './widgets'

/** 시드 줄이 서는 자리. 버튼 셋 아래입니다. */
const SEED_Y = 634
const SEED_H = 36
/** 적을 수 있는 길이. 주소에 실려 나가므로 길게 둘 이유가 없습니다. */
const SEED_MAX = 28

/**
 * 무작위 시드 하나.
 *
 * **한 자리에 둡니다.** 처음 열 때 · 타이틀의 「무작위」 · 게임이 끝난 뒤 다시 시작할 때
 * 셋이 같은 모양이어야 시드만 보고 이 게임의 것인지 알 수 있습니다.
 */
export function randomSeed(): string {
  return `CLOVER-${Math.floor(Math.random() * 1e6).toString().padStart(6, '0')}`
}

export class Title extends Container {
  private readonly logo = new Text({
    text: 'clover',
    style: {
      fontSize: 128, fill: COLOR.good, fontWeight: '800',
      stroke: { color: 0x07130b, width: 12 },
      letterSpacing: 10,
    },
  })
  private readonly tagline = new Text({
    text: t('ui.title.tagline'),
    style: { fontSize: 20, fill: COLOR.ink, fontWeight: '700', letterSpacing: 4 },
  })
  private readonly note = new Text({
    text: t('ui.title.note'),
    style: { fontSize: 13, fill: COLOR.inkDim },
  })
  /**
   * 시드를 적는 자리.
   *
   * **시드는 판 하나를 정하는 문자열입니다.** 덱 섞기 · 상점 · 팩 · 확률 발동이 모두
   * 이 문자열에서 갈라져 나오므로, 같은 시드를 넣으면 같은 판이 나옵니다 — 남이 만난 판을
   * 그대로 만나 볼 수 있어야 합니다.
   */
  private readonly seedPlate = new Graphics()
  private readonly seedLabel = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
  })
  private readonly seedValue = new Text({
    text: '', style: { fontSize: 15, fill: COLOR.ink, fontWeight: '800', letterSpacing: 1 },
  })
  private readonly seedHint = new Text({
    text: '', style: { fontSize: 11, fill: COLOR.inkDim },
  })
  /** 시드가 무엇인지. **고르는 자리에 적혀 있어야 고를 수 있습니다.** */
  private readonly seedNote = new Text({
    text: '', style: { fontSize: 11, fill: 0x6f7d90 },
  })
  private readonly caret = new Graphics()
  /** 지금 적고 있는가. 적는 동안에는 키가 이 화면의 것입니다. */
  private editing = false
  /** 적는 동안의 글. 물러나면 버립니다. */
  private buffer = ''
  private seed: string
  /** 시드가 정해졌을 때. 화면이 판을 다시 만듭니다. */
  onSeed?: (seed: string) => void

  /** 클로버 잎. 이름 위에서 천천히 흔들립니다. */
  private readonly leaf = new Graphics()
  private time = 0
  /** 글을 다시 읽어야 하는 것들. 말이 바뀌면 이 셋을 갈아 끼웁니다. */
  private readonly buttons: { key: string; button: Button }[] = []

  constructor(seed: string, onStart: () => void, onGuide: () => void,
              onOptions: () => void) {
    super()
    this.seed = seed

    // **덮는 층이 없습니다.** 글이 읽히게 하려고 반투명 사각형을 얹으면 그 겹의 변이
    // 그대로 가로선으로 보입니다 — 어둡게 할 것은 배경이므로 배경을 어둡게 합니다.
    //
    // `game.ts` 의 `syncMood` 가 타이틀에서 그렇게 넘깁니다.

    this.drawLeaf()
    this.leaf.position.set(SIZE.width / 2, 132)

    this.logo.anchor.set(0.5, 0)
    this.logo.position.set(SIZE.width / 2, 176)

    this.tagline.anchor.set(0.5, 0)
    this.tagline.position.set(SIZE.width / 2, 320)

    this.note.anchor.set(0.5, 0)
    this.note.position.set(SIZE.width / 2, 356)

    // 버튼은 **아래에 세로로** 섭니다. 눈이 이름에서 한 번 내려오면 그다음은 순서대로입니다.
    const bw = 236
    const bx = SIZE.width / 2 - bw / 2
    const start = new Button(t('ui.button.start'), bw, 54, 0x2f8f52, onStart)
    start.position.set(bx, 446)
    const guide = new Button(t('ui.button.guide'), bw, 44, 0x3a4658, onGuide)
    guide.position.set(bx, 512)
    const option = new Button(t('ui.button.options'), bw, 44, 0x3a4658, onOptions)
    option.position.set(bx, 568)
    this.buttons.push({ key: 'ui.button.start', button: start },
      { key: 'ui.button.guide', button: guide },
      { key: 'ui.button.options', button: option })

    // 시드 줄. 왼쪽이 적는 자리, 오른쪽이 무작위입니다.
    const dice = new Button(t('ui.button.random'), 86, 36, 0x3a4658, () => this.roll())
    dice.position.set(SIZE.width / 2 + 79, SEED_Y)
    this.buttons.push({ key: 'ui.button.random', button: dice })

    this.seedLabel.anchor.set(0, 0.5)
    this.seedLabel.position.set(SIZE.width / 2 - 165 + 14, SEED_Y + SEED_H / 2)

    this.seedValue.anchor.set(0, 0.5)
    this.seedValue.position.set(SIZE.width / 2 - 165 + 58, SEED_Y + SEED_H / 2)

    this.seedHint.anchor.set(0.5, 0)
    this.seedHint.position.set(SIZE.width / 2, SEED_Y + SEED_H + 12)

    this.seedNote.anchor.set(0.5, 0)
    this.seedNote.position.set(SIZE.width / 2, SEED_Y + SEED_H + 30)

    // **글자는 누르는 것을 받지 않습니다.** 받으면 칸 대신 글자가 맞은 것이 되고, 글자에는
    // 처리기가 없으므로 누른 것이 그대로 지나갑니다.
    for (const one of [this.seedLabel, this.seedValue, this.seedNote, this.seedHint, this.caret]) {
      one.eventMode = 'none'
    }

    this.seedPlate.eventMode = 'static'
    this.seedPlate.cursor = 'text'
    this.seedPlate.on('pointertap', event => {
      event.stopPropagation()
      this.beginEdit()
    })

    this.addChild(this.leaf, this.logo, this.tagline, this.note,
      start, guide, option, this.seedPlate, this.seedLabel, this.seedValue,
      this.caret, this.seedHint, this.seedNote, dice)

    this.relabel()
    this.drawSeed()

    // **적는 동안의 키는 이 화면의 것입니다.** 뒤에 있는 화면이 같은 키를 받으면 `Esc` 로
    // 판이 닫히거나 연출이 건너뛰어집니다.
    window.addEventListener('keydown', event => {
      if (!this.visible || !this.editing) return
      event.preventDefault()
      event.stopImmediatePropagation()
      this.typed(event.key)
    })
    window.addEventListener('paste', event => {
      if (!this.visible || !this.editing) return
      event.preventDefault()
      const text = event.clipboardData?.getData('text') ?? ''
      for (const ch of text) this.typed(ch)
    })

    // 뒤를 누르면 적던 것이 정해집니다. **시작은 눌러서 시작하는 것입니다.**
    this.eventMode = 'static'
    this.on('pointertap', () => this.commit())
  }

  /** 지금 시드를 적고 있는가. 화면이 키를 넘기기 전에 이것을 봅니다. */
  get typing(): boolean {
    return this.visible && this.editing
  }

  private beginEdit(): void {
    if (this.editing) return
    this.editing = true
    this.buffer = this.seed
    this.drawSeed()
  }

  /**
   * 적은 것을 정합니다.
   *
   * 비었으면 옛것을 그대로 둡니다 — 시드 없는 판은 없습니다.
   */
  private commit(): void {
    if (!this.editing) return
    this.editing = false
    const next = this.buffer.trim()
    this.buffer = ''
    if (next !== '' && next !== this.seed) {
      this.seed = next
      this.onSeed?.(next)
    }
    this.drawSeed()
  }

  private cancel(): void {
    this.editing = false
    this.buffer = ''
    this.drawSeed()
  }

  /** 무작위로 하나. 적던 중이었으면 그것을 버립니다. */
  private roll(): void {
    this.editing = false
    this.buffer = ''
    this.seed = randomSeed()
    this.onSeed?.(this.seed)
    this.drawSeed()
  }

  private typed(key: string): void {
    if (key === 'Enter') {
      this.commit()
      return
    }
    if (key === 'Escape') {
      this.cancel()
      return
    }
    if (key === 'Backspace') {
      this.buffer = this.buffer.slice(0, -1)
      this.drawSeed()
      return
    }
    // **글자와 숫자와 `-` `_` 만 받습니다.** 시드는 주소에 실려 나가므로 그 밖의 글자는
    // 옮겨 적히는 사이에 달라집니다.
    if (key.length !== 1 || !/[A-Za-z0-9\-_]/.test(key)) return
    if (this.buffer.length >= SEED_MAX) return
    this.buffer += key
    this.drawSeed()
  }

  /** 시드 줄을 그립니다. */
  private drawSeed(): void {
    const x = SIZE.width / 2 - 165
    const g = this.seedPlate
    g.clear()
    g.roundRect(x, SEED_Y, 236, SEED_H, 8).fill({ color: 0x121a26, alpha: 0.92 })
    g.roundRect(x + 0.5, SEED_Y + 0.5, 235, SEED_H - 1, 8)
      .stroke({ color: this.editing ? COLOR.good : COLOR.panelEdge, width: 1.5, alpha: 0.9 })

    this.seedValue.text = this.editing ? this.buffer : this.seed
    this.caret.clear()
    if (!this.editing) return
    const at = this.seedValue.x + this.seedValue.width + 2
    this.caret.rect(at, SEED_Y + 9, 2, SEED_H - 18).fill({ color: COLOR.ink, alpha: 0.9 })
  }

  /**
   * 글을 다시 읽습니다.
   *
   * **말을 바꾼 그 자리에서 바뀌어야 합니다.** 글은 만들 때 한 번 읽히므로, 다시 읽지
   * 않으면 이 화면은 다음에 열 때까지 옛 말로 남습니다.
   */
  relabel(): void {
    this.tagline.text = t('ui.title.tagline')
    this.note.text = t('ui.title.note')
    this.seedLabel.text = t('ui.title.seed')
    this.seedHint.text = t('ui.title.seed_hint')
    this.seedNote.text = t('ui.title.seed_note')
    for (const one of this.buttons) one.button.text = t(one.key)
  }

  /** 네 잎. 원 넷을 돌려 붙인 모양입니다. */
  private drawLeaf(): void {
    const g = this.leaf
    g.clear()
    for (let i = 0; i < 4; i++) {
      const angle = (Math.PI / 2) * i + Math.PI / 4
      g.circle(Math.cos(angle) * 19, Math.sin(angle) * 19, 16)
        .fill({ color: COLOR.good, alpha: 0.92 })
    }
    g.rect(-2, 16, 4, 28).fill({ color: 0x2f8f52 })
  }

  advance(seconds: number): void {
    if (!this.visible) return
    this.time += seconds
    this.leaf.rotation = Math.sin(this.time * 0.8) * 0.16
    this.leaf.scale.set(1 + Math.sin(this.time * 1.6) * 0.04)
    // 적는 자리의 막대가 깜박입니다. 적을 수 있다는 표시입니다.
    this.caret.alpha = this.editing ? (Math.sin(this.time * 7) > 0 ? 1 : 0.15) : 0
  }
}
