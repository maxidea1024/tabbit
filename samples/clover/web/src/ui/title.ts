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
import { Button, IconButton } from './widgets'

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
  /** 클로버 잎. 이름 위에서 천천히 흔들립니다. */
  private readonly leaf = new Graphics()
  private time = 0
  /** 글을 다시 읽어야 하는 것들. 말이 바뀌면 이 셋을 갈아 끼웁니다. */
  private readonly buttons: { key: string; button: Button }[] = []

  constructor(onStart: () => void, onGuide: () => void, onOptions: () => void) {
    super()

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

    // **누를 것은 하나입니다.** 셋이 같은 크기로 나란히 서 있으면 무엇을 눌러야 하는지가
    // 셋 중에서 골라야 하는 일이 되고, 손가락으로는 잘못 누르는 일까지 생깁니다 — 시작
    // 하나만 판 가운데에 크게 두고, 나머지 둘은 구석의 아이콘으로 갑니다.
    const bw = 236
    const bh = 72
    const start = new Button(t('ui.button.start'), bw, bh, 0x2f8f52, onStart, 30)
    start.position.set(SIZE.width / 2 - bw / 2, 452)
    this.buttons.push({ key: 'ui.button.start', button: start })

    // 게임 방법과 옵션. **오른쪽 아래 구석입니다** — 판을 여는 것이 아니라 판 바깥의
    // 일이므로, 가운데의 줄에서 빠져 나와야 시작이 혼자 남습니다.
    const icon = 58
    const gap = 14
    const edge = 30
    const iconY = SIZE.height - edge - icon
    const guide = new IconButton(icon, 'circle-question-mark', onGuide)
    guide.position.set(SIZE.width - edge - icon * 2 - gap, iconY)
    const option = new IconButton(icon, 'settings', onOptions)
    option.position.set(SIZE.width - edge - icon, iconY)

    this.addChild(this.leaf, this.logo, this.tagline, this.note, start, guide, option)

    this.relabel()

    // 뒤를 눌러도 아무 일도 없습니다. **시작은 눌러서 시작하는 것입니다.**
    this.eventMode = 'static'
    this.on('pointertap', () => undefined)
  }

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
