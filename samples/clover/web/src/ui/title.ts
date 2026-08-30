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

import { COLOR, SIZE } from '../render/theme'
import { Button } from './widgets'

export interface Options {
  /** 소리를 내는가. */
  sound: boolean
  /** 연출의 배속. 1 · 2 · 4 중 하나입니다. */
  speed: number
}

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
    text: '포커 로그라이크 한 편',
    style: { fontSize: 20, fill: COLOR.ink, fontWeight: '700', letterSpacing: 4 },
  })
  private readonly note = new Text({
    text: '규칙과 수치는 시트에 있고, 이 화면은 그것을 읽는 한쪽 구현입니다.',
    style: { fontSize: 13, fill: COLOR.inkDim },
  })
  private readonly seedText = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
  })

  /** 클로버 잎. 이름 위에서 천천히 흔들립니다. */
  private readonly leaf = new Graphics()
  private time = 0

  /** 옵션 판. 버튼을 누르면 아래에서 펼쳐집니다. */
  private readonly options = new Container()
  private optionsOpen = false
  private soundButton!: Button
  private speedButton!: Button

  constructor(seed: string, private readonly settings: Options,
              onStart: () => void, onGuide: () => void) {
    super()

    // **배경은 화면 전체입니다.** 기준 넓이 밖까지 덮어야 창이 넓을 때 옆이 뚫리지 않습니다.
    this.ground.rect(-SIZE.width, -SIZE.height, SIZE.width * 3, SIZE.height * 3)
      .fill({ color: 0x070b12, alpha: 0.92 })

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
    const start = new Button('시작', bw, 54, 0x2f8f52, onStart)
    start.position.set(bx, 446)
    const guide = new Button('게임 방법', bw, 44, 0x3a4658, onGuide)
    guide.position.set(bx, 512)
    const option = new Button('옵션', bw, 44, 0x3a4658, () => this.toggleOptions())
    option.position.set(bx, 568)

    this.seedText.text = `시드  ${seed}`
    this.seedText.anchor.set(0.5, 0)
    this.seedText.position.set(SIZE.width / 2, SIZE.height - 42)

    this.buildOptions()

    this.addChild(this.ground, this.leaf, this.logo, this.tagline, this.note,
      start, guide, option, this.options, this.seedText)

    // 뒤를 눌러도 아무 일도 없습니다. **시작은 눌러서 시작하는 것입니다.**
    this.eventMode = 'static'
    this.on('pointertap', () => undefined)
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

  /**
   * 옵션.
   *
   * **둘뿐입니다** — 소리와 연출 속도. 이 둘이 사람마다 다른 것이고 나머지는 아닙니다.
   */
  private buildOptions(): void {
    const w = 300
    const h = 128
    const x = SIZE.width / 2 - w / 2
    const y = 620

    const plate = new Graphics()
    plate.roundRect(0, 0, w, h, 12).fill({ color: 0x131a25, alpha: 0.97 })
    plate.roundRect(0.5, 0.5, w - 1, h - 1, 12)
      .stroke({ color: COLOR.panelEdge, width: 2 })

    const soundLabel = new Text({
      text: '소리', style: { fontSize: 13, fill: COLOR.inkDim, fontWeight: '700' },
    })
    soundLabel.position.set(20, 26)
    const speedLabel = new Text({
      text: '연출 속도', style: { fontSize: 13, fill: COLOR.inkDim, fontWeight: '700' },
    })
    speedLabel.position.set(20, 78)

    this.soundButton = new Button('', 140, 34, 0x3a4658, () => {
      this.settings.sound = !this.settings.sound
      this.syncOptions()
    })
    this.soundButton.position.set(w - 160, 18)

    this.speedButton = new Button('', 140, 34, 0x3a4658, () => {
      // 1 → 2 → 4 → 1. **끄는 자리는 없습니다** — 연출이 없으면 이 게임이 아닙니다.
      this.settings.speed = this.settings.speed >= 4 ? 1 : this.settings.speed * 2
      this.syncOptions()
    })
    this.speedButton.position.set(w - 160, 70)

    this.options.addChild(plate, soundLabel, speedLabel, this.soundButton, this.speedButton)
    this.options.position.set(x, y)
    this.options.visible = false
    this.syncOptions()
  }

  private toggleOptions(): void {
    this.optionsOpen = !this.optionsOpen
    this.options.visible = this.optionsOpen
  }

  private syncOptions(): void {
    this.soundButton.text = this.settings.sound ? '켜짐' : '꺼짐'
    this.speedButton.text = `${this.settings.speed}배`
  }

  advance(seconds: number): void {
    if (!this.visible) return
    this.time += seconds
    this.leaf.rotation = Math.sin(this.time * 0.8) * 0.16
    this.leaf.scale.set(1 + Math.sin(this.time * 1.6) * 0.04)
  }
}
