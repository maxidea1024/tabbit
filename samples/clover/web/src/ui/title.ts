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
import { t, tf } from '../core/strings'

import { COLOR, SIZE } from '../render/theme'
import { Button } from './widgets'

export class Title extends Container {
  private readonly ground = new Graphics()
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
  private readonly seedText = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
  })

  /** 클로버 잎. 이름 위에서 천천히 흔들립니다. */
  private readonly leaf = new Graphics()
  private time = 0
  /** 글을 다시 읽어야 하는 것들. 말이 바뀌면 이 셋을 갈아 끼웁니다. */
  private readonly buttons: { key: string; button: Button }[] = []

  constructor(seed: string, onStart: () => void, onGuide: () => void,
              onOptions: () => void) {
    super()

    // **배경이 비쳐야 합니다.** 판이 없는 화면이라 덮을 것이 없고, 흐르는 배경이 이 화면에
    // 남은 유일한 움직임입니다 — 진하게 덮으면 멈춰 있는 그림이 됩니다.
    //
    // 글이 앉는 아래쪽만 짙어집니다. 층 넷으로 나누면 띠가 보이지 않습니다.
    for (let i = 0; i < 4; i++) {
      this.ground.rect(-SIZE.width, SIZE.height * (0.30 + i * 0.10),
        SIZE.width * 3, SIZE.height * 2)
        .fill({ color: 0x060a11, alpha: 0.15 })
    }

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

    this.seedText.text = tf('ui.stat.seed', { seed })
    this.seedText.anchor.set(0.5, 0)
    this.seedText.position.set(SIZE.width / 2, SIZE.height - 42)

    this.addChild(this.ground, this.leaf, this.logo, this.tagline, this.note,
      start, guide, option, this.seedText)

    // 뒤를 눌러도 아무 일도 없습니다. **시작은 눌러서 시작하는 것입니다.**
    this.eventMode = 'static'
    this.on('pointertap', () => undefined)
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
  }
}
